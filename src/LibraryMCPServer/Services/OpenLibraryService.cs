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

    public OpenLibraryService(HttpClient httpClient, LibraryDbContext context, IMemoryCache cache, ILogger<OpenLibraryService> logger, LibrarySettings? settings = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _filterJuvenileAndAdultContent = settings?.FilterJuvenileAndAdultContent ?? true;
        _defaultCopiesAvailable = Math.Max(1, settings?.DefaultCopiesAvailable ?? 5);
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LibraryMCPServer/1.0 (MCP library catalog)");
        }
    }

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
        existing.CopiesAvailable = _defaultCopiesAvailable;
        existing.IsAuthentic = true;
        existing.IsSample = false;
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
