using Lumina.Text.ReadOnly;
using ClientLanguage = Dalamud.Game.ClientLanguage;

namespace AetherGlot.Glossary;

/// <summary>
/// Splits a received chat SeString into typed text and auto-translate phrases. Auto-translate
/// payloads arrive as Group/Key; their text is resolved in the reader's client language with
/// Dalamud's SeString evaluator (not AutoTranslatePayload.Text, which follows the Dalamud UI
/// language). Must run on the framework thread.
/// </summary>
internal static class SeStringReader
{
    // Fixed macros with these raw groups are player / item / map links, not auto-translate
    private const uint FirstLinkGroup = 100;

    public static List<MessagePart> ToParts(
        SeString message, ISeStringEvaluator evaluator, ClientLanguage language, string languageCode, AutoTranslateCatalog catalog)
    {
        var parts = new List<MessagePart>();
        foreach (var payload in message.Payloads)
        {
            switch (payload)
            {
                case AutoTranslatePayload at when at.Group is > 0 and < FirstLinkGroup:
                    parts.Add(new AutoTranslatePart(at.Group, at.Key, Resolve(at, evaluator, language, languageCode, catalog)));
                    break;
                case ITextProvider text when !string.IsNullOrEmpty(text.Text):
                    parts.Add(new TextPart(text.Text));
                    break;
            }
        }
        return parts;
    }

    private static string Resolve(AutoTranslatePayload at, ISeStringEvaluator evaluator, ClientLanguage language, string languageCode, AutoTranslateCatalog catalog)
    {
        try
        {
            var evaluated = evaluator.Evaluate(new ReadOnlySeString(at.Encode()), default, language).ExtractText();
            var text = StripGlyphs(evaluated);
            if (text.Length > 0) return text;
        }
        catch
        {
            // Fall back to the catalog below
        }

        if (catalog.TryGet(at.Group, at.Key, out var entry))
            return languageCode == "ja" ? entry.Ja : entry.En;

        return StripGlyphs(at.Text);
    }

    // The auto-translate open/close markers are private-use glyphs
    private static string StripGlyphs(string s) =>
        new string(s.Where(c => c is < '' or > '').ToArray()).Trim();
}
