using LibraryMCPServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LibraryMCPServer.Data;

/// <summary>
/// Seeds the library database with 100% authentic, published books.
    /// Marks legacy generated records as samples without deleting existing data.
/// </summary>
public static class LibraryDbSeeder
{
    public static readonly string[] Genres =
    [
        "Classic Fiction",
        "Science Fiction",
        "Fantasy",
        "Programming",
        "Computer Science",
        "Technology",
        "History",
        "Biography",
        "Business",
        "Psychology",
        "Philosophy",
        "Self-Help"
    ];

    /// <summary>
    /// Seeds the database with authentic published literature, cleaning out legacy dummy records.
    /// </summary>
    public static async Task<int> SeedAsync(
        LibraryDbContext context,
        ILogger? logger = null,
        int count = 50,
        int seed = 42,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var legacyRecords = await context.Books
                .Where(b => b.Title.StartsWith("Awesome ") || b.Description.Contains("lorem ipsum") || b.Description.Contains("Lorem ipsum") || !b.IsAuthentic)
                .ToListAsync(cancellationToken);
            foreach (var legacyRecord in legacyRecords) legacyRecord.IsSample = true;

            var existingCount = await context.Books.CountAsync(cancellationToken);
            var authenticBooks = AuthenticBooksCatalog.GetAuthenticBooks();

            int addedCount = 0;
            foreach (var book in authenticBooks)
            {
                var existing = await context.Books.FirstOrDefaultAsync(b => b.ISBN == book.ISBN, cancellationToken);
                if (existing != null)
                {
                    // Update with accurate metadata
                    existing.Title = book.Title;
                    existing.Author = book.Author;
                    existing.Genre = book.Genre;
                    existing.PublishedYear = book.PublishedYear;
                    existing.Rating = book.Rating;
                    existing.PageCount = book.PageCount;
                    existing.CoverImageUrl = book.CoverImageUrl;
                    existing.Description = book.Description;
                    existing.IsAuthentic = true;
                    existing.IsSample = false;
                }
                else
                {
                    await context.Books.AddAsync(book, cancellationToken);
                    addedCount++;
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            // Seed sample active loans if table is empty
            if (!await context.BookLoans.AnyAsync(cancellationToken))
            {
                var firstBook = await context.Books.FirstOrDefaultAsync(b => b.Title == "Nineteen Eighty-Four", cancellationToken);
                var secondBook = await context.Books.FirstOrDefaultAsync(b => b.Title == "Clean Code: A Handbook of Agile Software Craftsmanship", cancellationToken);

                if (firstBook != null && secondBook != null)
                {
                    var loans = new List<BookLoan>
                    {
                        new() { BookId = firstBook.Id, BorrowerName = "Alice Johnson", BorrowedAt = DateTime.UtcNow.AddDays(-3), DueDate = DateTime.UtcNow.AddDays(11), IsReturned = false },
                        new() { BookId = secondBook.Id, BorrowerName = "Bob Smith", BorrowedAt = DateTime.UtcNow.AddDays(-5), DueDate = DateTime.UtcNow.AddDays(9), IsReturned = false }
                    };
                    await context.BookLoans.AddRangeAsync(loans, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                }
            }

            var totalFinal = await context.Books.CountAsync(cancellationToken);
            logger?.LogInformation("Library database successfully populated with {Total} authentic published books ({Added} newly added).", totalFinal, addedCount);
            return totalFinal;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "An error occurred while seeding authentic books.");
            throw;
        }
    }

    public static List<Book> GenerateBooks(int count = 50, int seed = 42)
    {
        return AuthenticBooksCatalog.GetAuthenticBooks().Take(count).ToList();
    }
}
