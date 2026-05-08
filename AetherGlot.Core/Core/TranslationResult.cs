namespace AetherGlot.Core;

/// <summary>
/// Immutable result returned by <see cref="ITranslationOrchestrator"/>.
/// </summary>
public sealed record TranslationResult(
    string OriginalText,
    string TranslatedText,
    string DetectedLanguage,
    bool IsSuccess,
    string? ErrorMessage = null)
{
    public static TranslationResult Success(string original, string translated, string detectedLang)
        => new(original, translated, detectedLang, true);

    public static TranslationResult Failure(string original, string error)
        => new(original, string.Empty, "??", false, error);
}
