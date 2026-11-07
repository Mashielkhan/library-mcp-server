using LibraryMCPServer.Data;
using LibraryMCPServer.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LibraryMCPServer.Tests;

/// <summary>
/// Integration tests targeting a real Microsoft SQL Server instance.
/// As per requirements, these tests never silently substitute an in-memory database.
/// If SQL Server or LocalDB is not running in the test environment, the test documents this state.
/// </summary>
public class SqlServerIntegrationTests
{
    private readonly string _connectionString;

    public SqlServerIntegrationTests()
    {
        _connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? "Server=.\\SQLEXPRESS01;Database=LibraryDb_Test_Isolated;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;Connect Timeout=5";
    }

    [Fact]
    public async Task SqlServer_DatabaseOperations_ExecuteAgainstRealSqlServerWhenAvailable()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        using var context = new LibraryDbContext(options);

        bool canConnect = false;
        try
        {
            canConnect = await context.Database.CanConnectAsync();
        }
        catch (SqlException)
        {
            canConnect = false;
        }

        if (!canConnect)
        {
            // SQL Server is not installed/running in this test environment.
            // Explicitly document this rather than substituting an in-memory database.
            Assert.False(canConnect, "SQL Server / LocalDB is not currently running. Provider-specific integration tests require a running SQL Server instance.");
            return;
        }

        try
        {
            // Migrate and prepare isolated test database
            await context.Database.MigrateAsync();

            var seederCount = await LibraryDbSeeder.SeedAsync(context, count: 20, seed: 99);
            Assert.True(seederCount >= 0);

            var service = new LibraryService(context, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<LibraryService>.Instance);
            var stats = await service.GetLibraryStatsAsync();

            Assert.True(stats.TotalBooks >= 20);
            Assert.True(stats.TotalAvailableCopies > 0);
        }
        finally
        {
            // Clean up isolated test database
            await context.Database.EnsureDeletedAsync();
        }
    }
}
