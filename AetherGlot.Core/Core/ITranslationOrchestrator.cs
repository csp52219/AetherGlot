namespace AetherGlot.Core;

/// <summary>
/// Orchestrates a single translation request: builds the prompt, calls the provider,
/// and parses the response into a <see cref="TranslationResult"/>.
/// Has zero Dalamud dependencies — fully unit-testable.
/// </summary>
public interface ITranslationOrchestrator
{
    /// <summary>
    /// Translates inbound chat. Expects the provider to return JSON: {"lang":"XX","text":"..."}.
    /// </summary>
    Task<TranslationResult> TranslateInboundAsync(
        string text,
        string targetLanguage,
        TranslationOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Translates outbound chat using the user's custom prompt.
    /// Expects the provider to return plain translated text.
    /// </summary>
    Task<TranslationResult> TranslateOutboundAsync(
        string text,
        string targetLanguage,
        string customPrompt,
        TranslationOptions options,
        CancellationToken ct = default);
}
