using AetherGlot.Core.Connection;

namespace AetherGlot.Core.Translation;

/// <summary>
/// Prompt → model → parse → validate → rehydrate, with one retry on parse failure and a
/// fallback to the original text. No Dalamud dependency.
/// </summary>
public interface ITranslationPipeline
{
    Task<InboundOutcome> TranslateInboundAsync(
        InboundRequest request, ConnectionSettings connection, PipelineOptions options,
        CancellationToken ct = default, bool bypassCache = false);

    /// <summary>
    /// Synchronous cache lookup (no network), for decisions that must be made inside the chat
    /// event, such as replacing the original line.
    /// </summary>
    bool TryGetCachedInbound(InboundRequest request, ConnectionSettings connection, out InboundOutcome outcome);

    Task<OutboundOutcome> TranslateOutboundAsync(
        OutboundRequest request, ConnectionSettings connection, PipelineOptions options,
        CancellationToken ct = default);
}
