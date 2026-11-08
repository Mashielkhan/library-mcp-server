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
builder.Services.Configure<OpenLibrarySettings>(
    builder.Configuration.GetSection(OpenLibrarySettings.SectionName));
builder.Services.AddSingleton(builder.Configuration.GetSection(LibrarySettings.SectionName).Get<LibrarySettings>() ?? new LibrarySettings());
builder.Services.AddSingleton(builder.Configuration.GetSection(OpenLibrarySettings.SectionName).Get<OpenLibrarySettings>() ?? new OpenLibrarySettings());

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
builder.Services.AddScoped<OpenLibraryDumpLoader>();
builder.Services.AddScoped<GoodbooksRatingsLoader>();
builder.Services.AddScoped<LibraryDataCleanupService>();
builder.Services.AddHostedService<OpenLibraryRefreshService>();

// Register application services and tools
builder.Services.AddScoped<ILibraryService, LibraryService>();
builder.Services.AddScoped<LibraryTools>();

// Register MCP server with stdio transport and assembly tool discovery
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

if (args.Contains("--load-open-library-authors", StringComparer.OrdinalIgnoreCase))
{
    var path = GetCommandValue(args, "--load-open-library-authors");
    var lookupPath = args.SkipWhile(x => !x.Equals("--load-open-library-authors", StringComparison.OrdinalIgnoreCase)).Skip(2).FirstOrDefault() ?? "data/open-library-authors.lookup.jsonl";
    if (path is null) throw new ArgumentException("Usage: --load-open-library-authors <authors-dump-path> [lookup-output-path]");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lookupPath))!);
    await OpenLibraryDumpLoader.BuildAuthorLookupAsync(path, lookupPath);
    Console.Error.WriteLine($"Open Library author lookup created at {lookupPath}.");
    return;
}

if (args.Contains("--normalize-authors", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
    var changed = await scope.ServiceProvider.GetRequiredService<LibraryDataCleanupService>().NormalizeAuthorsAsync();
    Console.Error.WriteLine($"Author cleanup complete: normalized {changed} books.");
    return;
}

if (args.Contains("--normalize-default-copies", StringComparer.OrdinalIgnoreCase))
{
    var oldDefault = GetIntOption(args, "--old-default", 5);
    using var scope = app.Services.CreateScope();
    var settings = scope.ServiceProvider.GetRequiredService<LibrarySettings>();
    await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
    var changed = await scope.ServiceProvider.GetRequiredService<LibraryDataCleanupService>()
        .NormalizeDefaultCopiesAsync(oldDefault, Math.Max(1, settings.DefaultCopies));
    Console.Error.WriteLine($"Default-copy cleanup complete: updated {changed} books; books with active loans were skipped.");
    return;
}

if (args.Contains("--load-ratings", StringComparer.OrdinalIgnoreCase))
{
    var path = GetCommandValue(args, "--load-ratings") ?? throw new ArgumentException("Usage: --load-ratings <goodbooks-10k-books.csv>");
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
    var matched = await scope.ServiceProvider.GetRequiredService<GoodbooksRatingsLoader>().LoadAsync(path);
    Console.Error.WriteLine($"Goodbooks ratings load complete: matched {matched} existing books.");
    return;
}

if (args.Contains("--load-open-library-dump", StringComparer.OrdinalIgnoreCase))
{
    var path = GetCommandValue(args, "--load-open-library-dump");
    if (path is null) throw new ArgumentException("Usage: --load-open-library-dump <editions-dump-path> [--max-rows N] [--authors-lookup path]");
    var maxRows = GetIntOption(args, "--max-rows", 200000);
    var lookup = GetOption(args, "--authors-lookup") ?? "data/open-library-authors.lookup.jsonl";
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
    var report = await scope.ServiceProvider.GetRequiredService<OpenLibraryDumpLoader>().LoadEditionsAsync(path, lookup, maxRows);
    Console.Error.WriteLine($"Open Library dump complete: rows read={report.RowsRead}, kept={report.Kept}, inserted={report.Inserted}, updated={report.Updated}.");
    Console.Error.WriteLine($"Skipped: {string.Join(", ", report.Skipped.Select(x => $"{x.Key}={x.Value}"))}");
    Console.Error.WriteLine($"Genres: {string.Join(", ", report.BooksPerGenre.Select(x => $"{x.Key}={x.Value}"))}");
    return;
}

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

static string? GetCommandValue(string[] args, string command) => GetOption(args, command);

static string? GetOption(string[] args, string option)
{
    var index = Array.FindIndex(args, x => x.Equals(option, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[index + 1] : null;
}

static int GetIntOption(string[] args, string option, int fallback) => int.TryParse(GetOption(args, option), out var value) ? value : fallback;

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
