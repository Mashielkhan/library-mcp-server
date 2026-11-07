using LibraryMCPServer.Data;
using LibraryMCPServer.Models;
using LibraryMCPServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LibraryMCPServer.Tests;

public class LibraryServiceTests
{
    private static LibraryDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new LibraryDbContext(options);
    }

    private static IMemoryCache CreateMemoryCache()
    {
        return new MemoryCache(new MemoryCacheOptions());
    }

    private static async Task PopulateSampleDataAsync(LibraryDbContext context)
    {
        var books = new List<Book>
        {
            new() { Title = "1984", Author = "George Orwell", Genre = "Science Fiction", PublishedYear = 1949, ISBN = "978-0-1", CopiesAvailable = 5, Rating = 4.7, PageCount = 328, Description = "Dystopian classic" },
            new() { Title = "Animal Farm", Author = "George Orwell", Genre = "History", PublishedYear = 1945, ISBN = "978-0-2", CopiesAvailable = 3, Rating = 4.6, PageCount = 140, Description = "Satirical allegory" },
            new() { Title = "Python Crash Course", Author = "Eric Matthes", Genre = "Programming", PublishedYear = 2019, ISBN = "978-0-3", CopiesAvailable = 10, Rating = 4.8, PageCount = 544, Description = "Hands-on guide" },
            new() { Title = "Fluent Python", Author = "Luciano Ramalho", Genre = "Programming", PublishedYear = 2022, ISBN = "978-0-4", CopiesAvailable = 4, Rating = 4.9, PageCount = 1014, Description = "Advanced Python" },
            new() { Title = "The Way of Kings", Author = "Brandon Sanderson", Genre = "Fantasy", PublishedYear = 2010, ISBN = "978-0-5", CopiesAvailable = 8, Rating = 4.9, PageCount = 1007, Description = "Epic fantasy" },
            new() { Title = "Words of Radiance", Author = "Brandon Sanderson", Genre = "Fantasy", PublishedYear = 2014, ISBN = "978-0-6", CopiesAvailable = 2, Rating = 4.9, PageCount = 1087, Description = "Stormlight 2" },
            new() { Title = "Oathbringer", Author = "Brandon Sanderson", Genre = "Fantasy", PublishedYear = 2017, ISBN = "978-0-7", CopiesAvailable = 0, Rating = 4.8, PageCount = 1248, Description = "Stormlight 3" }
        };

        await context.Books.AddRangeAsync(books);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchBooksAsync_FindsBooksByAuthor_CaseInsensitive()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        await PopulateSampleDataAsync(context);
        var service = new LibraryService(context, CreateMemoryCache(), NullLogger<LibraryService>.Instance);

        var result = await service.SearchBooksAsync("george orwell");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, b => Assert.Equal("George Orwell", b.Author));
    }

    [Fact]
    public async Task SearchBooksAsync_FindsBooksByTitle_CaseInsensitive()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        await PopulateSampleDataAsync(context);
        var service = new LibraryService(context, CreateMemoryCache(), NullLogger<LibraryService>.Instance);

        var result = await service.SearchBooksAsync("python");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, b => Assert.Contains("Python", b.Title));
    }

    [Fact]
    public async Task SearchBooksAsync_EnforcesResultLimit_AndPagination()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        for (int i = 0; i < 20; i++)
        {
            context.Books.Add(new Book
            {
                Title = $"Alpha Book {i:D2}",
                Author = "Common Author",
                Genre = "Fantasy",
                PublishedYear = 2020,
                ISBN = $"978-TEST-{i:D4}",
                CopiesAvailable = 1,
                Rating = 4.5,
                PageCount = 300
            });
        }
        await context.SaveChangesAsync();

        var service = new LibraryService(context, CreateMemoryCache(), NullLogger<LibraryService>.Instance);
        var page1 = await service.SearchBooksAsync("Alpha", page: 1, pageSize: 5);

        Assert.Equal(20, page1.TotalCount);
        Assert.Equal(4, page1.TotalPages);
        Assert.Equal(5, page1.Items.Count);
        Assert.True(page1.HasMore);
        Assert.Equal("Alpha Book 00", page1.Items[0].Title);

        var page2 = await service.SearchBooksAsync("Alpha", page: 2, pageSize: 5);
        Assert.Equal(5, page2.Items.Count);
        Assert.Equal("Alpha Book 05", page2.Items[0].Title);
    }

    [Fact]
    public async Task SearchBooksAsync_FuzzyTypoTolerance_MatchesTypoInAuthorOrTitle()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        await PopulateSampleDataAsync(context);
        var service = new LibraryService(context, CreateMemoryCache(), NullLogger<LibraryService>.Instance);

        // Typo: "Gorge Orwel" instead of "George Orwell"
        var result = await service.SearchBooksAsync("Gorge Orwel");

        Assert.NotEmpty(result.Items);
        Assert.Contains(result.Items, b => b.Author == "George Orwell");
    }

    [Fact]
    public async Task SearchBooksAdvancedAsync_FiltersByRatingAndYear()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        await PopulateSampleDataAsync(context);
        var service = new LibraryService(context, CreateMemoryCache(), NullLogger<LibraryService>.Instance);

        var result = await service.SearchBooksAdvancedAsync(
            genre: "Programming",
            minRating: 4.85,
            minYear: 2020);

        Assert.Single(result.Items);
        Assert.Equal("Fluent Python", result.Items[0].Title);
    }

    [Fact]
    public async Task BorrowBookAsync_DecrementsCopiesAndRecordsLoan()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        await PopulateSampleDataAsync(context);
        var service = new LibraryService(context, CreateMemoryCache(), NullLogger<LibraryService>.Instance);

        var book = await context.Books.FirstAsync(b => b.Title == "1984");
        var initialCopies = book.CopiesAvailable;

        var loanResult = await service.BorrowBookAsync(book.Id, "Charlie Brown", loanDays: 14);

        Assert.True(loanResult.Success);
        Assert.Equal(initialCopies - 1, loanResult.RemainingCopies);

        var activeLoans = await service.GetActiveLoansAsync();
        Assert.Contains(activeLoans.Items, l => l.BorrowerName == "Charlie Brown" && l.BookId == book.Id);
    }

    [Fact]
    public async Task ReturnBookAsync_IncrementsCopiesAndClosesLoan()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        await PopulateSampleDataAsync(context);
        var service = new LibraryService(context, CreateMemoryCache(), NullLogger<LibraryService>.Instance);

        var book = await context.Books.FirstAsync(b => b.Title == "1984");
        await service.BorrowBookAsync(book.Id, "Charlie Brown", loanDays: 14);

        var returnResult = await service.ReturnBookAsync(book.Id, "Charlie Brown");

        Assert.True(returnResult.Success);
        Assert.Equal(5, returnResult.RemainingCopies);

        var activeLoans = await service.GetActiveLoansAsync();
        Assert.DoesNotContain(activeLoans.Items, l => l.BorrowerName == "Charlie Brown" && l.BookId == book.Id);
    }

    [Fact]
    public async Task GetBooksByGenreAsync_FiltersByGenre_ReportsTotalAndPaginates()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        await PopulateSampleDataAsync(context);
        var service = new LibraryService(context, CreateMemoryCache(), NullLogger<LibraryService>.Instance);

        var result = await service.GetBooksByGenreAsync("Fantasy", page: 1, pageSize: 2);

        Assert.Equal("Fantasy", result.Genre);
        Assert.Equal(3, result.TotalMatching);
        Assert.Equal(2, result.Books.Count);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task GetLibraryStatsAsync_CalculatesAggregatesCorrectly()
    {
        using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        await PopulateSampleDataAsync(context);
        var service = new LibraryService(context, CreateMemoryCache(), NullLogger<LibraryService>.Instance);

        var stats = await service.GetLibraryStatsAsync();

        Assert.Equal(7, stats.TotalBooks);
        Assert.Equal(32, stats.TotalAvailableCopies);
        Assert.Equal(0, stats.ActiveLoansCount);
        Assert.NotEmpty(stats.GenreBreakdown);
        Assert.Equal("Fantasy", stats.GenreBreakdown[0].Genre);
    }
}
