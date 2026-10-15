using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Core;

public sealed class GameChatLineTests
{
    [Fact]
    public void AutoTranslatePhrases_BecomePayloadPieces()
    {
        var message = Placeholderizer.Build([new TextPart("brb "), new AutoTranslatePart(56, 1, "バイオ"), new TextPart(", 2 min")]);
        var segments = Rehydrator.Rehydrate("すぐ戻ります {{at0}}、2分です", message.AutoTranslateTexts, []);

        var pieces = GameChatLine.Build(segments, message.AutoTranslateParts, markUnresolved: false);

        Assert.Equal(3, pieces.Count);
        Assert.Equal(new GameChatTextPiece("すぐ戻ります "), pieces[0]);
        Assert.Equal(new GameChatAutoTranslatePiece(56, 1, "【バイオ】"), pieces[1]);
        Assert.Equal(new GameChatTextPiece("、2分です"), pieces[2]);
        Assert.Equal("すぐ戻ります 【バイオ】、2分です", GameChatLine.ToPlainText(pieces));
    }

    [Fact]
    public void UnresolvedSpans_AreMarked_OnlyWhenAsked()
    {
        var segments = Rehydrator.Rehydrate("次は {{u0}} でやろう", new Dictionary<string, string>(),
            [new UnresolvedSpan { Id = "u0", Text = "zorp strat" }]);

        Assert.Equal("次は ‹zorp strat› でやろう", GameChatLine.ToPlainText(GameChatLine.Build(segments, [], markUnresolved: true)));
        var plain = GameChatLine.Build(segments, [], markUnresolved: false);
        Assert.Single(plain);
        Assert.Equal("次は zorp strat でやろう", GameChatLine.ToPlainText(plain));
    }

    [Fact]
    public void UnknownPhrase_FallsBackToText()
    {
        var segments = new[] { new DisplaySegment("【?】", SegmentKind.AutoTranslate, PlaceholderId: "at5") };
        Assert.Equal([new GameChatTextPiece("【?】")], GameChatLine.Build(segments, [], false));
    }
}
