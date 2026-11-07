using LibraryMCPServer.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibraryMCPServer.Tests;

public class SeederTests
{
    [Fact]
    public void GenerateBooks_CreatesAuthenticBooks_WithUniqueISBNs()
    {
        var books = AuthenticBooksCatalog.GetAuthenticBooks();

        Assert.NotEmpty(books);

        var distinctIsbns = books.Select(b => b.ISBN).Distinct().Count();
        Assert.Equal(books.Count, distinctIsbns);
        Assert.All(books, b => Assert.True(b.IsAuthentic));
        Assert.All(books, b => Assert.False(string.IsNullOrWhiteSpace(b.CoverImageUrl)));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_DoesNotDuplicateRecords()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new LibraryDbContext(options);

        // First seeding
        var seededCount1 = await LibraryDbSeeder.SeedAsync(context);
        Assert.True(seededCount1 > 0);
        var initialDbCount = await context.Books.CountAsync();

        // Second seeding attempt does not duplicate rows
        var seededCount2 = await LibraryDbSeeder.SeedAsync(context);
        Assert.Equal(initialDbCount, await context.Books.CountAsync());
    }
}
