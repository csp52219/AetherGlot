using AetherGlot.Core.Connection;
using AetherGlot.Core.Translation;
using AetherGlot.Providers;
using Xunit;
using Xunit.Abstractions;

namespace AetherGlot.Tests.Integration;

/// <summary>
/// Real-API scenarios shared by every backend. Each derived class supplies settings;
/// tests are skipped when the backend's environment variables are not set.
/// </summary>
public abstract class PipelineIntegrationTestsBase : IDisposable
{
    private static readonly PipelineOptions Options = new();

    protected readonly HttpClient HttpClient = new(LlmHttpSender.CreateDefaultHandler()) { Timeout = TimeSpan.FromSeconds(60) };
    protected readonly LlmHttpSender Sender;
    protected readonly TranslationPipeline Pipeline;
    protected readonly ITestOutputHelper Output;

    protected PipelineIntegrationTestsBase(ITestOutputHelper output)
    {
        Output   = output;
        Sender   = new LlmHttpSender(HttpClient);
        Pipeline = new TranslationPipeline(new TranslatorProviderFactory(Sender), new PromptBuilder(),
                                           new ModelCapabilityStore(), new TranslationCache<InboundOutcome>(0));
    }

    /// <summary>Settings for this backend, or null to skip.</summary>
    protected abstract ConnectionSettings? Settings { get; }

    protected abstract string SkipReason { get; }

    protected ConnectionSettings RequireSettings()
    {
        Skip.If(Settings is null, SkipReason);
        return Settings!;
    }

    private static InboundRequest Inbound(string text, Dictionary<string, string>? at = null, IReadOnlyList<GlossaryHint>? glossary = null) =>
        new(text, at ?? new Dictionary<string, string>(), "Japanese", "ja", Glossary: glossary);

    private void Log(InboundOutcome o) =>
        Output.WriteLine($"[{o.Kind}] lang={o.DetectedLanguage} model={o.Model} {o.TotalElapsed.TotalSeconds:0.00}s | {o.DisplayText} | {o.FailureReason}");

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task Inbound_English_IsTranslatedToJapanese()
    {
        var settings = RequireSettings();
        var outcome  = await Pipeline.TranslateInboundAsync(Inbound("Hello, are you ready for the raid tonight?"), settings, Options);
        Log(outcome);

        Assert.True(outcome.Kind is InboundOutcomeKind.Translated or InboundOutcomeKind.Partial, outcome.FailureReason);
        Assert.Equal("en", outcome.DetectedLanguage, ignoreCase: true);
        Assert.False(outcome.Validation!.RefusalDetected);
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task Inbound_SameLanguage_IsSkipped()
    {
        var settings = RequireSettings();
        var outcome  = await Pipeline.TranslateInboundAsync(Inbound("了解です！よろしくお願いします。"), settings, Options);
        Log(outcome);

        Assert.Equal(InboundOutcomeKind.Skipped, outcome.Kind);
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task Inbound_PlaceholdersAndGlossary_ArePreserved()
    {
        var settings = RequireSettings();
        var at = new Dictionary<string, string> { ["at0"] = "【こんにちは！】", ["at1"] = "【モンク】" };
        var outcome = await Pipeline.TranslateInboundAsync(
            Inbound("{{at0}} opo > rapu > kua-ru, keep ur form uptime pls {{at1}}", at,
                    [new GlossaryHint("opo", ["壱の型：魔猿"]), new GlossaryHint("rapu", ["弐の型：走竜"]), new GlossaryHint("kua-ru", ["参の型：猛虎"])]),
            settings, Options);
        Log(outcome);

        Assert.NotEqual(InboundOutcomeKind.Fallback, outcome.Kind);
        Assert.Contains("【こんにちは！】", outcome.DisplayText);
        Assert.Contains("【モンク】", outcome.DisplayText);
        Assert.Contains("魔猿", outcome.DisplayText);
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task Inbound_Gibberish_DoesNotProduceRefusalText()
    {
        var settings = RequireSettings();
        var outcome  = await Pipeline.TranslateInboundAsync(Inbound("ok next pull we do the zorp blorp strat"), settings, Options);
        Log(outcome);

        // Either translated with unresolved spans, or rejected and shown as the original — never a refusal sentence
        Assert.DoesNotContain("申し訳", outcome.DisplayText);
        Assert.DoesNotContain("翻訳できません", outcome.DisplayText);
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task Outbound_Japanese_IsTranslatedWithBackTranslation()
    {
        var settings = RequireSettings();
        var outcome  = await Pipeline.TranslateOutboundAsync(
            new OutboundRequest("次のギミック頭割りだから集まって。メディカは私が入れます", "Japanese", "English",
                                OutboundTone.Short, FixedTerms: [new GlossaryHint("メディカ", ["Medica"])]),
            settings, Options);
        Output.WriteLine($"[Outbound] {outcome.Translation} | back={outcome.Response?.BackTranslation} | {outcome.FailureReason}");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Contains("Medica", outcome.Translation);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Response!.BackTranslation));
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task InvalidKey_FallsBack_WithoutLeakingTheKey()
    {
        var settings = RequireSettings();
        const string badKey = "sk-invalid-key-for-integration-test-0000";
        var bad = new ConnectionSettings
        {
            Mode = settings.Mode, DirectProvider = settings.DirectProvider, BaseUrl = settings.BaseUrl, ApiKey = badKey,
            UserTag = settings.UserTag, CloudflareAccessEnabled = settings.CloudflareAccessEnabled,
            CloudflareClientId = settings.CloudflareClientId, CloudflareClientSecret = settings.CloudflareClientSecret,
            InboundModel = settings.InboundModel, OutboundModel = settings.OutboundModel,
        };

        var outcome = await Pipeline.TranslateInboundAsync(Inbound("hello"), bad, Options);
        Log(outcome);

        Assert.Equal(InboundOutcomeKind.Fallback, outcome.Kind);
        Assert.NotNull(outcome.HttpFailure);
        Assert.DoesNotContain(badKey, outcome.FailureReason ?? string.Empty);
    }

    public void Dispose()
    {
        HttpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
