using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Translation;

public sealed class ResponseValidatorTests
{
    private static InboundResponse Resp(string translation, string status = "ok", params UnresolvedSpan[] unresolved) => new()
    {
        Status = status, Lang = "en", Translation = translation, Unresolved = unresolved.ToList(),
    };

    [Fact]
    public void AllPlaceholdersKept_IsAcceptable()
    {
        var v = ResponseValidator.ValidateInbound(Resp("{{at0}} 型を切らさないでね {{at1}}"), "{{at0}} opo uptime pls {{at1}}");
        Assert.True(v.PlaceholdersPreserved);
        Assert.True(v.IsAcceptable(discardRefusals: true));
    }

    [Fact]
    public void MissingPlaceholder_IsRejected()
    {
        var v = ResponseValidator.ValidateInbound(Resp("こんにちは！型を切らさないでね"), "{{at0}} opo uptime pls {{at1}}");
        Assert.False(v.PlaceholdersPreserved);
        Assert.Equal(["at0", "at1"], v.MissingPlaceholders);
        Assert.False(v.IsAcceptable(discardRefusals: false));
    }

    [Fact]
    public void InventedPlaceholder_IsRejected()
    {
        var v = ResponseValidator.ValidateInbound(Resp("{{at0}} と {{at7}} と {{u3}}"), "{{at0}} hi");
        Assert.Equal(["at7", "u3"], v.UnknownPlaceholders);
        Assert.False(v.IsAcceptable(true));
    }

    [Fact]
    public void UnresolvedPlaceholder_WithEntry_IsAcceptable()
    {
        var v = ResponseValidator.ValidateInbound(
            Resp("次は {{u0}} でやろう", "partial", new UnresolvedSpan { Id = "u0", Text = "zorp strat" }),
            "next one we do the zorp strat");
        Assert.True(v.IsAcceptable(true));
        Assert.Equal(1, v.UnresolvedCount);
    }

    [Fact]
    public void Refusal_RejectedOnlyWhenDiscardEnabled()
    {
        var v = ResponseValidator.ValidateInbound(Resp("申し訳ありませんが翻訳できません"), "zorp");
        Assert.True(v.RefusalDetected);
        Assert.False(v.IsAcceptable(discardRefusals: true));
        Assert.True(v.IsAcceptable(discardRefusals: false));
    }

    [Fact]
    public void Skip_IsAlwaysAcceptable()
    {
        var v = ResponseValidator.ValidateInbound(Resp("", "skip"), "{{at0}} 了解です");
        Assert.True(v.IsAcceptable(true));
    }
}
