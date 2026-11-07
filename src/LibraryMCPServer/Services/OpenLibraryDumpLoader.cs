using System.IO.Compression;
using System.Text.Json;
using LibraryMCPServer.Configuration;
using LibraryMCPServer.Data;
using LibraryMCPServer.Models;
using LibraryMCPServer.Utilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LibraryMCPServer.Services;

public sealed record DumpLoadReport(
    long RowsRead,
    long Kept,
    IReadOnlyDictionary<string, long> Skipped,
    long Inserted,
    long Updated,
    IReadOnlyDictionary<string, long> BooksPerGenre);

public sealed class OpenLibraryDumpLoader
{
    private const int BatchSize = 1000;
    private readonly LibraryDbContext _db;
    private readonly LibrarySettings _settings;
    private readonly ILogger<OpenLibraryDumpLoader> _logger;

    public OpenLibraryDumpLoader(LibraryDbContext db, LibrarySettings settings, ILogger<OpenLibraryDumpLoader> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public static async Task BuildAuthorLookupAsync(string dumpPath, string lookupPath, CancellationToken cancellationToken = default)
    {
        await using var input = await OpenDumpAsync(dumpPath);
        await using var output = new StreamWriter(new FileStream(lookupPath, FileMode.Create, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false));
        string? line;
        while ((line = await input.Reader.ReadLineAsync(cancellationToken)) is not null)
        {
            var record = ParseRecord(line);
            if (record is null || !record.Value.TryGetProperty("key", out var keyElement) || !record.Value.TryGetProperty("name", out var nameElement)) continue;
            var key = keyElement.GetString();
            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name)) continue;
            await output.WriteLineAsync(JsonSerializer.Serialize(new { key, name }));
        }
    }

    public async Task<DumpLoadReport> LoadEditionsAsync(string dumpPath, string authorLookupPath, int maxRows = 200000, CancellationToken cancellationToken = default)
    {
        maxRows = Math.Max(1, maxRows);
        var cap = Math.Max(1, maxRows / LibraryGenreRules.AllowedGenres.Length);
        var genreCounts = LibraryGenreRules.AllowedGenres.ToDictionary(x => x, _ => 0L, StringComparer.OrdinalIgnoreCase);
        var skipped = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var lookup = await LoadAuthorLookupAsync(authorLookupPath, cancellationToken);
        long read = 0, kept = 0, inserted = 0, updated = 0;
        var batch = new List<Book>(BatchSize);

        await using var input = await OpenDumpAsync(dumpPath);
        string? line;
        while ((line = await input.Reader.ReadLineAsync(cancellationToken)) is not null)
        {
            read++;
            if (read % 10000 == 0) _logger.LogInformation("Open Library dump progress: {RowsRead} rows read, {Kept} kept", read, kept);
            var result = TryParseEdition(line, lookup, genreCounts, cap, Math.Max(1, _settings.DefaultCopiesAvailable), out var book, out var reason);
            if (!result)
            {
                Increment(skipped, reason!);
                continue;
            }
            kept++;
            genreCounts[book!.Genre]++;
            batch.Add(book!);
            if (batch.Count >= BatchSize)
            {
                await EnsureDatabaseBelowLimitAsync(cancellationToken);
                var counts = await UpsertBatchAsync(batch, cancellationToken);
                inserted += counts.Inserted;
                updated += counts.Updated;
                batch.Clear();
            }
            if (kept >= maxRows) break;
        }
        if (batch.Count > 0)
        {
            await EnsureDatabaseBelowLimitAsync(cancellationToken);
            var counts = await UpsertBatchAsync(batch, cancellationToken);
            inserted += counts.Inserted;
            updated += counts.Updated;
        }

        return new DumpLoadReport(read, kept, skipped, inserted, updated, genreCounts);
    }

    internal static bool TryParseEdition(string line, IReadOnlyDictionary<string, string> authors, IReadOnlyDictionary<string, long> genreCounts, int genreCap, int defaultCopies, out Book? book, out string? reason)
    {
        book = null;
        reason = null;
        var record = ParseRecord(line);
        if (record is null) { reason = "invalid-json"; return false; }
        var json = record.Value;
        var title = StringProperty(json, "title");
        if (string.IsNullOrWhiteSpace(title)) { reason = "missing-title"; return false; }
        var isbn = ArrayValues(json, "isbn_13").Concat(ArrayValues(json, "isbn13")).FirstOrDefault(IsbnValidator.IsValidIsbn13);
        if (isbn is null) { reason = "invalid-isbn13"; return false; }
        var authorKey = json.TryGetProperty("authors", out var authorArray) && authorArray.ValueKind == JsonValueKind.Array
            ? authorArray.EnumerateArray().Select(x => StringProperty(x, "key")).FirstOrDefault(x => x is not null)
            : null;
        if (authorKey is null || !authors.TryGetValue(authorKey, out var author)) { reason = "unresolved-author"; return false; }
        var languageValues = ArrayValues(json, "languages");
        if (!languageValues.Any(x => x.EndsWith("/eng", StringComparison.OrdinalIgnoreCase) || x.Equals("eng", StringComparison.OrdinalIgnoreCase))) { reason = "not-english"; return false; }
        var year = ParseYear(json);
        if (year is null || year < 1800 || year > DateTime.UtcNow.Year) { reason = "invalid-year"; return false; }
        var subjects = ArrayValues(json, "subjects");
        var genre = LibraryGenreRules.MapGenre(null, string.Join(", ", subjects));
        if (LibraryGenreRules.ShouldRejectSubjects(subjects, genre)) { reason = "filtered-subjects"; return false; }
        if (!genreCounts.TryGetValue(genre, out var count) || count >= genreCap) { reason = "genre-cap"; return false; }
        var key = StringProperty(json, "key");
        book = new Book
        {
            ISBN = IsbnValidator.FormatIsbn13(isbn),
            Title = title[..Math.Min(200, title.Length)],
            Author = author[..Math.Min(150, author.Length)],
            Genre = genre,
            Subjects = LibraryGenreRules.NormalizeSubjects(subjects),
            PublishedYear = year.Value,
            CopiesAvailable = defaultCopies,
            PageCount = 300,
            IsAuthentic = true,
            IsSample = false,
            OpenLibraryKey = key is null ? null : key[..Math.Min(50, key.Length)],
            LastSyncedAt = DateTime.UtcNow
        };
        return true;
    }

    private async Task<(long Inserted, long Updated)> UpsertBatchAsync(List<Book> books, CancellationToken cancellationToken)
    {
        var isbns = books.Select(x => x.ISBN).ToArray();
        var existing = await _db.Books.Where(x => isbns.Contains(x.ISBN)).ToDictionaryAsync(x => x.ISBN, StringComparer.OrdinalIgnoreCase, cancellationToken);
        long inserted = 0, updated = 0;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var book in books)
        {
            if (!existing.TryGetValue(book.ISBN, out var current))
            {
                await _db.Books.AddAsync(book, cancellationToken);
                inserted++;
                continue;
            }
            current.Title = book.Title;
            current.Author = book.Author;
            current.Genre = book.Genre;
            current.Subjects = book.Subjects;
            current.PublishedYear = book.PublishedYear;
            current.OpenLibraryKey = book.OpenLibraryKey;
            current.LastSyncedAt = book.LastSyncedAt;
            updated++;
        }
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (inserted, updated);
    }

    private async Task EnsureDatabaseBelowLimitAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsSqlServer()) return;
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SUM(size) * 8.0 / 1024 / 1024 FROM sys.database_files";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var sizeGb = Convert.ToDouble(value ?? 0, System.Globalization.CultureInfo.InvariantCulture);
        if (sizeGb >= 8) throw new InvalidOperationException($"Open Library loader stopped: database size is {sizeGb:F2} GB, at or above the 8 GB safety limit.");
    }

    private static async Task<Dictionary<string, string>> LoadAuthorLookupAsync(string path, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var key = StringProperty(root, "key");
                var name = StringProperty(root, "name");
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(name)) result[key] = name;
            }
            catch (JsonException) { }
        }
        return result;
    }

    private static int? ParseYear(JsonElement json)
    {
        foreach (var value in new[] { StringProperty(json, "publish_year"), StringProperty(json, "publish_date") })
            if (value is not null && int.TryParse(value[..Math.Min(4, value.Length)], out var year)) return year;
        return null;
    }

    private static JsonElement? ParseRecord(string line)
    {
        var tab = line.LastIndexOf('\t');
        if (tab < 0) return null;
        try { using var document = JsonDocument.Parse(line[(tab + 1)..]); return document.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    private static string? StringProperty(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
    private static IEnumerable<string> ArrayValues(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Object ? StringProperty(x, "key") : x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>() : [];
    private static void Increment(Dictionary<string, long> values, string key) => values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;

    private static async Task<DumpReader> OpenDumpAsync(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        Stream content = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(stream, CompressionMode.Decompress) : stream;
        return new DumpReader(stream, new StreamReader(content));
    }

    private sealed class DumpReader(FileStream source, StreamReader Reader) : IAsyncDisposable
    {
        public StreamReader Reader { get; } = Reader;
        public async ValueTask DisposeAsync() { Reader.Dispose(); await source.DisposeAsync(); }
    }
}
