using AetherGlot.Core.Connection;

namespace AetherGlot.Core.Translation;

/// <summary>One call to the model, kept for the inspector. Contains no secrets.</summary>
public sealed record AttemptRecord(
    string RawContent,
    string Model,
    int? PromptTokens,
    int? CompletionTokens,
    TimeSpan Elapsed,
    bool JsonModeUsed,
    string? Error);

public enum InboundOutcomeKind
{
    /// <summary>Fully translated.</summary>
    Translated,

    /// <summary>Translated with unresolved spans left in the original language.</summary>
    Partial,

    /// <summary>The message is already in the client language.</summary>
    Skipped,

    /// <summary>The response was rejected (or the call failed); show the original text.</summary>
    Fallback,
}

/// <summary>Everything known about one inbound translation (display + inspector data).</summary>
public sealed class InboundOutcome
{
    public required InboundRequest Request { get; init; }
    public required InboundOutcomeKind Kind { get; init; }
    public required IReadOnlyList<DisplaySegment> Segments { get; init; }
    public InboundResponse? Response { get; init; }
    public ValidationReport? Validation { get; init; }

    /// <summary>Why the outcome is a fallback (already free of secrets).</summary>
    public string? FailureReason { get; init; }
    public HttpFailureKind? HttpFailure { get; init; }
    public IReadOnlyList<AttemptRecord> Attempts { get; init; } = [];
    public bool FromCache { get; init; }
    public string SystemPrompt { get; init; } = string.Empty;
    public string UserPrompt { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;

    public string DisplayText => Rehydrator.ToPlainText(Segments);
    public string DetectedLanguage => Response?.Lang ?? string.Empty;
    public TimeSpan TotalElapsed => Attempts.Aggregate(TimeSpan.Zero, (t, a) => t + a.Elapsed);

    public InboundOutcome AsCached() => new()
    {
        Request = Request, Kind = Kind, Segments = Segments, Response = Response, Validation = Validation,
        FailureReason = FailureReason, HttpFailure = HttpFailure, Attempts = Attempts, FromCache = true,
        SystemPrompt = SystemPrompt, UserPrompt = UserPrompt, Model = Model,
    };
}

/// <summary>Result of an outbound translation.</summary>
public sealed class OutboundOutcome
{
    public required OutboundRequest Request { get; init; }
    public required bool Success { get; init; }
    public OutboundResponse? Response { get; init; }
    public ValidationReport? Validation { get; init; }
    public string? FailureReason { get; init; }
    public HttpFailureKind? HttpFailure { get; init; }
    public IReadOnlyList<AttemptRecord> Attempts { get; init; } = [];
    public string SystemPrompt { get; init; } = string.Empty;
    public string UserPrompt { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;

    public string Translation => Response?.Translation ?? string.Empty;
}
