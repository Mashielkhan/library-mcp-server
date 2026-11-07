using System.Globalization;
using Microsoft.EntityFrameworkCore;
using LibraryMCPServer.Data;

namespace LibraryMCPServer.Services;

public sealed class GoodbooksRatingsLoader
{
    private readonly LibraryDbContext _db;

    public GoodbooksRatingsLoader(LibraryDbContext db) => _db = db;

    public async Task<int> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var ratings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync(cancellationToken);
        if (header is null) throw new InvalidDataException("The Goodbooks CSV is empty.");
        var columns = CsvLine.Parse(header);
        var isbnIndex = columns.FindIndex(x => x.Equals("isbn13", StringComparison.OrdinalIgnoreCase));
        var ratingIndex = columns.FindIndex(x => x.Equals("average_rating", StringComparison.OrdinalIgnoreCase));
        if (isbnIndex < 0 || ratingIndex < 0) throw new InvalidDataException("The Goodbooks CSV must contain isbn13 and average_rating columns.");
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            var values = CsvLine.Parse(line);
            if (values.Count <= Math.Max(isbnIndex, ratingIndex) || !double.TryParse(values[ratingIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) || rating is < 0 or > 5) continue;
            var isbn = values[isbnIndex].Replace("-", string.Empty).Trim();
            if (Utilities.IsbnValidator.IsValidIsbn13(isbn)) ratings[isbn] = rating;
        }
        var isbns = ratings.Keys.ToArray();
        var books = await _db.Books.ToListAsync(cancellationToken);
        var matched = books.Where(x => ratings.ContainsKey(x.ISBN.Replace("-", string.Empty))).ToList();
        foreach (var book in matched) book.Rating = ratings[book.ISBN.Replace("-", string.Empty)];
        await _db.SaveChangesAsync(cancellationToken);
        return matched.Count;
    }

    private static class CsvLine
    {
        public static List<string> Parse(string line)
        {
            var result = new List<string>();
            var value = new System.Text.StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; continue; }
                if (c == '"') { quoted = !quoted; continue; }
                if (c == ',' && !quoted) { result.Add(value.ToString()); value.Clear(); continue; }
                value.Append(c);
            }
            result.Add(value.ToString());
            return result;
        }
    }
}
