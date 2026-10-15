namespace AetherGlot.Core.Translation;

/// <summary>Builds system / user prompts. Pure string logic.</summary>
public interface IPromptBuilder
{
    string BuildInboundSystemPrompt(InboundRequest request);
    string BuildInboundUserPrompt(InboundRequest request);
    string BuildOutboundSystemPrompt(OutboundRequest request, int maxChars);
    string BuildOutboundUserPrompt(OutboundRequest request);
}
