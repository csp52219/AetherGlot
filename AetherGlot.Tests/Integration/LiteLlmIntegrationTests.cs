using AetherGlot.Core.Connection;
using AetherGlot.Core.Translation;
using AetherGlot.Providers;
using Xunit;
using Xunit.Abstractions;

namespace AetherGlot.Tests.Integration;

/// <summary>
/// LiteLLM proxy (+ Cloudflare Access when AETHERGLOT_CF_CLIENT_ID / _SECRET are set).
/// Required: AETHERGLOT_LITELLM_BASE_URL, AETHERGLOT_LITELLM_KEY, AETHERGLOT_LITELLM_INBOUND_MODEL.
/// Optional: AETHERGLOT_LITELLM_OUTBOUND_MODEL, AETHERGLOT_LITELLM_USER_TAG.
/// </summary>
public sealed class LiteLlmIntegrationTests(ITestOutputHelper output) : PipelineIntegrationTestsBase(output)
{
    protected override ConnectionSettings? Settings { get; } = IntegrationEnv.LiteLlm();

    protected override string SkipReason =>
        "Set AETHERGLOT_LITELLM_BASE_URL, AETHERGLOT_LITELLM_KEY and AETHERGLOT_LITELLM_INBOUND_MODEL to run LiteLLM integration tests.";

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task Diagnostics_AllStagesPass()
    {
        var settings = RequireSettings();
        var diag     = new ConnectionDiagnostics(Sender, new TranslatorProviderFactory(Sender), new ModelCapabilityStore());

        var report = await diag.RunAsync(settings);
        foreach (var s in report.Steps)
            Output.WriteLine($"{s.Kind}: {s.Status} {s.Detail} {s.Hint}");

        Assert.True(report.AllPassed);
        if (settings.CloudflareAccessEnabled)
            Assert.Equal(DiagnosticStatus.Ok, report.Steps.Single(s => s.Kind == DiagnosticStepKind.CloudflareAccess).Status);
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task ModelList_ContainsConfiguredModels()
    {
        var settings = RequireSettings();
        var provider = new LiteLlmProvider(Sender, settings);

        var models = await provider.ListModelsAsync();
        Output.WriteLine($"{models.Count} models");

        Assert.Contains(settings.InboundModel, models);
        Assert.Contains(settings.OutboundModel, models);
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task WrongAccessSecret_IsReportedAsCloudflareAccess()
    {
        var settings = RequireSettings();
        Skip.IfNot(settings.CloudflareAccessEnabled, "Cloudflare Access is not configured.");

        var bad = new ConnectionSettings
        {
            Mode = settings.Mode, BaseUrl = settings.BaseUrl, ApiKey = settings.ApiKey, UserTag = settings.UserTag,
            CloudflareAccessEnabled = true, CloudflareClientId = settings.CloudflareClientId,
            CloudflareClientSecret = "wrong-secret-for-integration-test",
            InboundModel = settings.InboundModel, OutboundModel = settings.OutboundModel,
        };

        var ex = await Assert.ThrowsAsync<LlmHttpException>(() => new LiteLlmProvider(Sender, bad).ListModelsAsync());
        Output.WriteLine($"{ex.Kind} {(int?)ex.StatusCode}");

        Assert.True(ex.IsCloudflareAccess, $"Expected a Cloudflare Access rejection but got {ex.Kind}.");
    }
}
