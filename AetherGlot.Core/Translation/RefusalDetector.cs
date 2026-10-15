namespace AetherGlot.Core.Translation;

/// <summary>
/// Detects when the "translation" is really a refusal, apology or explanation from the model
/// (e.g. 「翻訳できません」, "I'm sorry, I can't ...").
/// </summary>
public static class RefusalDetector
{
    // Phrases that only appear when the model talks about itself / the task
    private static readonly string[] StrongPatterns =
    [
        // Japanese
        "翻訳できません", "翻訳することができません", "翻訳が困難", "翻訳不可", "認識できません",
        "お手伝いできません", "対応できません", "できかねます",
        "aiとして", "ai として", "言語モデル", "原文が不明",
        // English
        "i can't translate", "i cannot translate", "i can not translate", "unable to translate",
        "cannot be translated", "can't be translated", "not able to translate", "i'm unable to",
        "i am unable to", "as an ai", "as a language model", "please provide more context",
        // German / French
        "kann ich nicht übersetzen", "nicht übersetzen", "als ki", "je ne peux pas traduire",
        "impossible de traduire", "en tant qu'ia",
    ];

    // Apologies: a refusal only when the source itself is not an apology
    private static readonly string[] ApologyPatterns =
    [
        "申し訳", "すみません", "ごめんなさい",
        "i'm sorry", "i am sorry", "sorry,", "apologize", "apologies",
        "es tut mir leid", "entschuldigung", "désolé", "je suis désolé",
    ];

    // Confusion: a refusal only when the source does not express confusion itself
    private static readonly string[] ConfusionPatterns =
    [
        "理解できません", "意味が分かりません", "意味がわかりません",
        "i don't understand", "i do not understand", "could you clarify", "could you please clarify",
    ];

    private static readonly string[] SourceConfusionMarkers =
    [
        "understand", "idk", "dunno", "confus", "clarify", "what", "?", "？",
        "理解", "わから", "分から", "意味", "なに", "何",
        "verstehe", "comprends",
    ];

    private static readonly string[] SourceApologyMarkers =
    [
        "sorry", "sry", "srry", "my bad", "apolog", "mb",
        "ごめん", "すみません", "すいません", "申し訳", "悪い",
        "entschuldig", "tut mir leid", "désolé", "pardon",
    ];

    public static bool IsRefusal(string? translation, string? source)
    {
        if (string.IsNullOrWhiteSpace(translation)) return false;

        var t = Normalize(translation);
        if (StrongPatterns.Any(p => t.Contains(p, StringComparison.Ordinal)))
            return true;

        var s = Normalize(source ?? string.Empty);

        if (ApologyPatterns.Any(p => t.Contains(p, StringComparison.Ordinal)) &&
            !SourceApologyMarkers.Any(m => ContainsWord(s, m)))
            return true;

        if (ConfusionPatterns.Any(p => t.Contains(p, StringComparison.Ordinal)) &&
            !SourceConfusionMarkers.Any(m => ContainsWord(s, m)))
            return true;

        return false;
    }

    /// <summary>
    /// True when the translation is far longer than the source, which usually means the model
    /// added commentary. Placeholders are not counted.
    /// </summary>
    public static bool IsSuspiciouslyLong(string? translation, string? source, double maxRatio = 4.0, int slack = 40)
    {
        if (string.IsNullOrEmpty(translation)) return false;
        var tLen = Placeholders.TokenRegex().Replace(translation, string.Empty).Length;
        var sLen = Placeholders.TokenRegex().Replace(source ?? string.Empty, string.Empty).Length;
        return tLen > (sLen * maxRatio) + slack;
    }

    private static string Normalize(string s) =>
        s.ToLowerInvariant().Replace('’', '\'').Replace('‘', '\'');

    // Short latin markers ("mb", "sry") must be whole words; others may be substrings
    private static bool ContainsWord(string haystack, string marker)
    {
        if (marker.Length > 3 || !marker.All(char.IsAsciiLetter))
            return haystack.Contains(marker, StringComparison.Ordinal);

        var idx = 0;
        while ((idx = haystack.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
        {
            var before = idx == 0 || !char.IsLetter(haystack[idx - 1]);
            var end    = idx + marker.Length;
            var after  = end >= haystack.Length || !char.IsLetter(haystack[end]);
            if (before && after) return true;
            idx = end;
        }
        return false;
    }
}
