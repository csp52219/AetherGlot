using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Core;

public sealed class PromptBuilderTests
{
    private readonly IPromptBuilder sut = new PromptBuilder();

    private static InboundRequest Inbound(string lang = "Japanese") =>
        new("{{at0}} opo pls", new Dictionary<string, string> { ["at0"] = "【こんにちは！】" }, lang, "ja");

    [Theory]
    [InlineData("Japanese")]
    [InlineData("English")]
    [InlineData("German")]
    [InlineData("French")]
    public void InboundSystem_ContainsTargetLanguage(string lang)
        => Assert.Contains(lang, sut.BuildInboundSystemPrompt(Inbound(lang)));

    [Fact]
    public void InboundSystem_ContainsSchemaAndRules()
    {
        var p = sut.BuildInboundSystemPrompt(Inbound());
        Assert.Contains(PromptBuilder.InboundSchema, p);
        Assert.Contains("{{at0}}", p);
        Assert.Contains("unresolved", p);
        Assert.Contains("Never refuse", p);
        Assert.Contains("\"skip\"", p);
    }

    [Fact]
    public void InboundUser_IsJson_AndOmitsEmptySections()
    {
        var p = sut.BuildInboundUserPrompt(Inbound());
        Assert.Equal("""{"message":"{{at0}} opo pls"}""", p);
    }

    [Fact]
    public void InboundUser_IncludesOnlyGivenGlossaryAndContext()
    {
        var req = Inbound() with
        {
            Hints    = new TranslationHints(InCombat: false, Location: "Limsa Lominsa"),
            Glossary = [new GlossaryHint("opo", ["壱の型：魔猿"]), new GlossaryHint("bio", ["バイオ（技）", "トイレ（離席）"])],
        };
        var p = sut.BuildInboundUserPrompt(req);
        Assert.Contains("\"in_combat\":false", p);
        Assert.Contains("Limsa Lominsa", p);
        Assert.Contains("\"dst\":\"壱の型：魔猿\"", p);
        Assert.Contains("\"senses\":[\"バイオ（技）\",\"トイレ（離席）\"]", p);
        Assert.DoesNotContain("party_jobs", p);
    }

    [Fact]
    public void Outbound_IncludesCustomPromptToneAndLimit()
    {
        var req = new OutboundRequest("こんにちは", "Japanese", "English", OutboundTone.Short, "You are a casual gamer.");
        var p = sut.BuildOutboundSystemPrompt(req, 450);
        Assert.Contains("You are a casual gamer.", p);
        Assert.Contains("short", p);
        Assert.Contains("450", p);
        Assert.Contains(PromptBuilder.OutboundSchema, p);
    }
}
