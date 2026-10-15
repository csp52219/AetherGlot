namespace AetherGlot.Core.Translation;

/// <summary>What to show when part of a message could not be translated.</summary>
public enum UnresolvedDisplayMode
{
    /// <summary>Show the partial translation; unresolved spans in the original, marked (default).</summary>
    PartialMarked,

    /// <summary>Show only the original message.</summary>
    OriginalOnly,

    /// <summary>Show nothing.</summary>
    Hide,
}

/// <summary>Persisted translation behaviour (part of Configuration.json).</summary>
[Serializable]
public sealed class TranslationConfig
{
    public UnresolvedDisplayMode UnresolvedMode { get; set; } = UnresolvedDisplayMode.PartialMarked;

    /// <summary>Discard refusal / apology "translations" and show the original instead.</summary>
    public bool DiscardRefusals { get; set; } = true;

    /// <summary>Send combat state, location, party jobs and recent lines with each message.</summary>
    public bool SendContextHints { get; set; } = true;

    /// <summary>Number of recent lines of the same channel sent as context.</summary>
    public int RecentLineCount { get; set; } = 3;

    /// <summary>Max entries in the translation cache (0 disables caching).</summary>
    public int CacheSize { get; set; } = 500;

    public PipelineOptions ToPipelineOptions() => new(DiscardRefusals: DiscardRefusals);
}
