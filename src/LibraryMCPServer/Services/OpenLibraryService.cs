using System.Net;
using System.Text.Json;
using LibraryMCPServer.Data;
using LibraryMCPServer.Models;
using LibraryMCPServer.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace LibraryMCPServer.Services;

/// <summary>
/// Service that fetches real published book records live from the Open Library REST API.
/// </summary>
public sealed class OpenLibraryService
{
    public static readonly string[] SeedGenres =
    [
        "Mystery", "Romance", "Biography", "History", "Fantasy",
        "Business", "Self-Help", "Programming", "Technology", "Science Fiction"
    ];

    private readonly HttpClient _httpClient;
    private readonly LibraryDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OpenLibraryService> _logger;

    public OpenLibraryService(HttpClient httpClient, LibraryDbContext context, IMemoryCache cache, ILogger<OpenLibraryService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LibraryMCPServer/1.0 (MCP library catalog)");
        }
    }

    public async Task<int> SeedAsync(int targetCount = 1000, int delayMilliseconds = 250, int retries = 3, CancellationToken cancellationToken = default)
    {
        var perGenre = Math.Max(1, (int)Math.Ceiling(targetCount / (double)SeedGenres.Length));
        var imported = 0;
        foreach (var genre in SeedGenres)
        {
            var docs = await SearchAsync(genre, perGenre, delayMilliseconds, retries, cancellationToken);
            foreach (var doc in docs)
            {
                var book = ParseBook(doc, genre);
                if (book is null) continue;

                var existing = await _context.Books.FirstOrDefaultAsync(b => b.ISBN == book.ISBN, cancellationToken);
                if (existing is not null)
                {
                    existing.CopiesAvailable++;
                    existing.Genre = MergeGenres(existing.Genre, book.Genre);
                    continue;
                }

                await _context.Books.AddAsync(book, cancellationToken);
                imported++;
            }

            await _context.SaveChangesAsync(cancellationToken);
            _cache.Remove("library_stats_cache");
            _cache.Remove("library_genres_cache");
        }

        return imported;
    }

    public async Task<(int importedCount, IReadOnlyList<string> importedTitles)> ImportBooksAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query keyword cannot be empty.", nameof(query));
        var docs = await SearchAsync(query.Trim(), Math.Clamp(limit, 1, 50), 250, 3, cancellationToken);
        var titles = new List<string>();
        foreach (var doc in docs)
        {
            var book = ParseBook(doc, query.Trim());
            if (book is null) continue;
            var existing = await _context.Books.FirstOrDefaultAsync(b => b.ISBN == book.ISBN, cancellationToken);
            if (existing is not null) { existing.CopiesAvailable++; continue; }
            await _context.Books.AddAsync(book, cancellationToken);
            titles.Add(book.Title);
        }
        if (titles.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _cache.Remove("library_stats_cache");
            _cache.Remove("library_genres_cache");
        }
        return (titles.Count, titles);
    }

    private async Task<List<JsonElement>> SearchAsync(string query, int limit, int delayMilliseconds, int retries, CancellationToken cancellationToken)
    {
        var url = $"https://openlibrary.org/search.json?q={Uri.EscapeDataString(query)}&limit={Math.Clamp(limit, 1, 100)}&fields=title,author_name,isbn,first_publish_year,number_of_pages_median,ratings_average,first_sentence,subject";
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

    private static Book? ParseBook(JsonElement item, string genre)
    {
        var title = GetString(item, "title");
        var author = GetArrayString(item, "author_name");
        var isbn = GetArrayString(item, "isbn", IsbnValidator.IsValidIsbn13);
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(isbn)) return null;

        if (!item.TryGetProperty("first_publish_year", out var yearElement) || !yearElement.TryGetInt32(out var year) || year is < 1000 or > 2100) return null;
        var pageCount = item.TryGetProperty("number_of_pages_median", out var pages) && pages.TryGetInt32(out var count) && count > 0 ? Math.Clamp(count, 1, 10000) : 1;
        double? rating = item.TryGetProperty("ratings_average", out var ratingElement) && ratingElement.TryGetDouble(out var value) && value is >= 0 and <= 5 ? Math.Round(value, 2) : null;
        var cleanIsbn = isbn.Replace("-", string.Empty);
        var subjects = item.TryGetProperty("subject", out var subjectElement) && subjectElement.ValueKind == JsonValueKind.Array
            ? subjectElement.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Take(3).ToList()
            : [];
        var fullGenre = MergeGenres(genre, string.Join(", ", subjects));
        var description = GetString(item, "first_sentence") ?? string.Empty;

        return new Book
        {
            Title = title.Trim()[..Math.Min(title.Trim().Length, 200)],
            Author = author.Trim()[..Math.Min(author.Trim().Length, 150)],
            Genre = fullGenre[..Math.Min(fullGenre.Length, 50)],
            PublishedYear = year,
            ISBN = IsbnValidator.FormatIsbn13(cleanIsbn),
            CopiesAvailable = 1,
            Rating = rating,
            PageCount = pageCount,
            CoverImageUrl = $"https://covers.openlibrary.org/b/isbn/{cleanIsbn}-M.jpg",
            Description = description[..Math.Min(description.Length, 1500)],
            IsAuthentic = true,
            IsSample = false
        };
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? GetArrayString(JsonElement element, string property, Func<string, bool>? predicate = null)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array) return null;
        return value.EnumerateArray().Select(x => x.GetString()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && (predicate is null || predicate(x)));
    }

    private static string MergeGenres(string first, string second) => string.Join(", ", (first + ", " + second).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase));
}
