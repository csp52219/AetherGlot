namespace AetherGlot.Providers;

/// <summary>
/// UI-facing metadata only. Provider instantiation is handled by
/// <see cref="TranslatorProviderFactory"/> (implements ITranslatorProviderFactory).
/// </summary>
public static class ProviderFactory
{
    public static readonly string[] AvailableProviders = ["OpenAI", "Gemini", "Claude", "Custom"];

    /// <summary>Well-known model suggestions shown in the UI combo box.</summary>
    public static readonly Dictionary<string, string[]> DefaultModels = new()
    {
        ["OpenAI"] = ["gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-3.5-turbo"],
        ["Gemini"] = ["gemini-2.0-flash", "gemini-2.0-flash-lite", "gemini-1.5-pro", "gemini-1.5-flash"],
        ["Claude"] = ["claude-opus-4-7", "claude-sonnet-4-6", "claude-opus-4-6", "claude-haiku-4-5-20251001"],
        ["Custom"] = [],
    };
}
