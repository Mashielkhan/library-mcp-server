using LibraryMCPServer.Configuration;
using LibraryMCPServer.Data;
using LibraryMCPServer.Services;
using LibraryMCPServer.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

var builder = Host.CreateApplicationBuilder(args);

// Load configuration
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// CRITICAL FOR MCP STDIO TRANSPORT:
// Clear default logging providers and route all logs strictly to standard error (stderr).
// Standard output (stdout) is reserved exclusively for MCP JSON-RPC protocol messages.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Configure options
builder.Services.Configure<LibrarySettings>(
    builder.Configuration.GetSection(LibrarySettings.SectionName));

// Configure EF Core with SQL Server
builder.Services.AddDbContext<LibraryDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("LibraryDb")
        ?? "Server=(localdb)\\mssqllocaldb;Database=LibraryDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
        sqlOptions.CommandTimeout(30);
    });
});

// Register in-memory caching for ultra-fast query performance
builder.Services.AddMemoryCache();

// Register HTTP client and Open Library API importer
builder.Services.AddSingleton(new HttpClient());
builder.Services.AddScoped<OpenLibraryService>();

// Register application services and tools
builder.Services.AddScoped<ILibraryService, LibraryService>();
builder.Services.AddScoped<LibraryTools>();

// Register MCP server with stdio transport and assembly tool discovery
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

if (args.Contains("--seed-open-library", StringComparer.OrdinalIgnoreCase))
{
    using var seedScope = app.Services.CreateScope();
    var seedContext = seedScope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    await seedContext.Database.MigrateAsync();
    var seedService = seedScope.ServiceProvider.GetRequiredService<OpenLibraryService>();
    var target = builder.Configuration.GetValue("OPEN_LIBRARY_TARGET_COUNT", 1000);
    var imported = await seedService.SeedAsync(target);
    Console.Error.WriteLine($"Open Library seed complete: imported {imported} new books.");
    return;
}

// Controlled local development initialization and seeding
await InitializeDatabaseAsync(app);

// Run the MCP Server host
await app.RunAsync();

static async Task InitializeDatabaseAsync(IHost host)
{
    using var scope = host.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var config = services.GetRequiredService<IConfiguration>();

    var enableAutoMigration = config.GetValue("LibrarySettings:EnableAutoMigrationAndSeed", true);
    if (!enableAutoMigration)
    {
        logger.LogInformation("Auto-migration and seeding is disabled by configuration.");
        return;
    }

    try
    {
        var dbContext = services.GetRequiredService<LibraryDbContext>();
        logger.LogInformation("Checking database connectivity...");

        var canConnect = await dbContext.Database.CanConnectAsync();
        if (canConnect)
        {
            logger.LogInformation("Database connection established. Applying any pending migrations...");
            await dbContext.Database.MigrateAsync();

            var seedCount = config.GetValue("LibrarySettings:SeedRecordCount", 5000);
            logger.LogInformation("Verifying database seed state...");
            await LibraryDbSeeder.SeedAsync(dbContext, logger, seedCount);
        }
        else
        {
            logger.LogWarning("Database is currently unreachable. The MCP server will start, but database queries will require a running SQL Server instance.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization encountered an error. MCP server will continue running.");
    }
}
