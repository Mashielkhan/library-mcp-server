using LibraryMCPServer.Models;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace LibraryMCPServer.Tests;

public class EntityValidationTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        var validationContext = new ValidationContext(model, serviceProvider: null, items: null);
        Validator.TryValidateObject(model, validationContext, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ValidBook_PassesValidation()
    {
        var book = new Book
        {
            Title = "Test Book",
            Author = "Test Author",
            Genre = "Programming",
            PublishedYear = 2024,
            ISBN = "978-0-1234567-8",
            CopiesAvailable = 10,
            Description = "A valid test book description."
        };

        var results = ValidateModel(book);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingTitle_FailsValidation(string? title)
    {
        var book = new Book
        {
            Title = title!,
            Author = "Valid Author",
            Genre = "Programming",
            PublishedYear = 2024,
            ISBN = "978-0-1234567-8",
            CopiesAvailable = 5
        };

        var results = ValidateModel(book);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Book.Title)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingAuthor_FailsValidation(string? author)
    {
        var book = new Book
        {
            Title = "Valid Title",
            Author = author!,
            Genre = "Programming",
            PublishedYear = 2024,
            ISBN = "978-0-1234567-8",
            CopiesAvailable = 5
        };

        var results = ValidateModel(book);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Book.Author)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingGenre_FailsValidation(string? genre)
    {
        var book = new Book
        {
            Title = "Valid Title",
            Author = "Valid Author",
            Genre = genre!,
            PublishedYear = 2024,
            ISBN = "978-0-1234567-8",
            CopiesAvailable = 5
        };

        var results = ValidateModel(book);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Book.Genre)));
    }

    [Theory]
    [InlineData(999)]
    [InlineData(2101)]
    public void OutOfRangePublishedYear_FailsValidation(int year)
    {
        var book = new Book
        {
            Title = "Valid Title",
            Author = "Valid Author",
            Genre = "Programming",
            PublishedYear = year,
            ISBN = "978-0-1234567-8",
            CopiesAvailable = 5
        };

        var results = ValidateModel(book);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Book.PublishedYear)));
    }

    [Fact]
    public void NegativeCopiesAvailable_FailsValidation()
    {
        var book = new Book
        {
            Title = "Valid Title",
            Author = "Valid Author",
            Genre = "Programming",
            PublishedYear = 2024,
            ISBN = "978-0-1234567-8",
            CopiesAvailable = -1
        };

        var results = ValidateModel(book);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Book.CopiesAvailable)));
    }

    [Fact]
    public void ExcessivelyLongStrings_FailValidation()
    {
        var book = new Book
        {
            Title = new string('A', 251),
            Author = new string('B', 201),
            Genre = new string('C', 101),
            PublishedYear = 2024,
            ISBN = new string('D', 26),
            CopiesAvailable = 5,
            Description = new string('E', 2501)
        };

        var results = ValidateModel(book);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Book.Title)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Book.Author)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Book.Genre)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Book.ISBN)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Book.Description)));
    }
}
