namespace LibraryMCPServer.Configuration;

public sealed class OpenLibrarySettings
{
    public const string SectionName = "OpenLibrary";

    public string ContactEmail { get; set; } = "your-email@example.com";

    public int RefreshDays { get; set; } = 30;

    public bool EnableBackgroundRefresh { get; set; } = false;
}