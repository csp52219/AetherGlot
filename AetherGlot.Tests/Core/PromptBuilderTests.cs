using AetherGlot.Core;
using Xunit;

namespace AetherGlot.Tests.Core;

public sealed class PromptBuilderTests
{
    private readonly IPromptBuilder sut = new PromptBuilder();

    // ── Inbound ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Japanese")]
    [InlineData("English")]
    [InlineData("German")]
    [InlineData("French")]
    public void BuildInboundSystemPrompt_ContainsTargetLanguage(string lang)
    {
        var prompt = sut.BuildInboundSystemPrompt(lang);

        Assert.Contains(lang, prompt);
    }

    [Fact]
    public void BuildInboundSystemPrompt_ContainsJsonFormatInstruction()
    {
        var prompt = sut.BuildInboundSystemPrompt("English");

        Assert.Contains("lang", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildInboundSystemPrompt_MentionsJsonOnly()
    {
        var prompt = sut.BuildInboundSystemPrompt("English");

        Assert.Contains("JSON", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── Outbound ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildOutboundSystemPrompt_ContainsTargetLanguage()
    {
        var prompt = sut.BuildOutboundSystemPrompt("Japanese", string.Empty);

        Assert.Contains("Japanese", prompt);
    }

    [Fact]
    public void BuildOutboundSystemPrompt_IncludesCustomPrompt_WhenProvided()
    {
        const string custom = "You are a casual gamer.";
        var prompt = sut.BuildOutboundSystemPrompt("English", custom);

        Assert.Contains(custom, prompt);
    }

    [Fact]
    public void BuildOutboundSystemPrompt_EmptyCustomPrompt_StillContainsTargetLanguage()
    {
        var prompt = sut.BuildOutboundSystemPrompt("German", string.Empty);

        Assert.Contains("German", prompt);
    }

    [Fact]
    public void BuildOutboundSystemPrompt_DoesNotContainJsonInstruction()
    {
        // Outbound should return plain text, not JSON
        var prompt = sut.BuildOutboundSystemPrompt("English", string.Empty);

        // The word "JSON" should not appear in the outbound prompt
        Assert.DoesNotContain("JSON", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
