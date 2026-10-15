using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Translation;

public sealed class ResponseParserTests
{
    private const string Valid =
        """{"status":"partial","lang":"en","translation":"正直微妙だった、次は {{u0}} で","terms":[{"src":"mid","dst":"微妙","from":"slang","conf":0.88}],"unresolved":[{"id":"u0","text":"zorp strat","guess":null}],"senses":[]}""";

    [Fact]
    public void ValidJson_Parses()
    {
        Assert.True(ResponseParser.TryParseInbound(Valid, out var r, out var error), error);
        Assert.Equal("partial", r!.Status);
        Assert.Equal("en", r.Lang);
        Assert.Single(r.Terms);
        Assert.Equal(0.88, r.Terms[0].Conf);
        Assert.Equal("zorp strat", r.Unresolved[0].Text);
    }

    [Fact]
    public void CodeFenceAndProse_AreStripped()
    {
        var raw = "Sure! Here is the JSON:\n```json\n" + Valid + "\n```\nHope this helps.";
        Assert.True(ResponseParser.TryParseInbound(raw, out var r, out _));
        Assert.Equal("partial", r!.Status);
    }

    [Fact]
    public void BracesInsideStrings_DoNotBreakExtraction()
    {
        const string raw = """{"status":"ok","lang":"en","translation":"{{at0}} は {x} だよ \"}\"","terms":[],"unresolved":[]}""";
        Assert.True(ResponseParser.TryParseInbound(raw, out var r, out var error), error);
        Assert.Equal("{{at0}} は {x} だよ \"}\"", r!.Translation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("申し訳ありませんが、この文章は翻訳できません。")]
    [InlineData("{\"status\":\"ok\",\"lang\":\"en\",\"translation\":\"途中で切れ")]
    [InlineData("{\"status\":\"ok\",,,}")]
    public void BrokenJson_Fails(string raw)
    {
        Assert.False(ResponseParser.TryParseInbound(raw, out var r, out var error));
        Assert.Null(r);
        Assert.StartsWith("Parse:", error);
    }

    [Theory]
    [InlineData("""{"status":"done","lang":"en","translation":"x"}""")]
    [InlineData("""{"lang":"en","translation":"x"}""")]
    [InlineData("""{"status":"ok","lang":"en"}""")]
    [InlineData("""{"status":"ok","lang":"en","translation":"   "}""")]
    [InlineData("""{"status":"ok","lang":"en","translation":"x","unresolved":[{"id":"zz","text":"a"}]}""")]
    [InlineData("""{"status":"ok","lang":"en","translation":"x","terms":[{"dst":"a"}]}""")]
    public void SchemaMismatch_Fails(string raw)
    {
        Assert.False(ResponseParser.TryParseInbound(raw, out _, out var error));
        Assert.StartsWith("Schema:", error);
    }

    [Fact]
    public void WrongType_FailsAsParseError()
    {
        Assert.False(ResponseParser.TryParseInbound("""{"status":"ok","lang":"en","translation":42}""", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Skip_AllowsEmptyTranslation()
    {
        Assert.True(ResponseParser.TryParseInbound("""{"status":"skip","lang":"ja","translation":""}""", out var r, out _));
        Assert.Equal("skip", r!.Status);
        Assert.Empty(r.Terms);
    }

    [Fact]
    public void StatusIsNormalised()
    {
        Assert.True(ResponseParser.TryParseInbound("""{"status":" OK ","lang":"en","translation":"はい"}""", out var r, out _));
        Assert.Equal("ok", r!.Status);
    }

    [Fact]
    public void Outbound_ParsesAndDropsEmptyAlternatives()
    {
        const string raw = """{"translation":"Stack for the next mechanic.","back_translation":"次のギミックは頭割り。","alternatives":["Stack up next mech","", null],"terms":[{"src":"頭割り","dst":"stack"}]}""";
        Assert.True(ResponseParser.TryParseOutbound(raw, out var r, out var error), error);
        Assert.Single(r!.Alternatives);
        Assert.Equal("stack", r.Terms[0].Dst);
    }

    [Fact]
    public void Outbound_MissingTranslation_Fails()
    {
        Assert.False(ResponseParser.TryParseOutbound("""{"back_translation":"x"}""", out _, out var error));
        Assert.StartsWith("Schema:", error);
    }
}
