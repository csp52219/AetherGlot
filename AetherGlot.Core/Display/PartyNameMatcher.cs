namespace AetherGlot.Core.Display;

/// <summary>
/// Matches a chat sender to the text of a party list row. The row can contain level glyphs
/// and, depending on the game's name display setting, an abbreviated name
/// ("K. Rhyne", "Kaela R." or "K. R.").
/// </summary>
public static class PartyNameMatcher
{
    public static bool Matches(string? rowText, string? senderName)
    {
        if (string.IsNullOrWhiteSpace(rowText) || string.IsNullOrWhiteSpace(senderName)) return false;

        var row = Clean(rowText);
        foreach (var variant in Variants(senderName))
        {
            var idx = row.IndexOf(variant, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            // The name must not continue with another letter ("Kaela Rhyner")
            var end = idx + variant.Length;
            if (end < row.Length && char.IsLetter(row[end])) continue;
            if (idx > 0 && char.IsLetter(row[idx - 1])) continue;
            return true;
        }
        return false;
    }

    /// <summary>Full name and the three abbreviated forms of a "First Last" name.</summary>
    public static IEnumerable<string> Variants(string name)
    {
        var full = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        yield return full;

        var parts = full.Split(' ');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) yield break;
        var (first, last) = (parts[0], parts[1]);
        yield return $"{first[0]}. {last}";
        yield return $"{first} {last[0]}.";
        yield return $"{first[0]}. {last[0]}.";
    }

    // Drops private-use glyphs (level digits, icons) and collapses spaces
    private static string Clean(string s)
    {
        var chars = s.Where(c => c is < '' or > '').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
