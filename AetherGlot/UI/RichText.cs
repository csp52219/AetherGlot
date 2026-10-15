using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherGlot.UI;

/// <summary>A run of text with one style.</summary>
internal readonly record struct TextRun(string Text, Vector4 Color, bool DashedUnderline = false);

/// <summary>
/// Draws styled runs inline. With wrapping, latin text breaks at spaces and Japanese text
/// between characters; without wrapping the line is cut with "…".
/// </summary>
internal static class RichText
{
    public static void Draw(IReadOnlyList<TextRun> runs, bool wrap)
    {
        var avail = Math.Max(40f, ImGui.GetContentRegionAvail().X);
        var x     = 0f;
        var first = true;

        foreach (var run in runs)
        {
            foreach (var chunk in Chunks(run.Text))
            {
                var width = ImGui.CalcTextSize(chunk).X;
                if (x > 0 && x + width > avail)
                {
                    if (!wrap)
                    {
                        ImGui.SameLine(0, 0);
                        ImGui.TextColored(Theme.TextMuted, "…");
                        return;
                    }
                    x = 0; // next item starts a new line
                    if (string.IsNullOrWhiteSpace(chunk)) continue;
                }
                else if (!first)
                {
                    ImGui.SameLine(0, 0);
                }

                ImGui.TextColored(run.Color, chunk);
                if (run.DashedUnderline) DashedUnderline(run.Color);
                x += width;
                first = false;
            }
        }
        if (first) ImGui.TextUnformatted(string.Empty);
    }

    /// <summary>A rounded "chip" label, optionally clickable. Returns true when clicked.</summary>
    public static bool Chip(string text, Vector4 color, Vector4 background, bool dashedBorder = false)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pad  = new Vector2(6, 3);
        var size = ImGui.CalcTextSize(text) + pad * 2;
        var pos  = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##chip{text}{pos.X}{pos.Y}", size);
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(background), 3f);
        if (dashedBorder)
            DashedRect(drawList, pos, pos + size, ImGui.GetColorU32(color with { W = 0.6f }));
        drawList.AddText(pos + pad, ImGui.GetColorU32(color), text);
        return clicked;
    }

    /// <summary>Places the next chip on the same line if it fits.</summary>
    public static void SameLineIfFits(string nextText)
    {
        var next = ImGui.CalcTextSize(nextText).X + 12 + 4;
        var lastMaxX = ImGui.GetItemRectMax().X;
        var windowRight = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        if (lastMaxX + 4 + next < windowRight) ImGui.SameLine(0, 4);
    }

    private static void DashedUnderline(Vector4 color)
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var col = ImGui.GetColorU32(color);
        var drawList = ImGui.GetWindowDrawList();
        for (var x = min.X; x < max.X; x += 6)
            drawList.AddLine(new Vector2(x, max.Y), new Vector2(Math.Min(x + 3, max.X), max.Y), col, 1f);
    }

    private static void DashedRect(ImDrawListPtr dl, Vector2 a, Vector2 b, uint col)
    {
        for (var x = a.X; x < b.X; x += 6)
        {
            dl.AddLine(new Vector2(x, a.Y), new Vector2(Math.Min(x + 3, b.X), a.Y), col, 1f);
            dl.AddLine(new Vector2(x, b.Y - 1), new Vector2(Math.Min(x + 3, b.X), b.Y - 1), col, 1f);
        }
        for (var y = a.Y; y < b.Y; y += 6)
        {
            dl.AddLine(new Vector2(a.X, y), new Vector2(a.X, Math.Min(y + 3, b.Y)), col, 1f);
            dl.AddLine(new Vector2(b.X - 1, y), new Vector2(b.X - 1, Math.Min(y + 3, b.Y)), col, 1f);
        }
    }

    /// <summary>Breakable pieces: a word with its trailing spaces, or a single CJK character.</summary>
    private static IEnumerable<string> Chunks(string text)
    {
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (IsCjk(c))
            {
                if (i > start) yield return text[start..i];
                // Keep closing punctuation with the previous character
                var end = i + 1;
                while (end < text.Length && IsClosingPunctuation(text[end])) end++;
                yield return text[i..end];
                i = start = end;
                continue;
            }
            if (c == ' ')
            {
                var end = i + 1;
                while (end < text.Length && text[end] == ' ') end++;
                yield return text[start..end];
                i = start = end;
                continue;
            }
            i++;
        }
        if (start < text.Length) yield return text[start..];
    }

    private static bool IsCjk(char c) =>
        c is >= '　' and <= 'ヿ' or >= '一' and <= '鿿' or >= '＀' and <= '￯';

    private static bool IsClosingPunctuation(char c) =>
        c is '、' or '。' or '」' or '』' or '）' or '】' or '！' or '？' or 'ー' or '…' or '，' or '．';
}
