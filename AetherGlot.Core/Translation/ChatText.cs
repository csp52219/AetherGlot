using System.Text;

namespace AetherGlot.Core.Translation;

/// <summary>Prepares text for the game's chat box.</summary>
public static class ChatText
{
    /// <summary>The chat input accepts at most 500 bytes (UTF-8).</summary>
    public const int MaxBytes = 500;

    /// <summary>
    /// One line, no control characters, collapsed whitespace, cut at <paramref name="maxBytes"/>
    /// UTF-8 bytes without splitting a character.
    /// </summary>
    public static (string Text, bool Truncated) Sanitize(string? text, int maxBytes = MaxBytes)
    {
        if (string.IsNullOrWhiteSpace(text)) return (string.Empty, false);

        var sb = new StringBuilder(text.Length);
        var lastSpace = false;
        foreach (var c in text)
        {
            var ch = char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c;
            var space = char.IsWhiteSpace(ch);
            if (space && lastSpace) continue;
            sb.Append(space ? ' ' : ch);
            lastSpace = space;
        }
        var clean = sb.ToString().Trim();

        if (Encoding.UTF8.GetByteCount(clean) <= maxBytes) return (clean, false);

        var cut = new StringBuilder();
        var bytes = 0;
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(clean);
        while (e.MoveNext())
        {
            var element = e.GetTextElement();
            var n = Encoding.UTF8.GetByteCount(element);
            if (bytes + n > maxBytes) break;
            cut.Append(element);
            bytes += n;
        }
        return (cut.ToString().TrimEnd(), true);
    }

    public static int ByteCount(string? text) => string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);
}
