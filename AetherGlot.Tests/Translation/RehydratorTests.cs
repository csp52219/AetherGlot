using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Translation;

public sealed class RehydratorTests
{
    private static readonly Dictionary<string, string> At = new()
    {
        ["at0"] = "【こんにちは！】",
        ["at1"] = "【モンク】",
    };

    [Fact]
    public void AutoTranslateAndUnresolved_AreFilledBack()
    {
        var segments = Rehydrator.Rehydrate(
            "{{at0}} 次は {{u0}} でやろう、{{at1}}さん",
            At,
            [new UnresolvedSpan { Id = "u0", Text = "zorp strat", Guess = "作戦名?" }]);

        Assert.Equal("【こんにちは！】 次は zorp strat でやろう、【モンク】さん", Rehydrator.ToPlainText(segments));
        Assert.Equal(
            [SegmentKind.AutoTranslate, SegmentKind.Translated, SegmentKind.Unresolved, SegmentKind.Translated, SegmentKind.AutoTranslate, SegmentKind.Translated],
            segments.Select(s => s.Kind));
        Assert.Equal("作戦名?", segments[2].Guess);
    }

    [Fact]
    public void UnknownToken_IsKeptVerbatim()
    {
        var segments = Rehydrator.Rehydrate("a {{u9}} b", At, []);
        Assert.Single(segments);
        Assert.Equal("a {{u9}} b", segments[0].Text);
    }

    [Fact]
    public void Original_FillsAutoTranslateOnly()
    {
        var segments = Rehydrator.Original("{{at0}} opo uptime pls {{at1}}", At);
        Assert.Equal("【こんにちは！】 opo uptime pls 【モンク】", Rehydrator.ToPlainText(segments));
        Assert.Equal(SegmentKind.Original, segments[1].Kind);
    }
}
