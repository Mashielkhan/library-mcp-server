namespace LibraryMCPServer.Utilities;

public static class AuthorNameNormalizer
{
    public static string Normalize(string value)
    {
        var result = value.Trim().TrimEnd('.').Trim();
        var comma = result.IndexOf(',');
        if (comma > 0)
        {
            var last = result[..comma].Trim();
            var first = result[(comma + 1)..].Trim();
            result = $"{first} {last}".Trim();
        }
        return result.TrimEnd('.').Trim();
    }
}