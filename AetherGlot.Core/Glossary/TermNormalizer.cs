using System.Globalization;
using System.Text;

namespace AetherGlot.Core.Glossary;

/// <summary>
/// Normalises spellings so that "opo", "Opo-opo", "opo opo" and "opoopo" compare equal:
/// NFKC, lower case, separators (space, hyphen, apostrophe, dot, middle dot, colon) removed,
/// and an exactly repeated word collapsed ("opoopo" → "opo").
/// </summary>
public static class TermNormalizer
{
    private static readonly HashSet<char> Separators =
    [
        ' ', '　', '-', '‐', '‑', '–', '—', '_', '\'', '’', '`', '.', '·', '・', ':', '：', '/', '／',
    ];

    public static string Normalize(string? text) => Normalize(text, collapseRepeat: true);

    /// <param name="collapseRepeat">Collapse "opoopo" → "opo" (used for latin text only).</param>
    public static string Normalize(string? text, bool collapseRepeat)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var nfkc = text.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(nfkc.Length);
        foreach (var c in nfkc)
        {
            if (Separators.Contains(c)) continue;
            sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
        }
        return collapseRepeat ? CollapseRepeat(sb.ToString()) : sb.ToString();
    }

    /// <summary>
    /// Normalises character by character and returns, for each output char, the index of the
    /// source char it came from (used to map matches back to the original text).
    /// </summary>
    public static (string Text, int[] Map) NormalizeWithMap(string text)
    {
        var sb  = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (Separators.Contains(c)) continue;
            var n = c.ToString().Normalize(NormalizationForm.FormKC);
            foreach (var nc in n)
            {
                if (Separators.Contains(nc)) continue;
                sb.Append(char.ToLower(nc, CultureInfo.InvariantCulture));
                map.Add(i);
            }
        }
        return (sb.ToString(), map.ToArray());
    }

    /// <summary>"opoopo" → "opo", "byebye" → "bye". Only exact two-fold repeats of 2+ chars.</summary>
    public static string CollapseRepeat(string s)
    {
        if (s.Length < 4 || s.Length % 2 != 0) return s;
        var half = s.Length / 2;
        return string.CompareOrdinal(s, 0, s, half, half) == 0 ? s[..half] : s;
    }

    /// <summary>Removes a trailing parenthetical ("stun (status)" → "stun") and end punctuation.</summary>
    public static string StripDecorations(string text)
    {
        var s = text.Trim();
        if (s.StartsWith('【') && s.EndsWith('】')) s = s[1..^1].Trim();
        var paren = s.IndexOfAny(['(', '（']);
        if (paren > 0) s = s[..paren];
        return s.Trim().TrimEnd('.', '!', '?', '。', '！', '？', '…', ':', '：').Trim();
    }

    /// <summary>True for text that is mostly Japanese script.</summary>
    public static bool IsJapanese(string text)
    {
        var jp = 0; var latin = 0;
        foreach (var c in text)
        {
            if (c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿' or >= 'ｦ' and <= 'ﾟ') jp++;
            else if (char.IsAsciiLetter(c)) latin++;
        }
        return jp > 0 && jp >= latin;
    }
}
