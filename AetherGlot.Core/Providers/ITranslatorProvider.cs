namespace AetherGlot.Providers;

/// <summary>
/// Abstraction over a single LLM API backend.
/// Implementations live in the plugin project; this interface lives in Core so
/// it can be referenced by both the orchestrator and unit-test mocks without
/// pulling in any Dalamud assemblies.
/// </summary>
public interface ITranslatorProvider
{
    string ProviderName { get; }

    /// <summary>
    /// Calls the LLM API and returns the raw model response string.
    /// For inbound translation the orchestrator expects JSON {"lang":"XX","text":"..."}.
    /// For outbound it expects plain translated text.
    /// </summary>
    Task<string> TranslateAsync(
        string text,
        string systemPrompt,
        string userPrompt,
        string apiKey,
        string model,
        string? customEndpoint = null,
        CancellationToken ct = default);
}
