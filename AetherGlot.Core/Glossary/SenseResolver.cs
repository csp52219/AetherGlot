namespace AetherGlot.Core.Glossary;

/// <summary>Context available when deciding which sense a word has.</summary>
public sealed record SenseContext(
    bool? InCombat,
    IReadOnlySet<string> NormalizedWords,
    string NormalizedMessage,
    IReadOnlySet<string> PartyJobs)
{
    public static SenseContext Create(string message, IEnumerable<string> tokens, bool? inCombat, IEnumerable<string>? partyJobs) => new(
        inCombat,
        tokens.Select(TermNormalizer.Normalize).Where(t => t.Length > 0).ToHashSet(StringComparer.Ordinal),
        TermNormalizer.Normalize(message),
        (partyJobs ?? []).Select(j => j.Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal));
}

/// <summary>A sense with the score it got from the context.</summary>
public sealed record ScoredSense(TermSense Sense, int Score);

/// <summary>Outcome of sense resolution for one hit.</summary>
public sealed record SenseResolution(TermEntry Entry, IReadOnlyList<ScoredSense> Candidates, TermSense? Chosen)
{
    public bool IsAmbiguous => Chosen is null;
}

/// <summary>
/// Picks a sense from local context. Scoring per sense:
/// combat state matches +1 / contradicts −2, each co-occurring word +2, a listed job in the
/// party +1. A sense is chosen when it scores strictly higher than the others and above 0;
/// otherwise all candidates are passed to the model.
/// </summary>
public static class SenseResolver
{
    public static SenseResolution Resolve(TermEntry entry, SenseContext context)
    {
        var scored = entry.Senses
            .Select(s => new ScoredSense(s, Score(s.Rules, context)))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Sense.Priority)
            .ToList();

        if (scored.Count == 1)
            return new SenseResolution(entry, scored, scored[0].Sense);

        if (scored.Count == 0)
            return new SenseResolution(entry, scored, null);

        var top = scored[0];
        var decided = top.Score > 0 && top.Score > scored[1].Score;
        return new SenseResolution(entry, scored, decided ? top.Sense : null);
    }

    public static int Score(SenseRules rules, SenseContext context)
    {
        var score = 0;

        if (rules.InCombat is { } wantCombat && context.InCombat is { } inCombat)
            score += wantCombat == inCombat ? 1 : -2;

        foreach (var w in rules.Words)
        {
            var n = TermNormalizer.Normalize(w);
            if (n.Length == 0) continue;
            var multiWord = w.Trim().Contains(' ');
            if (multiWord ? context.NormalizedMessage.Contains(n, StringComparison.Ordinal) : context.NormalizedWords.Contains(n))
                score += 2;
        }

        if (rules.PartyJobs.Any(j => context.PartyJobs.Contains(j.Trim().ToUpperInvariant())))
            score += 1;

        return score;
    }
}
