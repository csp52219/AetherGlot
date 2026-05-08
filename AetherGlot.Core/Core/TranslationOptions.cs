namespace AetherGlot.Core;

/// <summary>
/// Value object carrying runtime provider configuration extracted from
/// <c>Configuration</c> by the service layer before calling the orchestrator.
/// Keeping this in Core avoids leaking Dalamud's IPluginConfiguration here.
/// </summary>
public sealed record TranslationOptions(
    string ProviderName,
    string ApiKey,
    string Model,
    string? CustomEndpoint = null);
