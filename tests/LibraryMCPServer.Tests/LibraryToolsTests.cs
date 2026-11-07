using LibraryMCPServer.Data;
using LibraryMCPServer.Models;
using LibraryMCPServer.Services;
using LibraryMCPServer.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace LibraryMCPServer.Tests;

public class LibraryToolsTests
{
    private static LibraryTools CreateTools(LibraryDbContext context)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new LibraryService(context, cache, NullLogger<LibraryService>.Instance);
        var openLibrary = new OpenLibraryService(new HttpClient(), context, cache, NullLogger<OpenLibraryService>.Instance);
        return new LibraryTools(service, openLibrary, NullLogger<LibraryTools>.Instance);
    }

    private static LibraryDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new LibraryDbContext(options);

        context.Books.AddRange(
            new Book { Title = "1984", Author = "George Orwell", Genre = "Science Fiction", PublishedYear = 1949, ISBN = "978-001", CopiesAvailable = 5, Rating = 4.7, PageCount = 328 },
            new Book { Title = "Animal Farm", Author = "George Orwell", Genre = "History", PublishedYear = 1945, ISBN = "978-002", CopiesAvailable = 2, Rating = 4.6, PageCount = 140 },
            new Book { Title = "The Way of Kings", Author = "Brandon Sanderson", Genre = "Fantasy", PublishedYear = 2010, ISBN = "978-003", CopiesAvailable = 10, Rating = 4.9, PageCount = 1007 }
        );
        context.SaveChanges();

        return context;
    }

    [Fact]
    public async Task SearchBooks_ValidTerm_ReturnsSuccessResult()
    {
        using var context = CreateInMemoryContext();
        var tools = CreateTools(context);

        var result = await tools.SearchBooksAsync("George Orwell", page: 1, pageSize: 10);
        var json = JsonSerializer.Serialize(result);

        Assert.Contains("Found 2 matching book(s)", json);
        Assert.Contains("1984", json);
        Assert.Contains("Animal Farm", json);
    }

    [Fact]
    public async Task BorrowBook_ValidBook_ReturnsCheckoutSuccess()
    {
        using var context = CreateInMemoryContext();
        var tools = CreateTools(context);

        var book = await context.Books.FirstAsync(b => b.Title == "1984");
        var result = await tools.BorrowBookAsync(book.Id, "John Doe", loanDays: 14);
        var json = JsonSerializer.Serialize(result);

        Assert.Contains("Successfully borrowed", json);
        Assert.Contains("1984", json);
    }

    [Fact]
    public async Task ReturnBook_ValidLoan_ReturnsReturnSuccess()
    {
        using var context = CreateInMemoryContext();
        var tools = CreateTools(context);

        var book = await context.Books.FirstAsync(b => b.Title == "1984");
        await tools.BorrowBookAsync(book.Id, "John Doe", loanDays: 14);

        var returnResult = await tools.ReturnBookAsync(book.Id, "John Doe");
        var json = JsonSerializer.Serialize(returnResult);

        Assert.Contains("Successfully returned", json);
    }

    [Fact]
    public async Task GetBooksByGenre_ValidGenre_ReturnsStructuredCounts()
    {
        using var context = CreateInMemoryContext();
        var tools = CreateTools(context);

        var result = await tools.GetBooksByGenreAsync("Fantasy", page: 1, pageSize: 10);
        var json = JsonSerializer.Serialize(result);

        Assert.Contains("Found 1 books in genre", json);
        Assert.Contains("Fantasy", json);
        Assert.Contains("The Way of Kings", json);
    }

    [Fact]
    public async Task GetLibraryStats_ReturnsAccurateMetrics()
    {
        using var context = CreateInMemoryContext();
        var tools = CreateTools(context);

        var result = await tools.GetLibraryStatsAsync();
        var stats = Assert.IsType<LibraryStatsDto>(result);

        Assert.Equal(3, stats.TotalBooks);
        Assert.Equal(17, stats.TotalAvailableCopies);
        Assert.Equal(3, stats.GenreBreakdown.Count);
    }

    [Fact]
    public async Task EveryReadListTool_ReturnsPaginationMetadata()
    {
        using var context = CreateInMemoryContext();
        var tools = CreateTools(context);

        var genres = JsonSerializer.Serialize(await tools.ListAvailableGenresAsync(page: 1, pageSize: 1));
        var lowStock = JsonSerializer.Serialize(await tools.GetLowStockBooksAsync(threshold: 5, page: 1, pageSize: 1));
        var loans = JsonSerializer.Serialize(await tools.GetActiveLoansAsync(page: 1, pageSize: 1));

        Assert.Contains("totalCount", genres);
        Assert.Contains("totalPages", lowStock);
        Assert.Contains("hasMore", loans);
    }

    [Fact]
    public async Task AdvancedSearch_MinRatingFiltersItemsAndTotalCount()
    {
        using var context = CreateInMemoryContext();
        var tools = CreateTools(context);

        var json = JsonSerializer.Serialize(await tools.SearchBooksAdvancedAsync(minRating: 4.8));

        Assert.Contains("\"totalCount\":1", json);
        Assert.Contains("The Way of Kings", json);
        Assert.DoesNotContain("Animal Farm", json);
    }

    [Fact]
    public async Task SearchBooks_EmptyTerm_ReturnsClearError()
    {
        using var context = CreateInMemoryContext();
        var json = JsonSerializer.Serialize(await CreateTools(context).SearchBooksAsync(" "));

        Assert.Contains("Search term cannot be empty", json);
    }

    [Fact]
    public async Task BookAndLoanErrors_ReturnClearFailures()
    {
        using var context = CreateInMemoryContext();
        var tools = CreateTools(context);
        var unavailable = new Book { Title = "Unavailable", Author = "Author", Genre = "Mystery", PublishedYear = 2020, ISBN = "978-099", CopiesAvailable = 0, PageCount = 100 };
        context.Books.Add(unavailable);
        await context.SaveChangesAsync();

        var unknown = JsonSerializer.Serialize(await tools.GetBookDetailsAsync(99999));
        var noCopies = JsonSerializer.Serialize(await tools.BorrowBookAsync(unavailable.Id, "Reader"));
        var notBorrowed = JsonSerializer.Serialize(await tools.ReturnBookAsync(unavailable.Id, "Reader"));

        Assert.Contains("No book found", unknown);
        Assert.Contains("No inventory available", noCopies);
        Assert.Contains("No active loan found", notBorrowed);
    }
}
