namespace LibraryMCPServer.Utilities;

public static class LibraryGenreRules
{
    public static readonly string[] AllowedGenres =
    [
        "Mystery", "Romance", "Biography", "History", "Fantasy",
        "Business", "Self-Help", "Programming", "Technology", "Science Fiction"
    ];

    private static readonly string[] MetadataPrefixes = ["nyt:", "collectionid:", "series:"];
    private static readonly string[] FantasyRomanceNonfictionMarkers = ["sexual fantasies", "drawing", "technique", "cross-stitch", "psychology"];
    private static readonly string[] JuvenileAdultMarkers = ["juvenile", "erotica", "bdsm"];

    public static string MapGenre(string? requestedGenre, string? subjects)
    {
        var text = $"{requestedGenre}, {subjects}".ToLowerInvariant();
        if (text.Contains("science fiction") || text.Contains("science-fiction") || text.Contains("space opera")) return "Science Fiction";
        if (text.Contains("programming") || text.Contains("computer software") || text.Contains("software development")) return "Programming";
        if (text.Contains("technology") || text.Contains("computing") || text.Contains("information technology")) return "Technology";
        if (text.Contains("self-help") || text.Contains("self help") || text.Contains("personal development")) return "Self-Help";
        if (text.Contains("business") || text.Contains("management") || text.Contains("entrepreneur") || text.Contains("finance")) return "Business";
        if (text.Contains("mystery") || text.Contains("detective") || text.Contains("crime")) return "Mystery";
        if (text.Contains("romance") || text.Contains("love story")) return "Romance";
        if (text.Contains("biography") || text.Contains("autobiography") || text.Contains("memoir")) return "Biography";
        if (text.Contains("fantasy") || text.Contains("fairy tale") || text.Contains("magic")) return "Fantasy";
        return "History";
    }

    public static bool ShouldRejectSubjects(IEnumerable<string> subjects, string genre, bool filterJuvenileAndAdult = true)
    {
        var normalized = subjects.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
        if (normalized.Any(s => MetadataPrefixes.Any(prefix => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))) return true;
        if (filterJuvenileAndAdult && normalized.Any(s => JuvenileAdultMarkers.Any(marker => s.Contains(marker, StringComparison.OrdinalIgnoreCase)))) return true;
        if ((genre.Equals("Fantasy", StringComparison.OrdinalIgnoreCase) || genre.Equals("Romance", StringComparison.OrdinalIgnoreCase)) &&
            normalized.Any(s => FantasyRomanceNonfictionMarkers.Any(marker => s.Contains(marker, StringComparison.OrdinalIgnoreCase)))) return true;
        return false;
    }

    public static string NormalizeSubjects(IEnumerable<string> subjects) =>
        string.Join(", ", subjects.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
}
