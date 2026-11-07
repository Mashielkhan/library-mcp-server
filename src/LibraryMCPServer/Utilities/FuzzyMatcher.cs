namespace LibraryMCPServer.Utilities;

/// <summary>
/// Provides string similarity and fuzzy matching algorithms (Levenshtein distance & token overlap)
/// to tolerate typographical errors and phonetic variations in book queries.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Computes the normalized similarity ratio between two strings (0.0 = completely different, 1.0 = exact match).
    /// </summary>
    public static double Similarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
        {
            return 0.0;
        }

        var s = source.Trim().ToLowerInvariant();
        var t = target.Trim().ToLowerInvariant();

        if (s == t) return 1.0;
        if (s.Contains(t) || t.Contains(s)) return 0.85;

        // Check token-level containment
        var sTokens = s.Split([' ', ',', '.', '-', ':', ';'], StringSplitOptions.RemoveEmptyEntries);
        var tTokens = t.Split([' ', ',', '.', '-', ':', ';'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var st in sTokens)
        {
            foreach (var tt in tTokens)
            {
                if (st == tt) return 0.8;
                if (LevenshteinDistance(st, tt) <= 1 && Math.Min(st.Length, tt.Length) >= 4)
                {
                    return 0.75;
                }
            }
        }

        var distance = LevenshteinDistance(s, t);
        var maxLength = Math.Max(s.Length, t.Length);
        return 1.0 - ((double)distance / maxLength);
    }

    /// <summary>
    /// Computes the Levenshtein edit distance between two strings.
    /// </summary>
    public static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        var d = new int[s.Length + 1, t.Length + 1];

        for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= t.Length; j++) d[0, j] = j;

        for (int i = 1; i <= s.Length; i++)
        {
            for (int j = 1; j <= t.Length; j++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s.Length, t.Length];
    }
}
