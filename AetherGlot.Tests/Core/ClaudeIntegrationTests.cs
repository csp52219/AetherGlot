using AetherGlot.Core;
using AetherGlot.Providers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AetherGlot.Tests.Core;

/// <summary>
/// Real HTTP integration tests against the Claude API.
/// Skipped unless the AETHERGLOT_CLAUDE_KEY environment variable is set.
///
/// Run with:
///   $env:AETHERGLOT_CLAUDE_KEY = "sk-ant-xxx"
///   dotnet test AetherGlot.Tests\AetherGlot.Tests.csproj --filter "Category=Integration" --verbosity normal
/// </summary>
public sealed class ClaudeIntegrationTests : IDisposable
{
    private static readonly string? ApiKey    = Environment.GetEnvironmentVariable("AETHERGLOT_CLAUDE_KEY");
    private static readonly bool    ShouldSkip = string.IsNullOrWhiteSpace(ApiKey);
    private const string            TestModel  = "claude-haiku-4-5-20251001";

    private readonly HttpClient              httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ITranslationOrchestrator orchestrator;

    public ClaudeIntegrationTests()
    {
        var factory       = new RealClaudeFactory(httpClient);
        var promptBuilder = new PromptBuilder();
        orchestrator      = new TranslationOrchestrator(factory, promptBuilder);
    }

    // ── Inbound ──────────────────────────────────────────────────────────────

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateInbound_Japanese_ReturnsEnglishJson()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_CLAUDE_KEY to run integration tests.");

        var options = new TranslationOptions("Claude", ApiKey!, TestModel);
        var result  = await orchestrator.TranslateInboundAsync(
            "こんにちは、今日はいい天気ですね。", "English", options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.TranslatedText));
        Assert.NotEqual("??", result.DetectedLanguage);
        Console.WriteLine($"[Inbound JA→EN] lang={result.DetectedLanguage} | {result.TranslatedText}");
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateInbound_English_ReturnsJapanese()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_CLAUDE_KEY to run integration tests.");

        var options = new TranslationOptions("Claude", ApiKey!, TestModel);
        var result  = await orchestrator.TranslateInboundAsync(
            "Hello, are you ready for the raid tonight?", "Japanese", options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.TranslatedText));
        Console.WriteLine($"[Inbound EN→JA] lang={result.DetectedLanguage} | {result.TranslatedText}");
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateInbound_FFXIVTerms_PreservesProperNouns()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_CLAUDE_KEY to run integration tests.");

        var options = new TranslationOptions("Claude", ApiKey!, TestModel);
        var result  = await orchestrator.TranslateInboundAsync(
            "光の戦士よ、コラプトを倒してオーラムヴェイルへ向かおう", "English", options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        // FFXIV proper nouns should appear in the translated output
        Assert.Contains("Warrior of Light", result.TranslatedText, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"[Inbound FFXIV] lang={result.DetectedLanguage} | {result.TranslatedText}");
    }

    // ── Outbound ─────────────────────────────────────────────────────────────

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateOutbound_Japanese_ReturnsEnglishSlang()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_CLAUDE_KEY to run integration tests.");

        const string customPrompt =
            "You are a player in an MMORPG. Translate casually. " +
            "Use gaming slang: 'ty', 'gg', 'brb', 'omw'. Avoid periods.";

        var options = new TranslationOptions("Claude", ApiKey!, TestModel);
        var result  = await orchestrator.TranslateOutboundAsync(
            "ちょっと待ってください、すぐ行きます", "English", customPrompt, options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.TranslatedText));
        Console.WriteLine($"[Outbound JA→EN] {result.TranslatedText}");
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateOutbound_EmptyCustomPrompt_StillTranslates()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_CLAUDE_KEY to run integration tests.");

        var options = new TranslationOptions("Claude", ApiKey!, TestModel);
        var result  = await orchestrator.TranslateOutboundAsync(
            "準備完了です！", "English", string.Empty, options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.TranslatedText));
        Console.WriteLine($"[Outbound no-prompt] {result.TranslatedText}");
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateInbound_InvalidApiKey_ReturnsFailure()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_CLAUDE_KEY to run integration tests.");

        var options = new TranslationOptions("Claude", "sk-ant-invalid-key", TestModel);
        var result  = await orchestrator.TranslateInboundAsync("test", "English", options);

        Assert.False(result.IsSuccess);
        Console.WriteLine($"[Error case] {result.ErrorMessage}");
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateInbound_ImmediatelyCancelled_ReturnsFailure()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_CLAUDE_KEY to run integration tests.");

        var cts     = new CancellationTokenSource();
        var options = new TranslationOptions("Claude", ApiKey!, TestModel);
        cts.Cancel();

        var result = await orchestrator.TranslateInboundAsync("test", "English", options, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("Cancelled", result.ErrorMessage);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public void Dispose() => httpClient.Dispose();

    private sealed class RealClaudeFactory(HttpClient client) : ITranslatorProviderFactory
    {
        public ITranslatorProvider GetProvider(string _) => new InlineClaudeProvider(client);
    }

    /// <summary>
    /// Minimal Claude provider inlined in the test project so it has no
    /// dependency on AetherGlot (plugin) project. Mirrors ClaudeProvider exactly.
    /// </summary>
    private sealed class InlineClaudeProvider(HttpClient httpClient) : ITranslatorProvider
    {
        private const string DefaultEndpoint   = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion  = "2023-06-01";
        private const int    MaxTokens         = 1024;

        public string ProviderName => "Claude";

        public async Task<string> TranslateAsync(
            string text, string systemPrompt, string userPrompt,
            string apiKey, string model, string? customEndpoint = null,
            CancellationToken ct = default)
        {
            var endpoint = string.IsNullOrWhiteSpace(customEndpoint) ? DefaultEndpoint : customEndpoint;

            var payload = JsonSerializer.Serialize(new
            {
                model,
                max_tokens = MaxTokens,
                system     = systemPrompt,
                messages   = new[] { new { role = "user", content = userPrompt } },
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("x-api-key",         apiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                      .GetProperty("content")[0]
                      .GetProperty("text")
                      .GetString() ?? string.Empty;
        }
    }
}
