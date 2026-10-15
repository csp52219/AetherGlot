namespace AetherGlot.Core.Translation;

/// <summary>Result of validating a parsed response; shown in the inspector.</summary>
public sealed record ValidationReport(
    bool SchemaOk,
    string? SchemaError,
    bool RefusalDetected,
    bool TooLong,
    bool PlaceholdersPreserved,
    IReadOnlyList<string> MissingPlaceholders,
    IReadOnlyList<string> UnknownPlaceholders,
    int UnresolvedCount)
{
    /// <summary>True when the response may be displayed.</summary>
    public bool IsAcceptable(bool discardRefusals) =>
        SchemaOk && PlaceholdersPreserved && UnknownPlaceholders.Count == 0 &&
        (!discardRefusals || (!RefusalDetected && !TooLong));

    public static ValidationReport SchemaFailure(string error) =>
        new(false, error, false, false, false, [], [], 0);
}

/// <summary>Checks a parsed inbound response against the request.</summary>
public static class ResponseValidator
{
    public static ValidationReport ValidateInbound(InboundResponse response, string placeholderizedSource,
        double maxLengthRatio = 4.0, int lengthSlack = 40)
    {
        if (response.Status == InboundResponse.StatusSkip)
            return new ValidationReport(true, null, false, false, true, [], [], 0);

        var translation = response.Translation ?? string.Empty;

        var inputIds  = Placeholders.Find(placeholderizedSource).Where(Placeholders.IsAutoTranslate).Distinct().ToList();
        var outputIds = Placeholders.Find(translation).ToHashSet(StringComparer.Ordinal);
        var missing   = inputIds.Where(id => !outputIds.Contains(id)).ToList();

        var unresolvedIds = response.Unresolved.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = outputIds
            .Where(id => Placeholders.IsAutoTranslate(id) ? !inputIds.Contains(id) : !unresolvedIds.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new ValidationReport(
            SchemaOk: true,
            SchemaError: null,
            RefusalDetected: RefusalDetector.IsRefusal(translation, placeholderizedSource),
            TooLong: RefusalDetector.IsSuspiciouslyLong(translation, placeholderizedSource, maxLengthRatio, lengthSlack),
            PlaceholdersPreserved: missing.Count == 0,
            MissingPlaceholders: missing,
            UnknownPlaceholders: unknown,
            UnresolvedCount: response.Unresolved.Count);
    }

    public static ValidationReport ValidateOutbound(OutboundResponse response, string source)
    {
        var translation = response.Translation ?? string.Empty;
        return new ValidationReport(
            SchemaOk: true,
            SchemaError: null,
            RefusalDetected: RefusalDetector.IsRefusal(translation, source),
            TooLong: false, // outbound length is limited by the prompt and the 500-char chat box
            PlaceholdersPreserved: true,
            MissingPlaceholders: [],
            UnknownPlaceholders: [],
            UnresolvedCount: 0);
    }
}
