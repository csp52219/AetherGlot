using AetherGlot.Core.Connection;

namespace AetherGlot.Tests.Integration;

/// <summary>
/// Reads integration-test settings from environment variables.
/// Locally they are injected by Run-IntegrationTests.ps1 (1Password `op run`);
/// in CI by .github/workflows/integration.yml (GitHub environment "integration").
/// Values are never printed.
/// </summary>
internal static class IntegrationEnv
{
    public static string? Get(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    /// <summary>LiteLLM (+ optional Cloudflare Access) settings, or null when not configured.</summary>
    public static ConnectionSettings? LiteLlm()
    {
        var baseUrl  = Get("AETHERGLOT_LITELLM_BASE_URL");
        var key      = Get("AETHERGLOT_LITELLM_KEY");
        var inbound  = Get("AETHERGLOT_LITELLM_INBOUND_MODEL");
        if (baseUrl is null || key is null || inbound is null) return null;

        var cfId     = Get("AETHERGLOT_CF_CLIENT_ID");
        var cfSecret = Get("AETHERGLOT_CF_CLIENT_SECRET");
        return new ConnectionSettings
        {
            Mode                    = ConnectionMode.LiteLlm,
            BaseUrl                 = baseUrl,
            ApiKey                  = key,
            UserTag                 = Get("AETHERGLOT_LITELLM_USER_TAG") ?? string.Empty,
            CloudflareAccessEnabled = cfId is not null && cfSecret is not null,
            CloudflareClientId      = cfId ?? string.Empty,
            CloudflareClientSecret  = cfSecret ?? string.Empty,
            InboundModel            = inbound,
            OutboundModel           = Get("AETHERGLOT_LITELLM_OUTBOUND_MODEL") ?? inbound,
            UseJsonResponseFormat   = true,
        };
    }

    /// <summary>Direct vendor settings. The endpoint may be overridden by env var.</summary>
    public static ConnectionSettings? Direct(DirectProvider provider, string keyVar, string modelVar, string endpointVar, string defaultEndpoint)
    {
        var key   = Get(keyVar);
        var model = Get(modelVar);
        if (key is null || model is null) return null;
        return new ConnectionSettings
        {
            Mode                  = ConnectionMode.Direct,
            DirectProvider        = provider,
            BaseUrl               = Get(endpointVar) ?? defaultEndpoint,
            ApiKey                = key,
            InboundModel          = model,
            OutboundModel         = model,
            UseJsonResponseFormat = true,
        };
    }
}
