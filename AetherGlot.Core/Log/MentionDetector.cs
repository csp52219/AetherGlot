using System.Text.RegularExpressions;

namespace AetherGlot.Core.Log;

/// <summary>
/// Detects messages addressed to you: your name (full or first name), your job (name or
/// abbreviation, in English or Japanese) or your role ("healer", "タンク", ...).
/// </summary>
public sealed class MentionDetector
{
    private readonly Regex? latin;
    private readonly string[] japanese;

    public MentionDetector(IEnumerable<string> terms)
    {
        var list = terms.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        japanese = list.Where(Glossary.TermNormalizer.IsJapanese).ToArray();
        var latinTerms = list.Except(japanese).Where(t => t.Length >= 2).OrderByDescending(t => t.Length).Select(Regex.Escape).ToList();
        latin = latinTerms.Count == 0
            ? null
            : new Regex($@"(?<![\p{{L}}\p{{N}}])(?:{string.Join("|", latinTerms)})(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static MentionDetector Empty { get; } = new([]);

    public bool Matches(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (latin?.IsMatch(text) == true) return true;
        return japanese.Any(j => text.Contains(j, StringComparison.Ordinal));
    }

    /// <summary>Role words for an FFXIV ClassJob.Role value (1 tank, 2 melee, 3 ranged, 4 healer).</summary>
    public static IEnumerable<string> RoleWords(byte role) => role switch
    {
        1 => ["tank", "tanks", "タンク"],
        4 => ["healer", "healers", "heals", "ヒーラー"],
        2 => ["dps", "melee", "DPS", "近接"],
        3 => ["dps", "ranged", "DPS", "遠隔"],
        _ => [],
    };

    /// <summary>Full name, first name and last name of a character name ("Kaela Rhyne").</summary>
    public static IEnumerable<string> NameWords(string? characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) yield break;
        var name = characterName.Trim();
        yield return name;
        foreach (var part in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (part.Length >= 3) yield return part;
    }
}
