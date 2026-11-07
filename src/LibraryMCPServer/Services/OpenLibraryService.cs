using System.Net;
using System.Text.Json;
using LibraryMCPServer.Data;
using LibraryMCPServer.Models;
using LibraryMCPServer.Utilities;
using LibraryMCPServer.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace LibraryMCPServer.Services;

public sealed class OpenLibraryService
{
    public static readonly string[] SeedGenres = LibraryGenreRules.AllowedGenres;

    private static readonly IReadOnlyDictionary<string, string[]> SearchQueries =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mystery"] = ["subject:mystery", "subject:detective mystery"],
            ["Romance"] = ["subject:romance novel", "subject:love stories"],
            ["Biography"] = ["subject:biography", "subject:autobiography memoir"],
            ["History"] = ["subject:history books", "subject:world history"],
            ["Fantasy"] = ["subject:fantasy fiction", "subject:epic fantasy"],
            ["Business"] = ["subject:business management", "subject:entrepreneurship finance"],
            ["Self-Help"] = ["subject:self-help personal development", "subject:self help"],
            ["Programming"] = ["subject:computer programming", "subject:software development", "subject:programming languages"],
            ["Technology"] = ["subject:technology computing", "subject:information technology", "subject:computer science technology", "subject:electronics", "subject:engineering technology", "subject:computers", "technology", "computers technology", "engineering technology"],
            ["Science Fiction"] = ["subject:science fiction", "subject:space opera", "subject:cyberpunk science fiction"]
        };

    private readonly HttpClient _httpClient;
    private readonly LibraryDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OpenLibraryService> _logger;
    private readonly bool _filterJuvenileAndAdultContent;
    private readonly int _defaultCopiesAvailable;
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTime _lastRequestUtc = DateTime.MinValue;
    private static readonly Dictionary<string, (DateTimeOffset Expires, JsonDocument Document)> LiveCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly OpenLibrarySettings _openLibrarySettings;

    public OpenLibraryService(HttpClient httpClient, LibraryDbContext context, IMemoryCache cache, ILogger<OpenLibraryService> logger, LibrarySettings? settings = null, OpenLibrarySettings? openLibrarySettings = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _filterJuvenileAndAdultContent = settings?.FilterJuvenileAndAdultContent ?? true;
        _defaultCopiesAvailable = Math.Max(1, settings?.DefaultCopiesAvailable ?? 5);
        _openLibrarySettings = openLibrarySettings ?? new OpenLibrarySettings();
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            var contact = string.IsNullOrWhiteSpace(_openLibrarySettings.ContactEmail) ? "without-email" : _openLibrarySettings.ContactEmail;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"LibraryMCPServer ({contact})");
        }
    }

    public async Task<int> RefreshStaleBooksAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _openLibrarySettings.RefreshDays));
        var books = await _context.Books.Where(x => x.OpenLibraryKey != null && (x.LastSyncedAt == null || x.LastSyncedAt < cutoff)).OrderBy(x => x.LastSyncedAt).Take(Math.Max(1, _openLibrarySettings.BatchSize)).ToListAsync(cancellationToken);
        var refreshed = 0;
        foreach (var book in books)
        {
            using var document = await GetJsonAsync($"https://openlibrary.org{book.OpenLibraryKey}.json", cancellationToken);
            var edition = document.RootElement;
            book.Title = (GetString(edition, "title") ?? book.Title)[..Math.Min(200, (GetString(edition, "title") ?? book.Title).Length)];
            book.Subjects = LibraryGenreRules.NormalizeSubjects(GetArrayStrings(edition, "subjects"));
            book.PageCount = Math.Clamp(GetInt(edition, "number_of_pages") ?? book.PageCount, 1, 10000);
            book.CoverImageUrl = $"https://covers.openlibrary.org/b/isbn/{book.ISBN}-M.jpg";
            book.LastSyncedAt = DateTime.UtcNow;
            refreshed++;
        }
        if (refreshed > 0) await _context.SaveChangesAsync(cancellationToken);
        return refreshed;
    }

    public async Task<IReadOnlyList<object>> SearchOpenLibraryAsync(string query, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var url = $"https://openlibrary.org/search.json?q={Uri.EscapeDataString(query)}&page={page}&limit={pageSize}&fields=key,title,author_name,isbn,first_publish_year,cover_i";
        using var document = await GetJsonAsync(url, cancellationToken);
        if (!document.RootElement.TryGetProperty("docs", out var docs)) return [];
        var results = new List<object>();
        foreach (var doc in docs.EnumerateArray())
        {
            var isbn = GetArrayString(doc, "isbn", IsbnValidator.IsValidIsbn13);
            var inLibrary = isbn is not null && await _context.Books.AnyAsync(x => x.ISBN == IsbnValidator.FormatIsbn13(isbn), cancellationToken);
            results.Add(new { key = GetString(doc, "key"), title = GetString(doc, "title"), author = GetArrayString(doc, "author_name"), isbn13 = isbn is null ? null : IsbnValidator.FormatIsbn13(isbn), publishedYear = doc.TryGetProperty("first_publish_year", out var year) ? year.GetInt32() : (int?)null, coverId = doc.TryGetProperty("cover_i", out var cover) ? cover.GetInt64() : (long?)null, inLibrary });
        }
        return results;
    }

    public async Task<Book> AddBookByIsbnAsync(string isbn, int copies = 1, CancellationToken cancellationToken = default)
    {
        if (!IsbnValidator.IsValidIsbn13(isbn)) throw new ArgumentException("A valid ISBN-13 is required.", nameof(isbn));
        var clean = IsbnValidator.FormatIsbn13(isbn);
        using var document = await GetJsonAsync($"https://openlibrary.org/isbn/{clean}.json", cancellationToken);
        var edition = document.RootElement;
        var title = GetString(edition, "title");
        var year = ParseYear(edition);
        var subjects = GetArrayStrings(edition, "subjects");
        var authorKey = edition.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array ? authors.EnumerateArray().Select(x => GetString(x, "key")).FirstOrDefault(x => x is not null) : null;
        if (title is null || year is null || authorKey is null) throw new InvalidDataException("Open Library returned an incomplete edition.");
        var author = await ResolveAuthorNameAsync(authorKey, cancellationToken);
        if (author is null) throw new InvalidDataException("Open Library author could not be resolved.");
        var genre = LibraryGenreRules.MapGenre(null, string.Join(", ", subjects));
        if (LibraryGenreRules.ShouldRejectSubjects(subjects, genre)) throw new InvalidDataException("The edition subjects were filtered.");
        var book = new Book { ISBN = clean, Title = title[..Math.Min(200, title.Length)], Author = author[..Math.Min(150, author.Length)], Genre = genre, Subjects = LibraryGenreRules.NormalizeSubjects(subjects), PublishedYear = year.Value, CopiesAvailable = Math.Max(1, copies), PageCount = GetInt(edition, "number_of_pages") ?? 300, CoverImageUrl = $"https://covers.openlibrary.org/b/isbn/{clean}-M.jpg", IsAuthentic = true, IsSample = false, OpenLibraryKey = GetString(edition, "key"), LastSyncedAt = DateTime.UtcNow };
        await UpsertAsync(book, cancellationToken, _ => { });
        await _context.SaveChangesAsync(cancellationToken);
        return await _context.Books.AsNoTracking().SingleAsync(x => x.ISBN == clean, cancellationToken);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        lock (LiveCache) if (LiveCache.TryGetValue(url, out var cached) && cached.Expires > DateTimeOffset.UtcNow) return JsonDocument.Parse(cached.Document.RootElement.GetRawText());
        for (var attempt = 0; ; attempt++)
        {
            await RequestGate.WaitAsync(cancellationToken);
            try { var wait = TimeSpan.FromMilliseconds(334) - (DateTime.UtcNow - _lastRequestUtc); if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken); _lastRequestUtc = DateTime.UtcNow; }
            finally { RequestGate.Release(); }
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if ((int)response.StatusCode is 429 or >= 500 && attempt < 4) { var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt)); await Task.Delay(delay, cancellationToken); continue; }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            lock (LiveCache) LiveCache[url] = (DateTimeOffset.UtcNow.AddHours(24), JsonDocument.Parse(document.RootElement.GetRawText()));
            return document;
        }
    }

    private async Task<string?> ResolveAuthorNameAsync(string key, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"https://openlibrary.org{key}.json", cancellationToken);
        return GetString(document.RootElement, "name");
    }

    private static int? ParseYear(JsonElement value) { var text = GetString(value, "publish_date"); return text is not null && int.TryParse(new string(text.Take(4).ToArray()), out var year) ? year : null; }
    private static int? GetInt(JsonElement value, string property) => value.TryGetProperty(property, out var element) && element.TryGetInt32(out var result) ? result : null;

    public async Task<int> SeedAsync(int targetCount = 1000, int delayMilliseconds = 250, int retries = 3, CancellationToken cancellationToken = default)
    {
        var targetPerGenre = Math.Max(1, (int)Math.Ceiling(targetCount / (double)SeedGenres.Length));
        var newBooks = 0;
        foreach (var genre in SeedGenres)
        {
            var accepted = await _context.Books.CountAsync(b => b.Genre == genre, cancellationToken);
            var seenIsbns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var query in SearchQueries[genre])
            {
                if (accepted >= targetPerGenre) break;
                for (var offset = 0; offset <= 500 && accepted < targetPerGenre; offset += 100)
                {
                    var docs = await SearchAsync(query, 100, offset, delayMilliseconds, retries, cancellationToken);
                    foreach (var doc in docs)
                    {
                        var book = ParseBook(doc, genre);
                        if (book is null || !seenIsbns.Add(book.ISBN)) continue;
                        var existing = await _context.Books.AsNoTracking().FirstOrDefaultAsync(b => b.ISBN == book.ISBN, cancellationToken);
                        await UpsertAsync(book, cancellationToken, isNew => newBooks += isNew ? 1 : 0);
                        if (existing is null || !string.Equals(existing.Genre, genre, StringComparison.OrdinalIgnoreCase)) accepted++;
                        if (accepted >= targetPerGenre) break;
                    }
                    if (docs.Count < 100) break;
                }
            }
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Open Library genre {Genre}: processed {Accepted} records; target was {Target}", genre, accepted, targetPerGenre);
        }
        ClearCaches();
        return newBooks;
    }

    public async Task<(int importedCount, IReadOnlyList<string> importedTitles)> ImportBooksAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query keyword cannot be empty.", nameof(query));
        var requestedGenre = LibraryGenreRules.MapGenre(query, query);
        var docs = await SearchAsync(query.Trim(), Math.Clamp(limit, 1, 100), 0, 250, 3, cancellationToken);
        var titles = new List<string>();
        foreach (var doc in docs)
        {
            var book = ParseBook(doc, requestedGenre);
            if (book is null) continue;
            var isNew = await UpsertAsync(book, cancellationToken, isAdded =>
            {
                if (isAdded) titles.Add(book.Title);
            });
        }
        if (titles.Count > 0) await _context.SaveChangesAsync(cancellationToken);
        else if (_context.ChangeTracker.HasChanges()) await _context.SaveChangesAsync(cancellationToken);
        ClearCaches();
        return (titles.Count, titles);
    }

    private async Task<bool> UpsertAsync(Book book, CancellationToken cancellationToken, Action<bool> onNew)
    {
        var existing = await _context.Books.FirstOrDefaultAsync(b => b.ISBN == book.ISBN, cancellationToken);
        if (existing is null)
        {
            await _context.Books.AddAsync(book, cancellationToken);
            onNew(true);
            return true;
        }

        existing.Title = book.Title;
        existing.Author = book.Author;
        existing.Genre = book.Genre;
        existing.Subjects = book.Subjects;
        existing.PublishedYear = book.PublishedYear;
        existing.Rating = book.Rating;
        existing.PageCount = book.PageCount;
        existing.CoverImageUrl = book.CoverImageUrl;
        existing.Description = book.Description;
        existing.IsAuthentic = true;
        existing.IsSample = false;
        existing.LastSyncedAt = DateTime.UtcNow;
        onNew(false);
        return false;
    }

    private async Task<List<JsonElement>> SearchAsync(string query, int limit, int offset, int delayMilliseconds, int retries, CancellationToken cancellationToken)
    {
        var url = $"https://openlibrary.org/search.json?q={Uri.EscapeDataString(query)}&limit={Math.Clamp(limit, 1, 100)}&offset={offset}&fields=title,author_name,isbn,first_publish_year,number_of_pages_median,ratings_average,first_sentence,subject";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    if (attempt >= retries) response.EnsureSuccessStatusCode();
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), cancellationToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var docs = document.RootElement.TryGetProperty("docs", out var value) && value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray().Select(x => x.Clone()).ToList()
                    : [];
                if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds, cancellationToken);
                return docs;
            }
            catch (HttpRequestException) when (attempt < retries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), cancellationToken);
            }
        }
    }

    private Book? ParseBook(JsonElement item, string requestedGenre)
    {
        var title = GetString(item, "title")?.Trim();
        var author = GetArrayString(item, "author_name")?.Trim();
        var isbn = GetArrayString(item, "isbn", IsbnValidator.IsValidIsbn13);
        var subjects = GetArrayStrings(item, "subject");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(isbn)) return null;
        if (LibraryGenreRules.ShouldRejectSubjects(subjects, requestedGenre, _filterJuvenileAndAdultContent)) return null;
        if (!item.TryGetProperty("first_publish_year", out var yearElement) || !yearElement.TryGetInt32(out var year) || year is < 1000 or > 2100) return null;

        var genre = LibraryGenreRules.AllowedGenres.Contains(requestedGenre, StringComparer.OrdinalIgnoreCase)
            ? LibraryGenreRules.AllowedGenres.First(g => g.Equals(requestedGenre, StringComparison.OrdinalIgnoreCase))
            : LibraryGenreRules.MapGenre(requestedGenre, string.Join(", ", subjects));
        var pageCount = item.TryGetProperty("number_of_pages_median", out var pages) && pages.TryGetInt32(out var count) && count > 0 ? Math.Clamp(count, 1, 10000) : 1;
        double? rating = item.TryGetProperty("ratings_average", out var ratingElement) && ratingElement.TryGetDouble(out var value) && value is >= 0 and <= 5 ? Math.Round(value, 2) : null;
        var cleanIsbn = isbn.Replace("-", string.Empty);
        var description = GetString(item, "first_sentence") ?? string.Empty;

        return new Book
        {
            Title = title[..Math.Min(title.Length, 200)],
            Author = author[..Math.Min(author.Length, 150)],
            Genre = genre,
            Subjects = LibraryGenreRules.NormalizeSubjects(subjects),
            PublishedYear = year,
            ISBN = IsbnValidator.FormatIsbn13(cleanIsbn),
            CopiesAvailable = _defaultCopiesAvailable,
            Rating = rating,
            PageCount = pageCount,
            CoverImageUrl = $"https://covers.openlibrary.org/b/isbn/{cleanIsbn}-M.jpg",
            Description = description[..Math.Min(description.Length, 1500)],
            IsAuthentic = true,
            IsSample = false
        };
    }

    private void ClearCaches()
    {
        _cache.Remove("library_stats_cache");
        _cache.Remove("library_genres_cache");
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? GetArrayString(JsonElement element, string property, Func<string, bool>? predicate = null) =>
        GetArrayStrings(element, property).FirstOrDefault(x => predicate is null || predicate(x));

    private static IReadOnlyList<string> GetArrayStrings(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
    }
}
