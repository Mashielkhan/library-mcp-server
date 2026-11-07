using LibraryMCPServer.Data;
using LibraryMCPServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace LibraryMCPServer.Tests;

public class DbContextConfigurationTests
{
    [Fact]
    public void ModelConfiguration_HasRequiredTableIndexesAndConstraints()
    {
        var factory = new LibraryDbContextFactory();
        using var context = factory.CreateDbContext([]);

        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var bookEntityType = designTimeModel.FindEntityType(typeof(Book));
        Assert.NotNull(bookEntityType);

        // Check table name
        Assert.Equal("Books", bookEntityType.GetTableName());

        // Check Indexes
        var indexes = bookEntityType.GetIndexes().ToList();

        var isbnIndex = indexes.FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Book.ISBN)));
        Assert.NotNull(isbnIndex);
        Assert.True(isbnIndex.IsUnique, "ISBN index must be unique");

        var titleIndex = indexes.FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Book.Title)));
        Assert.NotNull(titleIndex);

        var authorIndex = indexes.FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Book.Author)));
        Assert.NotNull(authorIndex);

        var genreIndex = indexes.FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(Book.Genre)));
        Assert.NotNull(genreIndex);

        // Check Check Constraints
        var checkConstraints = bookEntityType.GetCheckConstraints().ToList();
        Assert.Contains(checkConstraints, c => c.Name == "CK_Books_CopiesAvailable");

        // Check BookLoans table
        var loanEntityType = designTimeModel.FindEntityType(typeof(BookLoan));
        Assert.NotNull(loanEntityType);
        Assert.Equal("BookLoans", loanEntityType.GetTableName());
    }
}
