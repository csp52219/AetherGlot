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

    Task<OutboundOutcome> TranslateOutboundAsync(
        OutboundRequest request, ConnectionSettings connection, PipelineOptions options,
        CancellationToken ct = default);
}
