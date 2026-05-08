namespace AetherGlot.Core;

/// <inheritdoc cref="IPromptBuilder"/>
public sealed class PromptBuilder : IPromptBuilder
{
    public string BuildInboundSystemPrompt(string targetLanguage) =>
        $"Translate the input text to {targetLanguage}. " +
        "Keep all FFXIV-specific terms, character names, and lore words intact. " +
        "Identify the source language. " +
        "Return ONLY valid JSON in this exact format: " +
        "{\"lang\":\"EN\",\"text\":\"translated text here\"}";

    public string BuildOutboundSystemPrompt(string targetLanguage, string customPrompt) =>
        string.IsNullOrWhiteSpace(customPrompt)
            ? $"Translate the user's text to {targetLanguage}. Return only the translated text."
            : $"{customPrompt.TrimEnd()}\nTranslate to {targetLanguage}. Return only the translated text.";
}
