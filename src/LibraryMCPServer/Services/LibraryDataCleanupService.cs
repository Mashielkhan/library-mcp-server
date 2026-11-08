using LibraryMCPServer.Data;
using Microsoft.EntityFrameworkCore;
using LibraryMCPServer.Utilities;

namespace LibraryMCPServer.Services;

public sealed class LibraryDataCleanupService
{
    private readonly LibraryDbContext _db;

    public LibraryDataCleanupService(LibraryDbContext db) => _db = db;

    public async Task<int> NormalizeDefaultCopiesAsync(int oldDefault, int newDefault, CancellationToken cancellationToken = default)
    {
        var books = await _db.Books
            .Where(b => b.CopiesAvailable == oldDefault && !b.Loans.Any(l => !l.IsReturned))
            .ToListAsync(cancellationToken);
        foreach (var book in books) book.CopiesAvailable = newDefault;
        await _db.SaveChangesAsync(cancellationToken);
        return books.Count;
    }

    public async Task<int> NormalizeAuthorsAsync(CancellationToken cancellationToken = default)
    {
        var books = await _db.Books.ToListAsync(cancellationToken);
        var changed = 0;
        foreach (var book in books)
        {
            var normalized = AuthorNameNormalizer.Normalize(book.Author);
            if (normalized == book.Author) continue;
            book.Author = normalized;
            changed++;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return changed;
    }
}