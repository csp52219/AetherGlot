using AetherGlot.Core.Security;

namespace AetherGlot.Core.Connection;

/// <summary>
/// Persisted connection settings (part of the plugin's Configuration.json).
/// Holds NO secrets: keys, the Cloudflare service token and extra header values live in
/// the <see cref="ISecretStore"/>.
/// </summary>
[Serializable]
public sealed class ConnectionConfig
{
    public ConnectionMode Mode { get; set; } = ConnectionMode.Direct;
    public DirectProvider DirectProvider { get; set; } = DirectProvider.OpenAI;

    /// <summary>
    /// Vendor endpoints for Direct mode, editable by the user. Gemini's contains "{model}".
    /// These are configuration defaults, not hardcoded in the providers.
    /// </summary>
    public Dictionary<string, string> DirectEndpoints { get; set; } = new()
    {
        [nameof(Connection.DirectProvider.OpenAI)] = "https://api.openai.com/v1/chat/completions",
        [nameof(Connection.DirectProvider.Claude)] = "https://api.anthropic.com/v1/messages",
        [nameof(Connection.DirectProvider.Gemini)] = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
    };

    public string LiteLlmBaseUrl { get; set; } = string.Empty;
    public string OpenAICompatibleBaseUrl { get; set; } = string.Empty;

    /// <summary>Sent as the "user" field (LiteLLM / OpenAI-compatible).</summary>
    public string UserTag { get; set; } = "aetherglot";

    public bool CloudflareAccessEnabled { get; set; }

    /// <summary>Names of additional headers; values are in the secret store.</summary>
    public List<string> ExtraHeaderNames { get; set; } = [];

    public string InboundModel { get; set; } = string.Empty;
    public string OutboundModel { get; set; } = string.Empty;

    public bool UseJsonResponseFormat { get; set; } = true;

    /// <summary>Per-model memory of whether response_format is accepted.</summary>
    public Dictionary<string, bool> ModelJsonSupport { get; set; } = new();

    public int RequestTimeoutSeconds { get; set; } = 30;
}

/// <summary>Builds a <see cref="ConnectionSettings"/> snapshot from config + secrets.</summary>
public static class ConnectionSettingsBuilder
{
    public static string SecretNameForApiKey(ConnectionConfig config) => config.Mode switch
    {
        ConnectionMode.LiteLlm          => SecretNames.LiteLlmKey,
        ConnectionMode.OpenAICompatible => SecretNames.OpenAICompatibleKey,
        _                               => SecretNames.DirectApiKey(config.DirectProvider.ToString()),
    };

    public static ConnectionSettings Build(ConnectionConfig config, ISecretStore secrets)
    {
        var proxied = config.Mode != ConnectionMode.Direct;
        var baseUrl = config.Mode switch
        {
            ConnectionMode.LiteLlm          => config.LiteLlmBaseUrl,
            ConnectionMode.OpenAICompatible => config.OpenAICompatibleBaseUrl,
            _ => config.DirectEndpoints.TryGetValue(config.DirectProvider.ToString(), out var e) ? e : string.Empty,
        };

        var cf = proxied && config.CloudflareAccessEnabled;
        var headers = proxied
            ? config.ExtraHeaderNames
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(n => new KeyValuePair<string, string>(n.Trim(), secrets.Get(SecretNames.ExtraHeader(n)) ?? string.Empty))
                    .ToList()
            : [];

        return new ConnectionSettings
        {
            Mode                    = config.Mode,
            DirectProvider          = config.DirectProvider,
            BaseUrl                 = baseUrl ?? string.Empty,
            ApiKey                  = secrets.Get(SecretNameForApiKey(config)) ?? string.Empty,
            UserTag                 = proxied ? config.UserTag ?? string.Empty : string.Empty,
            CloudflareAccessEnabled = cf,
            CloudflareClientId      = cf ? secrets.Get(SecretNames.CfClientId) ?? string.Empty : string.Empty,
            CloudflareClientSecret  = cf ? secrets.Get(SecretNames.CfClientSecret) ?? string.Empty : string.Empty,
            ExtraHeaders            = headers,
            InboundModel            = config.InboundModel ?? string.Empty,
            OutboundModel           = string.IsNullOrWhiteSpace(config.OutboundModel) ? config.InboundModel ?? string.Empty : config.OutboundModel,
            UseJsonResponseFormat   = config.UseJsonResponseFormat,
        };
    }

    /// <summary>Human-readable reason the connection cannot be used yet, or null.</summary>
    public static string? MissingRequirement(ConnectionSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.BaseUrl)) return "The endpoint / base URL is not configured.";
        if (string.IsNullOrWhiteSpace(s.InboundModel)) return "No model is configured.";
        if (s.Mode != ConnectionMode.OpenAICompatible && string.IsNullOrWhiteSpace(s.ApiKey)) return "The API key is not configured.";
        if (s.CloudflareAccessEnabled && (string.IsNullOrWhiteSpace(s.CloudflareClientId) || string.IsNullOrWhiteSpace(s.CloudflareClientSecret)))
            return "Cloudflare Access is enabled but the service token is incomplete.";
        return null;
    }
}
