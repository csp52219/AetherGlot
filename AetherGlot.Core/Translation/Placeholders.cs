using System.Text.RegularExpressions;

namespace AetherGlot.Core.Translation;

/// <summary>
/// Placeholder tokens used in prompts: {{at0}} for auto-translate phrases and {{u0}} for
/// unresolved spans.
/// </summary>
public static partial class Placeholders
{
    public const string AutoTranslatePrefix = "at";
    public const string UnresolvedPrefix    = "u";

    [GeneratedRegex(@"\{\{(?<id>(?:at|u)\d+)\}\}", RegexOptions.CultureInvariant)]
    public static partial Regex TokenRegex();

    public static string Token(string id) => $"{{{{{id}}}}}";

    public static string AutoTranslateId(int index) => $"{AutoTranslatePrefix}{index}";

    /// <summary>All placeholder ids in order of appearance (duplicates kept).</summary>
    public static IReadOnlyList<string> Find(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : TokenRegex().Matches(text).Select(m => m.Groups["id"].Value).ToList();

    public static bool IsAutoTranslate(string id) =>
        id.StartsWith(AutoTranslatePrefix, StringComparison.Ordinal);

    public static bool IsUnresolved(string id) =>
        id.StartsWith(UnresolvedPrefix, StringComparison.Ordinal) && !IsAutoTranslate(id);
}
