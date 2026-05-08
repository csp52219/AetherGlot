using AetherGlot.Core;
using AetherGlot.Providers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AetherGlot.Tests.Core;

/// <summary>
/// Real HTTP integration tests against the Google Gemini generateContent API.
/// Skipped unless the AETHERGLOT_GEMINI_KEY environment variable is set.
///
/// Run with:
///   $env:AETHERGLOT_GEMINI_KEY = "AIza..."
///   dotnet test AetherGlot.Tests\AetherGlot.Tests.csproj --filter "Category=Integration" --verbosity normal
///
/// Or via 1Password CLI — see Run-IntegrationTests.ps1 in the repo root.
/// </summary>
public sealed class GeminiIntegrationTests : IDisposable
{
    private static readonly string? ApiKey     = Environment.GetEnvironmentVariable("AETHERGLOT_GEMINI_KEY");
    private static readonly bool    ShouldSkip = string.IsNullOrWhiteSpace(ApiKey);
    private const string            TestModel  = "gemini-2.0-flash";

    private readonly HttpClient               httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ITranslationOrchestrator orchestrator;

    public GeminiIntegrationTests()
    {
        var factory       = new RealGeminiFactory(httpClient);
        var promptBuilder = new PromptBuilder();
        orchestrator      = new TranslationOrchestrator(factory, promptBuilder);
    }

    // ── Inbound ──────────────────────────────────────────────────────────────

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateInbound_Japanese_ReturnsEnglishJson()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_GEMINI_KEY to run Gemini integration tests.");

        var options = new TranslationOptions("Gemini", ApiKey!, TestModel);
        var result  = await orchestrator.TranslateInboundAsync(
            "こんにちは、今日はいい天気ですね。", "English", options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.TranslatedText));
        Assert.NotEqual("??", result.DetectedLanguage);
        Console.WriteLine($"[Gemini Inbound JA→EN] lang={result.DetectedLanguage} | {result.TranslatedText}");
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateInbound_English_ReturnsJapanese()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_GEMINI_KEY to run Gemini integration tests.");

        var options = new TranslationOptions("Gemini", ApiKey!, TestModel);
        var result  = await orchestrator.TranslateInboundAsync(
            "Hello, are you ready for the raid tonight?", "Japanese", options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.TranslatedText));
        Console.WriteLine($"[Gemini Inbound EN→JA] lang={result.DetectedLanguage} | {result.TranslatedText}");
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateInbound_FFXIVTerms_PreservesProperNouns()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_GEMINI_KEY to run Gemini integration tests.");

        var options = new TranslationOptions("Gemini", ApiKey!, TestModel);
        var result  = await orchestrator.TranslateInboundAsync(
            "光の戦士よ、コラプトを倒してオーラムヴェイルへ向かおう", "English", options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("Warrior of Light", result.TranslatedText, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"[Gemini Inbound FFXIV] lang={result.DetectedLanguage} | {result.TranslatedText}");
    }

    // ── Outbound ─────────────────────────────────────────────────────────────

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateOutbound_Japanese_ReturnsEnglishSlang()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_GEMINI_KEY to run Gemini integration tests.");

        const string customPrompt =
            "You are a player in an MMORPG. Translate casually. " +
            "Use gaming slang: 'ty', 'gg', 'brb', 'omw'. Avoid periods.";

        var options = new TranslationOptions("Gemini", ApiKey!, TestModel);
        var result  = await orchestrator.TranslateOutboundAsync(
            "ちょっと待ってください、すぐ行きます", "English", customPrompt, options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.TranslatedText));
        Console.WriteLine($"[Gemini Outbound JA→EN] {result.TranslatedText}");
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateOutbound_EmptyCustomPrompt_StillTranslates()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_GEMINI_KEY to run Gemini integration tests.");

        var options = new TranslationOptions("Gemini", ApiKey!, TestModel);
        var result  = await orchestrator.TranslateOutboundAsync(
            "準備完了です！", "English", string.Empty, options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.TranslatedText));
        Console.WriteLine($"[Gemini Outbound no-prompt] {result.TranslatedText}");
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task TranslateInbound_InvalidApiKey_ReturnsFailure()
    {
        Skip.If(ShouldSkip, "Set env var AETHERGLOT_GEMINI_KEY to run Gemini integration tests.");

        var options = new TranslationOptions("Gemini", "invalid-gemini-key", TestModel);
        var result  = await orchestrator.TranslateInboundAsync("test", "English", options);

        Assert.False(result.IsSuccess);
        Console.WriteLine($"[Gemini Error case] {result.ErrorMessage}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public void Dispose() => httpClient.Dispose();

    private sealed class RealGeminiFactory(HttpClient client) : ITranslatorProviderFactory
    {
        public ITranslatorProvider GetProvider(string _) => new InlineGeminiProvider(client);
    }

    /// <summary>
    /// Minimal Gemini provider inlined in the test project.
    /// Does not force responseMimeType=application/json so the same provider works for
    /// both inbound (JSON via system prompt) and outbound (plain text).
    /// </summary>
    private sealed class InlineGeminiProvider(HttpClient httpClient) : ITranslatorProvider
    {
        private const string EndpointTemplate =
            "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}";

        public string ProviderName => "Gemini";

        public async Task<string> TranslateAsync(
            string text,
            string systemPrompt,
            string userPrompt,
            string apiKey,
            string model,
            string? customEndpoint = null,
            CancellationToken ct = default)
        {
            var endpoint = string.Format(EndpointTemplate, model, apiKey);

            var payload = JsonSerializer.Serialize(new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = systemPrompt } },
                },
                contents = new[]
                {
                    new { parts = new[] { new { text = userPrompt } } },
                },
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                      .GetProperty("candidates")[0]
                      .GetProperty("content")
                      .GetProperty("parts")[0]
                      .GetProperty("text")
                      .GetString() ?? string.Empty;
        }
    }
}
