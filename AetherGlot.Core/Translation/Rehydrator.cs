namespace AetherGlot.Core.Translation;

public enum SegmentKind
{
    /// <summary>Text translated by the model.</summary>
    Translated,

    /// <summary>Auto-translate phrase, shown in the client language (green 【】 in the UI).</summary>
    AutoTranslate,

    /// <summary>Span the model could not translate; shown in the original language, marked.</summary>
    Unresolved,

    /// <summary>Original text (fallback / skip).</summary>
    Original,
}

/// <summary>
/// A piece of displayable text. <see cref="Guess"/> is set for unresolved spans;
/// <see cref="PlaceholderId"/> ("at0", ...) for auto-translate phrases.
/// </summary>
public sealed record DisplaySegment(string Text, SegmentKind Kind, string? Guess = null, string? PlaceholderId = null);

/// <summary>Turns placeholder text back into display segments.</summary>
public static class Rehydrator
{
    /// <summary>
    /// Replaces {{atN}} with the auto-translate display text and {{uN}} with the original
    /// unresolved text. Unknown tokens are kept verbatim as translated text.
    /// </summary>
    public static IReadOnlyList<DisplaySegment> Rehydrate(
        string translation,
        IReadOnlyDictionary<string, string> autoTranslateTexts,
        IReadOnlyList<UnresolvedSpan> unresolved)
    {
        var byId = unresolved.GroupBy(u => u.Id, StringComparer.Ordinal)
                             .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return Split(translation, SegmentKind.Translated, id =>
        {
            if (Placeholders.IsAutoTranslate(id) && autoTranslateTexts.TryGetValue(id, out var at))
                return new DisplaySegment(at, SegmentKind.AutoTranslate, PlaceholderId: id);
            if (byId.TryGetValue(id, out var u))
                return new DisplaySegment(u.Text, SegmentKind.Unresolved, u.Guess);
            return null;
        });
    }

    /// <summary>Original message with auto-translate phrases filled in (fallback display).</summary>
    public static IReadOnlyList<DisplaySegment> Original(
        string placeholderizedSource,
        IReadOnlyDictionary<string, string> autoTranslateTexts) =>
        Split(placeholderizedSource, SegmentKind.Original, id =>
            autoTranslateTexts.TryGetValue(id, out var at) ? new DisplaySegment(at, SegmentKind.AutoTranslate, PlaceholderId: id) : null);

    public static string ToPlainText(IEnumerable<DisplaySegment> segments) =>
        string.Concat(segments.Select(s => s.Text));

    private static IReadOnlyList<DisplaySegment> Split(string text, SegmentKind textKind, Func<string, DisplaySegment?> resolve)
    {
        var result = new List<DisplaySegment>();
        var last   = 0;
        foreach (System.Text.RegularExpressions.Match m in Placeholders.TokenRegex().Matches(text))
        {
            if (m.Index > last)
                Add(result, text[last..m.Index], textKind);

            var seg = resolve(m.Groups["id"].Value);
            if (seg is null) Add(result, m.Value, textKind);
            else result.Add(seg);

            last = m.Index + m.Length;
        }
        if (last < text.Length)
            Add(result, text[last..], textKind);
        return result;
    }

    // Merge adjacent plain segments
    private static void Add(List<DisplaySegment> list, string text, SegmentKind kind)
    {
        if (text.Length == 0) return;
        if (list.Count > 0 && list[^1].Kind == kind && kind is SegmentKind.Translated or SegmentKind.Original)
            list[^1] = list[^1] with { Text = list[^1].Text + text };
        else
            list.Add(new DisplaySegment(text, kind));
    }
}
