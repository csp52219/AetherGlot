using System.Diagnostics;
using AetherGlot.Core.Translation;
using AetherGlot.Providers;

namespace AetherGlot.Core.Connection;

public enum DiagnosticStepKind
{
    CloudflareAccess,
    Authentication,
    ModelList,
    JsonInbound,
    JsonOutbound,
}

public enum DiagnosticStatus
{
    Ok,

    /// <summary>Worked, but with a caveat (e.g. JSON mode rejected, succeeded on retry).</summary>
    Warning,
    Failed,
    Skipped,
}

/// <summary>One line of the connection test. Detail and Hint never contain secrets.</summary>
public sealed record DiagnosticStep(
    DiagnosticStepKind Kind,
    DiagnosticStatus Status,
    string Detail,
    string? Hint = null,
    int? StatusCode = null,
    TimeSpan? Elapsed = null);

/// <summary>Result of <see cref="ConnectionDiagnostics.RunAsync"/>.</summary>
public sealed record DiagnosticReport(IReadOnlyList<DiagnosticStep> Steps, IReadOnlyList<string> Models)
{
    public bool AllPassed => Steps.All(s => s.Status is DiagnosticStatus.Ok or DiagnosticStatus.Warning or DiagnosticStatus.Skipped);
}

/// <summary>
/// Staged connection test:
/// 1. Cloudflare Access is passed, 2. LiteLLM authentication, 3. model list,
/// 4. JSON response from the inbound and outbound models.
/// Later stages are skipped when an earlier one fails.
/// </summary>
public sealed class ConnectionDiagnostics
{
    public const string HintAccess       = "Check that the Cloudflare Access policy uses the Service Auth action and that the service token (Client ID / Secret) is correct.";
    public const string HintUnauthorized = "The LiteLLM virtual key (or API key) is invalid or expired.";
    public const string HintModel        = "Check that the key is allowed to use this model.";

    private const string ProbeSystem = "You are a connectivity test. Reply with ONLY this JSON object and nothing else: {\"ok\":true}";
    private const string ProbeUser   = "ping";

    private readonly LlmHttpSender sender;
    private readonly ITranslatorProviderFactory providerFactory;
    private readonly IModelCapabilityStore capabilities;

    public ConnectionDiagnostics(LlmHttpSender sender, ITranslatorProviderFactory providerFactory, IModelCapabilityStore capabilities)
    {
        this.sender          = sender;
        this.providerFactory = providerFactory;
        this.capabilities    = capabilities;
    }

    public async Task<DiagnosticReport> RunAsync(ConnectionSettings settings, CancellationToken ct = default)
    {
        var steps  = new List<DiagnosticStep>();
        var models = new List<string>();
        var proxied = settings.Mode is ConnectionMode.LiteLlm or ConnectionMode.OpenAICompatible;

        // ── 1. Cloudflare Access ────────────────────────────────────────────
        if (!settings.CloudflareAccessEnabled || !proxied)
        {
            steps.Add(new(DiagnosticStepKind.CloudflareAccess, DiagnosticStatus.Skipped, "Cloudflare Access is not used."));
        }
        else
        {
            var access = await ProbeAccessAsync(settings, ct).ConfigureAwait(false);
            steps.Add(access);
            if (access.Status == DiagnosticStatus.Failed)
                return Finish(steps, models, fromStep: DiagnosticStepKind.Authentication);
        }

        // ── 2 + 3. Authentication and model list (OpenAI-compatible /v1/models) ──
        var provider = SafeProvider(settings, out var providerError);
        if (provider is null)
        {
            steps.Add(new(DiagnosticStepKind.Authentication, DiagnosticStatus.Failed, providerError!));
            return Finish(steps, models, fromStep: DiagnosticStepKind.ModelList);
        }

        if (provider is IModelLister lister && proxied)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                models.AddRange(await lister.ListModelsAsync(ct).ConfigureAwait(false));
                sw.Stop();
                steps.Add(new(DiagnosticStepKind.Authentication, DiagnosticStatus.Ok, "OK", StatusCode: 200, Elapsed: sw.Elapsed));
                steps.Add(ModelListStep(settings, models, sw.Elapsed));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (LlmHttpException ex)
            {
                // Access can still reject here when step 1 was skipped
                var hint = ex.IsCloudflareAccess ? HintAccess
                         : ex.Kind == HttpFailureKind.Unauthorized ? HintUnauthorized : null;
                steps.Add(new(DiagnosticStepKind.Authentication, DiagnosticStatus.Failed, ex.Message, hint, (int?)ex.StatusCode));
                return Finish(steps, models, fromStep: DiagnosticStepKind.ModelList);
            }
            catch (Exception ex)
            {
                steps.Add(new(DiagnosticStepKind.Authentication, DiagnosticStatus.Failed, Describe(ex)));
                return Finish(steps, models, fromStep: DiagnosticStepKind.ModelList);
            }
        }
        else
        {
            steps.Add(new(DiagnosticStepKind.Authentication, DiagnosticStatus.Skipped, "Checked by the JSON test below."));
            steps.Add(new(DiagnosticStepKind.ModelList, DiagnosticStatus.Skipped, "Model listing is only available through LiteLLM / OpenAI-compatible."));
        }

        // ── 4. JSON responses ───────────────────────────────────────────────
        steps.Add(await ProbeJsonAsync(DiagnosticStepKind.JsonInbound, provider, settings.InboundModel, settings, ct).ConfigureAwait(false));
        steps.Add(await ProbeJsonAsync(DiagnosticStepKind.JsonOutbound, provider, settings.OutboundModel, settings, ct).ConfigureAwait(false));

        return new DiagnosticReport(steps, models);
    }

    private async Task<DiagnosticStep> ProbeAccessAsync(ConnectionSettings settings, CancellationToken ct)
    {
        Uri root;
        try
        {
            root = EndpointResolver.Root(settings.BaseUrl);
        }
        catch (Exception ex)
        {
            return new(DiagnosticStepKind.CloudflareAccess, DiagnosticStatus.Failed, Describe(ex));
        }

        // Unauthenticated request with only the Access headers: any answer from the origin
        // (200/401/404 JSON...) proves Access let us through.
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "health/liveliness"));
        var sw = Stopwatch.StartNew();
        try
        {
            var exchange = await sender.SendAsync(request, settings, expectJson: false, ct).ConfigureAwait(false);
            sw.Stop();
            return new(DiagnosticStepKind.CloudflareAccess, DiagnosticStatus.Ok, ((int)exchange.StatusCode).ToString(),
                       StatusCode: (int)exchange.StatusCode, Elapsed: sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (LlmHttpException ex) when (ex.IsCloudflareAccess)
        {
            return new(DiagnosticStepKind.CloudflareAccess, DiagnosticStatus.Failed, ex.Message, HintAccess, (int?)ex.StatusCode);
        }
        catch (LlmHttpException ex)
        {
            // The origin answered (e.g. 401/404) → Access was passed
            sw.Stop();
            return new(DiagnosticStepKind.CloudflareAccess, DiagnosticStatus.Ok, $"{(int?)ex.StatusCode} (origin reached)",
                       StatusCode: (int?)ex.StatusCode, Elapsed: sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new(DiagnosticStepKind.CloudflareAccess, DiagnosticStatus.Failed, Describe(ex));
        }
    }

    private static DiagnosticStep ModelListStep(ConnectionSettings settings, IReadOnlyList<string> models, TimeSpan elapsed)
    {
        var missing = new[] { settings.InboundModel, settings.OutboundModel }
            .Where(m => !string.IsNullOrWhiteSpace(m) && !models.Contains(m, StringComparer.Ordinal))
            .Distinct()
            .ToList();

        return missing.Count == 0
            ? new(DiagnosticStepKind.ModelList, DiagnosticStatus.Ok, $"{models.Count} models", Elapsed: elapsed)
            : new(DiagnosticStepKind.ModelList, DiagnosticStatus.Warning,
                  $"{models.Count} models; not listed: {string.Join(", ", missing)}", HintModel, Elapsed: elapsed);
    }

    private async Task<DiagnosticStep> ProbeJsonAsync(
        DiagnosticStepKind kind, ITranslatorProvider provider, string model, ConnectionSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model))
            return new(kind, DiagnosticStatus.Failed, "No model is configured.");

        var useJson = settings.UseJsonResponseFormat;
        var retried = false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var response = await provider.CompleteAsync(new LlmRequest(model, ProbeSystem, ProbeUser, useJson, 32), ct).ConfigureAwait(false);
                if (useJson) capabilities.SetJsonModeSupport(model, true);

                var json = ResponseParser.ExtractJsonObject(response.Content);
                if (json is null)
                    return new(kind, DiagnosticStatus.Failed, $"{model}: the reply was not JSON.", Elapsed: response.Elapsed);

                return retried
                    ? new(kind, DiagnosticStatus.Warning, $"{model}: OK without response_format (prompt-only JSON).", Elapsed: response.Elapsed)
                    : new(kind, DiagnosticStatus.Ok, $"{model}: {response.Elapsed.TotalSeconds:0.00}s", Elapsed: response.Elapsed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (LlmHttpException ex) when (useJson && ex.Kind == HttpFailureKind.JsonModeNotSupported)
            {
                capabilities.SetJsonModeSupport(model, false);
                useJson = false;
                retried = true;
            }
            catch (LlmHttpException ex)
            {
                var hint = ex.IsCloudflareAccess ? HintAccess
                         : ex.Kind == HttpFailureKind.Unauthorized ? HintUnauthorized
                         : ex.Kind == HttpFailureKind.ModelNotFound ? HintModel : null;
                return new(kind, DiagnosticStatus.Failed, $"{model}: {ex.Message}", hint, (int?)ex.StatusCode);
            }
            catch (Exception ex)
            {
                return new(kind, DiagnosticStatus.Failed, $"{model}: {Describe(ex)}");
            }
        }
        return new(kind, DiagnosticStatus.Failed, $"{model}: no answer.");
    }

    private ITranslatorProvider? SafeProvider(ConnectionSettings settings, out string? error)
    {
        try
        {
            error = null;
            return providerFactory.GetProvider(settings);
        }
        catch (Exception ex)
        {
            error = Describe(ex);
            return null;
        }
    }

    private static DiagnosticReport Finish(List<DiagnosticStep> steps, List<string> models, DiagnosticStepKind fromStep)
    {
        foreach (var kind in Enum.GetValues<DiagnosticStepKind>().Where(k => k >= fromStep && steps.All(s => s.Kind != k)))
            steps.Add(new(kind, DiagnosticStatus.Skipped, "Skipped because an earlier step failed."));
        return new DiagnosticReport(steps, models);
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException     => "The request timed out.",
        HttpRequestException hre  => $"Network error: {hre.HttpRequestError}.",
        InvalidOperationException => ex.Message,
        UriFormatException        => "The configured URL is invalid.",
        _                         => $"Unexpected error ({ex.GetType().Name}).",
    };
}
