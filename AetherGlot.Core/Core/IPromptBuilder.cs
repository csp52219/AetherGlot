namespace AetherGlot.Core;

/// <summary>
/// Builds LLM system prompts for inbound and outbound translation.
/// Pure string logic — no external dependencies.
/// </summary>
public interface IPromptBuilder
{
    /// <summary>
    /// System prompt for inbound translation.
    /// Instructs the LLM to return JSON: {"lang":"XX","text":"..."}.
    /// </summary>
    string BuildInboundSystemPrompt(string targetLanguage);

    /// <summary>
    /// System prompt for outbound translation.
    /// Wraps the user's custom prompt with target-language direction.
    /// Returns plain text (no JSON wrapper).
    /// </summary>
    string BuildOutboundSystemPrompt(string targetLanguage, string customPrompt);
}
