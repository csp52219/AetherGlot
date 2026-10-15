namespace AetherGlot.Core.Translation;

/// <summary>A piece of a received chat message.</summary>
public abstract record MessagePart;

/// <summary>Typed text.</summary>
public sealed record TextPart(string Text) : MessagePart;

/// <summary>An auto-translate phrase, already resolved in the reader's client language.</summary>
public sealed record AutoTranslatePart(uint Group, uint Key, string DisplayText) : MessagePart;

/// <summary>Result of <see cref="Placeholderizer.Build"/>.</summary>
public sealed record PlaceholderizedMessage(
    string Text,
    IReadOnlyDictionary<string, string> AutoTranslateTexts,
    IReadOnlyList<AutoTranslatePart> AutoTranslateParts)
{
    /// <summary>The message as the reader sees it (auto-translate shown in the client language).</summary>
    public string DisplayOriginal => Rehydrator.ToPlainText(Rehydrator.Original(Text, AutoTranslateTexts));

    /// <summary>Typed text only, for glossary matching.</summary>
    public string TypedText => Placeholders.TokenRegex().Replace(Text, " ");
}

/// <summary>Replaces auto-translate phrases with {{at0}}, {{at1}}, ... before the text goes to the model.</summary>
public static class Placeholderizer
{
    public const string Open  = "【";
    public const string Close = "】";

    public static PlaceholderizedMessage Build(IEnumerable<MessagePart> parts)
    {
        var sb    = new System.Text.StringBuilder();
        var map   = new Dictionary<string, string>(StringComparer.Ordinal);
        var atList = new List<AutoTranslatePart>();

        foreach (var part in parts)
        {
            switch (part)
            {
                case TextPart t:
                    sb.Append(t.Text);
                    break;
                case AutoTranslatePart at:
                    var id = Placeholders.AutoTranslateId(atList.Count);
                    map[id] = Wrap(at.DisplayText);
                    atList.Add(at);
                    // Keep words apart: "{{at0}}opo" would read as one token
                    if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1])) sb.Append(' ');
                    sb.Append(Placeholders.Token(id));
                    sb.Append(' ');
                    break;
            }
        }
        return new PlaceholderizedMessage(sb.ToString().Trim(), map, atList);
    }

    /// <summary>"Hello!" → "【Hello!】" (idempotent).</summary>
    public static string Wrap(string text)
    {
        var t = text.Trim();
        return t.StartsWith(Open, StringComparison.Ordinal) && t.EndsWith(Close, StringComparison.Ordinal) ? t : Open + t + Close;
    }
}
