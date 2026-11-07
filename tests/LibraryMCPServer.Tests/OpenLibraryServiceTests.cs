using System.Net;
using System.Net.Http;
using LibraryMCPServer.Data;
using LibraryMCPServer.Services;
using LibraryMCPServer.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LibraryMCPServer.Tests;

public class OpenLibraryServiceTests
{
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
            "subject": ["Fantasy", "Fiction"]
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
        Assert.Equal(5, book.CopiesAvailable);
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
