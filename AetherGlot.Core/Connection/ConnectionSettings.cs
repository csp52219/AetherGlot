namespace AetherGlot.Core.Connection;

/// <summary>
/// Runtime snapshot of everything needed to talk to the LLM backend, including secrets.
/// Built by the plugin from Configuration + the secret store right before use.
/// <para>
/// This is deliberately a class (not a record) so the compiler does not generate a
/// ToString() that would print secrets into logs.
/// </para>
/// </summary>
public sealed class ConnectionSettings
{
    public ConnectionMode Mode { get; init; } = ConnectionMode.LiteLlm;
    public DirectProvider DirectProvider { get; init; } = DirectProvider.OpenAI;

    /// <summary>
    /// Base URL for LiteLLM / OpenAI-compatible (e.g. https://llm.example.com), or the
    /// full endpoint URL/template for a Direct vendor (read from configuration).
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Vendor API key, LiteLLM virtual key, or bearer token. Secret.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Sent as the OpenAI "user" field (LiteLLM end-user tracking).</summary>
    public string UserTag { get; init; } = string.Empty;

    public bool CloudflareAccessEnabled { get; init; }
    public string CloudflareClientId { get; init; } = string.Empty;     // secret
    public string CloudflareClientSecret { get; init; } = string.Empty; // secret

    /// <summary>Additional headers (name, value). Values are treated as secrets.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ExtraHeaders { get; init; } = [];

    public string InboundModel { get; init; } = string.Empty;
    public string OutboundModel { get; init; } = string.Empty;

    /// <summary>Send response_format=json_object (falls back per model when rejected).</summary>
    public bool UseJsonResponseFormat { get; init; } = true;

    /// <summary>Every secret value held by this snapshot, for log redaction.</summary>
    public IEnumerable<string> SecretValues()
    {
        yield return ApiKey;
        yield return CloudflareClientId;
        yield return CloudflareClientSecret;
        foreach (var h in ExtraHeaders)
            yield return h.Value;
    }

    public override string ToString() => $"ConnectionSettings(Mode={Mode}, CfAccess={CloudflareAccessEnabled})";
}
