namespace AetherGlot.Core.Translation;

/// <summary>Game-state hints sent with a message so the model can disambiguate.</summary>
public sealed record TranslationHints(
    bool? InCombat = null,
    string? Location = null,
    IReadOnlyList<string>? PartyJobs = null,
    IReadOnlyList<string>? RecentLines = null);

/// <summary>
/// A glossary entry relevant to this message. One target = fixed translation;
/// several targets = senses the model must choose from.
/// </summary>
public sealed record GlossaryHint(string Source, IReadOnlyList<string> Targets, string? Note = null);

/// <summary>Inbound (receive) translation request.</summary>
/// <param name="SourceText">Message text with auto-translate phrases replaced by {{atN}}.</param>
/// <param name="AutoTranslateTexts">atN → text to display (client language, with 【】).</param>
/// <param name="TargetLanguage">Client language name, e.g. "Japanese".</param>
/// <param name="TargetLanguageCode">ISO 639-1 code, e.g. "ja".</param>
public sealed record InboundRequest(
    string SourceText,
    IReadOnlyDictionary<string, string> AutoTranslateTexts,
    string TargetLanguage,
    string TargetLanguageCode,
    TranslationHints? Hints = null,
    IReadOnlyList<GlossaryHint>? Glossary = null);

public enum OutboundTone
{
    Short,
    Normal,
    Polite,
}

/// <summary>Outbound (send) translation request.</summary>
/// <param name="FixedTerms">Terms (action / job names) whose target text must be used verbatim.</param>
public sealed record OutboundRequest(
    string SourceText,
    string SourceLanguage,
    string TargetLanguage,
    OutboundTone Tone = OutboundTone.Normal,
    string CustomPrompt = "",
    IReadOnlyList<GlossaryHint>? FixedTerms = null,
    TranslationHints? Hints = null);

/// <summary>Per-call behaviour switches (from configuration).</summary>
public sealed record PipelineOptions(
    bool DiscardRefusals = true,
    double MaxLengthRatio = 4.0,
    int LengthSlack = 40,
    int MaxTokens = 1024,
    int OutboundMaxChars = 450);
