namespace LibraryMCPServer.Configuration;

/// <summary>
/// Configuration settings for the Library MCP Server.
/// </summary>
public class LibrarySettings
{
    public const string SectionName = "LibrarySettings";

    /// <summary>
    /// Maximum number of records returned by search and list tools by default.
    /// </summary>
    public int MaxSearchResults { get; set; } = 10;

    /// <summary>
    /// Default number of fictional records to seed in development mode.
    /// </summary>
    public int SeedRecordCount { get; set; } = 5000;

    /// <summary>
    /// Controls whether automatic schema migration and data seeding run on startup.
    /// </summary>
    public bool EnableAutoMigrationAndSeed { get; set; } = true;

    public bool IncludeSampleData { get; set; } = false;

    public bool FilterJuvenileAndAdultContent { get; set; } = true;

    public int DefaultCopiesAvailable { get; set; } = 5;
}
