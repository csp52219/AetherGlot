namespace AetherGlot.Core.Translation;

/// <summary>A piece of a line printed into the game chat.</summary>
public abstract record GameChatPiece;

public sealed record GameChatTextPiece(string Text) : GameChatPiece;

/// <summary>An auto-translate phrase, printed as the game's own payload (green 【】, any client language).</summary>
public sealed record GameChatAutoTranslatePiece(uint Group, uint Key, string FallbackText) : GameChatPiece;

/// <summary>Builds the game chat version of a translation.</summary>
public static class GameChatLine
{
    public const string UnresolvedOpen  = "‹";
    public const string UnresolvedClose = "›";

    /// <summary>
    /// Pieces for <paramref name="segments"/>. Auto-translate segments become payload pieces when
    /// their phrase is known (<paramref name="parts"/>, indexed by "atN"), otherwise plain text.
    /// Unresolved spans are wrapped in ‹ › when <paramref name="markUnresolved"/>.
    /// Adjacent text is merged.
    /// </summary>
    public static IReadOnlyList<GameChatPiece> Build(
        IReadOnlyList<DisplaySegment> segments, IReadOnlyList<AutoTranslatePart> parts, bool markUnresolved)
    {
        var result = new List<GameChatPiece>();
        foreach (var s in segments)
        {
            if (s.Kind == SegmentKind.AutoTranslate && PartFor(s.PlaceholderId, parts) is { } at)
            {
                result.Add(new GameChatAutoTranslatePiece(at.Group, at.Key, s.Text));
                continue;
            }
            var text = s.Kind == SegmentKind.Unresolved && markUnresolved ? UnresolvedOpen + s.Text + UnresolvedClose : s.Text;
            if (text.Length == 0) continue;
            if (result.Count > 0 && result[^1] is GameChatTextPiece last)
                result[^1] = new GameChatTextPiece(last.Text + text);
            else
                result.Add(new GameChatTextPiece(text));
        }
        return result;
    }

    /// <summary>Plain text of the pieces (auto-translate phrases as their display text).</summary>
    public static string ToPlainText(IEnumerable<GameChatPiece> pieces) =>
        string.Concat(pieces.Select(p => p switch
        {
            GameChatTextPiece t          => t.Text,
            GameChatAutoTranslatePiece a => a.FallbackText,
            _                            => string.Empty,
        }));

    private static AutoTranslatePart? PartFor(string? id, IReadOnlyList<AutoTranslatePart> parts)
    {
        if (id is null || !id.StartsWith(Placeholders.AutoTranslatePrefix, StringComparison.Ordinal)) return null;
        return int.TryParse(id.AsSpan(Placeholders.AutoTranslatePrefix.Length), out var i) && i >= 0 && i < parts.Count ? parts[i] : null;
    }
}
