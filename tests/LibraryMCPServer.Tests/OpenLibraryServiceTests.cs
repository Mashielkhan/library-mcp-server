using System.Net;
using System.Net.Http;
using LibraryMCPServer.Data;
using LibraryMCPServer.Services;
using LibraryMCPServer.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using LibraryMCPServer.Configuration;

namespace LibraryMCPServer.Tests;

public class OpenLibraryServiceTests
{
    [Theory]
    [InlineData("Orwell, George.", "George Orwell")]
    [InlineData("Jane Austen.", "Jane Austen")]
    public void AuthorNameNormalizer_CanonicalizesNames(string input, string expected) => Assert.Equal(expected, AuthorNameNormalizer.Normalize(input));
    [Theory]
    [InlineData("subject:computer programming", "Programming")]
    [InlineData("subject:information technology", "Technology")]
    [InlineData("subject:science fiction", "Science Fiction")]
    [InlineData("Romance", "Romance")]
    [InlineData("unknown catalog tag", "History")]
    public void MapGenre_ReturnsOnlyAllowedGenre(string input, string expected)
    {
        Assert.Contains(expected, LibraryGenreRules.AllowedGenres);
        Assert.Equal(expected, LibraryGenreRules.MapGenre(input, input));
    }

    [Theory]
    [InlineData("9780451524935", true)]
    [InlineData("9780451524936", false)]
    public void Isbn13_CheckDigitIsValidated(string isbn, bool expected) => Assert.Equal(expected, IsbnValidator.IsValidIsbn13(isbn));

    [Fact]
    public void DumpEditionParser_RequiresEnglishResolvableAuthorAndValidMetadata()
    {
        var line = "editions/OL1M\t1\t2020\t{\"title\":\"A Mystery\",\"isbn_13\":[\"9780451524935\"],\"authors\":[{\"key\":\"/authors/OL1A\"}],\"languages\":[{\"key\":\"/languages/eng\"}],\"publish_year\":2000,\"subjects\":[\"mystery\"]}";
        var authors = new Dictionary<string, string> { ["/authors/OL1A"] = "An Author" };
        var counts = LibraryGenreRules.AllowedGenres.ToDictionary(x => x, _ => 0L);
        Assert.True(OpenLibraryDumpLoader.TryParseEdition(line, authors, counts, 20, 5, out var book, out var reason));
        Assert.NotNull(book);
        Assert.Null(reason);
        Assert.Equal("Mystery", book!.Genre);
    }

    [Theory]
    [InlineData("1200", false)]
    [InlineData("1449", false)]
    [InlineData("1450", true)]
    public void DumpEditionParser_ValidatesPublicationYears(string year, bool expected)
    {
        var line = $"editions/OL1M\t1\t2020\t{{\"title\":\"A Mystery\",\"isbn_13\":[\"9780451524935\"],\"authors\":[{{\"key\":\"/authors/OL1A\"}}],\"languages\":[{{\"key\":\"/languages/eng\"}}],\"publish_year\":{year},\"subjects\":[\"mystery\"]}}";
        var authors = new Dictionary<string, string> { ["/authors/OL1A"] = "An Author" };
        var counts = LibraryGenreRules.AllowedGenres.ToDictionary(x => x, _ => 0L);
        Assert.Equal(expected, OpenLibraryDumpLoader.TryParseEdition(line, authors, counts, 20, 3, out var book, out _));
        if (expected) Assert.Equal(1450, book!.PublishedYear);
    }

    [Fact]
    public void DumpEditionParser_PreservesUnknownPublicationYearAsNull()
    {
        var line = "editions/OL1M\t1\t2020\t{\"title\":\"A Mystery\",\"isbn_13\":[\"9780451524935\"],\"authors\":[{\"key\":\"/authors/OL1A\"}],\"languages\":[{\"key\":\"/languages/eng\"}],\"subjects\":[\"mystery\"]}";
        var authors = new Dictionary<string, string> { ["/authors/OL1A"] = "An Author" };
        var counts = LibraryGenreRules.AllowedGenres.ToDictionary(x => x, _ => 0L);
        Assert.True(OpenLibraryDumpLoader.TryParseEdition(line, authors, counts, 20, 3, out var book, out _));
        Assert.Null(book!.PublishedYear);
    }

    [Fact]
    public async Task RatingsLoader_UpdatesOnlyMatchedIsbn()
    {
        using var context = new LibraryDbContext(new DbContextOptionsBuilder<LibraryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Books.AddRange(new Models.Book { ISBN = "978-0-451-52493-5", Title = "One", Author = "A", Genre = "Mystery", PublishedYear = 2000, CopiesAvailable = 4 }, new Models.Book { ISBN = "978-0-451-52493-6", Title = "Two", Author = "B", Genre = "Mystery", PublishedYear = 2000, CopiesAvailable = 4 });
        await context.SaveChangesAsync();
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "isbn13,average_rating,ratings_count\n9780451524935,4.25,12\n9780306406157,5.0,20\n");
        try
        {
            Assert.Equal(1, await new GoodbooksRatingsLoader(context).LoadAsync(path));
            Assert.Equal(4.25, (await context.Books.SingleAsync(x => x.ISBN == "978-0-451-52493-5")).Rating);
            Assert.Equal(12, (await context.Books.SingleAsync(x => x.ISBN == "978-0-451-52493-5")).RatingCount);
            Assert.Null((await context.Books.SingleAsync(x => x.ISBN == "978-0-451-52493-6")).Rating);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RatingsLoader_StoresRatingCountAndSummarySuppressesLowCountRating()
    {
        using var context = new LibraryDbContext(new DbContextOptionsBuilder<LibraryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Books.Add(new Models.Book { ISBN = "978-0-451-52493-5", Title = "One", Author = "A", Genre = "Mystery", PublishedYear = 2000, CopiesAvailable = 4 });
        await context.SaveChangesAsync();
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "isbn13,average_rating,ratings_count\n9780451524935,4.25,9\n");
        try
        {
            await new GoodbooksRatingsLoader(context).LoadAsync(path);
            var service = new LibraryService(context, new MemoryCache(new MemoryCacheOptions()), NullLogger<LibraryService>.Instance);
            var result = await service.SearchBooksAsync("One");
            Assert.Single(result.Items);
            Assert.Null(result.Items[0].Rating);
            Assert.Equal(9, result.Items[0].RatingCount);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Fantasy", "sexual fantasies")]
    [InlineData("Fantasy", "drawing technique")]
    [InlineData("Romance", "psychology")]
    [InlineData("Mystery", "nyt: best sellers")]
    [InlineData("Mystery", "collectionID:abc123")]
    [InlineData("Mystery", "series:some-series")]
    [InlineData("Mystery", "Juvenile fiction")]
    [InlineData("Mystery", "erotica")]
    [InlineData("Mystery", "BDSM")]
    public void ShouldRejectSubjects_RejectsConfiguredMarkers(string genre, string subject)
    {
        Assert.True(LibraryGenreRules.ShouldRejectSubjects([subject], genre));
    }

    [Fact]
    public void ShouldRejectSubjects_CanDisableJuvenileAndAdultFilter()
    {
        Assert.False(LibraryGenreRules.ShouldRejectSubjects(["Juvenile fiction"], "Mystery", false));
    }

    [Fact]
    public async Task ImportBooksAsync_ExistingIsbnUpdatesWithoutAddingCopies()
    {
        using var context = new LibraryDbContext(new DbContextOptionsBuilder<LibraryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StaticResponseHandler("""
        {
          "docs": [{
            "title": "Updated Catalog Title",
            "author_name": ["Catalog Author"],
            "isbn": ["9780451524935"],
            "first_publish_year": 1949,
            "number_of_pages_median": 328,
            "ratings_average": 4.8,
            "first_sentence": "Updated description.",
             "subject": ["Fantasy", "Fiction"],
             "language": ["eng"]
          }]
        }
        """);
        var service = new OpenLibraryService(new HttpClient(handler), context, cache, NullLogger<OpenLibraryService>.Instance);

        var first = await service.ImportBooksAsync("Fantasy", 1);
        var second = await service.ImportBooksAsync("Fantasy", 1);

        var book = await context.Books.SingleAsync();
        Assert.Equal(1, first.importedCount);
        Assert.Equal(0, second.importedCount);
        Assert.Equal(1, await context.Books.CountAsync());
         Assert.Equal(3, book.CopiesAvailable);
        Assert.Equal("Updated Catalog Title", book.Title);
        Assert.Equal("Fantasy", book.Genre);
        Assert.Equal("Fantasy, Fiction", book.Subjects);
    }

    private sealed class StaticResponseHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response)
            });
        }
    }
}
