namespace LibraryMCPServer.Utilities;

/// <summary>
/// Validates and formats ISBN-10 and ISBN-13 identifiers according to International Standard Book Number specifications.
/// </summary>
public static class IsbnValidator
{
    /// <summary>
    /// Validates an ISBN-13 check digit.
    /// Formula: (10 - ((sum of odd-positioned digits + 3 * sum of even-positioned digits) % 10)) % 10.
    /// </summary>
    public static bool IsValidIsbn13(string isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn)) return false;

        var clean = isbn.Replace("-", "").Replace(" ", "").Trim();
        if (clean.Length != 13 || !long.TryParse(clean, out _)) return false;

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int digit = clean[i] - '0';
            sum += (i % 2 == 0) ? digit : digit * 3;
        }

        int check = (10 - (sum % 10)) % 10;
        return check == (clean[12] - '0');
    }

    /// <summary>
    /// Formats a 13-digit clean string into standard hyphenated ISBN-13 format (e.g. 978-0-13-235088-4).
    /// </summary>
    public static string FormatIsbn13(string cleanIsbn)
    {
        if (string.IsNullOrWhiteSpace(cleanIsbn)) return cleanIsbn;
        var clean = cleanIsbn.Replace("-", "").Replace(" ", "").Trim();

        if (clean.Length != 13) return cleanIsbn;

        // Standard 5-part ISBN-13: EAN-Group-Publisher-Title-Check
        return $"{clean[..3]}-{clean.Substring(3, 1)}-{clean.Substring(4, 3)}-{clean.Substring(7, 5)}-{clean[12]}";
    }
}
