using LibraryMCPServer.Utilities;
using Xunit;

namespace LibraryMCPServer.Tests;

public class IsbnValidatorTests
{
    [Theory]
    [InlineData("978-0-451-52493-5", true)]
    [InlineData("978-0-132-35088-4", true)]
    [InlineData("978-0-593-13520-4", true)]
    [InlineData("978-0-451-52493-9", false)] // Invalid check digit
    [InlineData("12345", false)]             // Too short
    [InlineData("", false)]
    public void IsValidIsbn13_ValidatesCorrectly(string isbn, bool expected)
    {
        var isValid = IsbnValidator.IsValidIsbn13(isbn);
        Assert.Equal(expected, isValid);
    }
}
