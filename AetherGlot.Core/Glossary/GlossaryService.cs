using AetherGlot.Core.Translation;

namespace AetherGlot.Core.Glossary;

/// <summary>A hit plus its sense resolution.</summary>
public sealed record GlossaryMatch(GlossaryHit Hit, SenseResolution Resolution);

/// <summary>Counts and versions for the footer / dictionary window.</summary>
public sealed record GlossaryStats(
    int UserCount,
    int GameDataCount,
    int CommunityCount,
    string? CommunityVersion,
    DateTimeOffset? CommunityUpdated,
    DateTimeOffset? GameDataLoaded)
{
    public int Total => UserCount + GameDataCount + CommunityCount;
    public static GlossaryStats Empty { get; } = new(0, 0, 0, null, null, null);
}

/// <summary>Terms found in one message.</summary>
public sealed record GlossaryLookup(IReadOnlyList<GlossaryMatch> Matches, string SourceLanguage, string TargetLanguage)
{
    public static GlossaryLookup Empty(string source = "", string target = "") => new([], source, target);

    /// <summary>
    /// Pairs to inject into the prompt: a decided sense gives one target, an undecided one
    /// gives every candidate (the model picks).
    /// </summary>
    public IReadOnlyList<GlossaryHint> ToHints()
    {
        var hints = new List<GlossaryHint>();
        var seen  = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in Matches)
        {
            var senses = m.Resolution.Chosen is { } chosen
                ? [chosen]
                : m.Resolution.Candidates.Select(c => c.Sense).ToList();
            var targets = senses.Select(s => s.TextFor(TargetLanguage))
                                .Where(t => !string.IsNullOrWhiteSpace(t))
                                .Distinct(StringComparer.Ordinal)
                                .ToList();
            if (targets.Count == 0) continue;
            if (!seen.Add(TermNormalizer.Normalize(m.Hit.Surface) + "\u001f" + string.Join("|", targets))) continue;

            var note = m.Resolution.Entry.Kind is TermKind.Other ? null : m.Resolution.Entry.Kind.ToString().ToLowerInvariant();
            hints.Add(new GlossaryHint(m.Hit.Surface, targets, note));
        }
        return hints;
    }
}

/// <summary>
/// Holds the merged glossary (user + game data + community) and looks up terms in messages.
/// Rebuilt atomically in the background; lookups are lock-free.
/// </summary>
public sealed class GlossaryService
{
    private sealed record Snapshot(
        GlossaryIndex English,
        GlossaryIndex Japanese,
        IReadOnlyList<TermEntry> All,
        GlossaryStats Stats);

    private volatile Snapshot snapshot = new(
        GlossaryIndex.Build([], "en"), GlossaryIndex.Build([], "ja"), [], GlossaryStats.Empty);

    public GlossaryStats Stats => snapshot.Stats;
    public IReadOnlyList<TermEntry> AllEntries => snapshot.All;

    /// <summary>Raised after every rebuild (on the rebuilding thread).</summary>
    public event Action? Rebuilt;

    public void Rebuild(IEnumerable<TermEntry> user, IEnumerable<TermEntry> gameData, IEnumerable<TermEntry> community, GlossaryStats stats)
    {
        // Order matters only within a source (first wins); between sources GlossaryIndex.Wins decides
        var all = user.Concat(gameData).Concat(community).ToList();
        snapshot = new Snapshot(GlossaryIndex.Build(all, "en"), GlossaryIndex.Build(all, "ja"), all, stats);
        Rebuilt?.Invoke();
    }

    /// <summary>
    /// Finds terms in <paramref name="text"/> (written in <paramref name="sourceLanguage"/>)
    /// and resolves their senses. Returns an empty lookup for unsupported language pairs.
    /// </summary>
    public GlossaryLookup Lookup(string text, string sourceLanguage, string targetLanguage,
                                 bool? inCombat = null, IEnumerable<string>? partyJobs = null, int maxHits = 16)
    {
        if (!Supported(sourceLanguage, targetLanguage) || string.IsNullOrWhiteSpace(text))
            return GlossaryLookup.Empty(sourceLanguage, targetLanguage);

        var snap  = snapshot;
        var index = sourceLanguage == "ja" ? snap.Japanese : snap.English;
        var hits  = index.FindHits(text, maxHits);
        if (hits.Count == 0) return GlossaryLookup.Empty(sourceLanguage, targetLanguage);

        var context = SenseContext.Create(text, GlossaryIndex.Tokenize(text), inCombat, partyJobs);
        var matches = hits.Select(h => new GlossaryMatch(h, SenseResolver.Resolve(h.Entry, context))).ToList();
        return new GlossaryLookup(matches, sourceLanguage, targetLanguage);
    }

    /// <summary>
    /// Official in-game names (actions, jobs, statuses, duties, places) found in an outbound
    /// message; the model must use their target text verbatim.
    /// </summary>
    public IReadOnlyList<GlossaryHint> FixedTerms(string text, string sourceLanguage, string targetLanguage)
    {
        var lookup = Lookup(text, sourceLanguage, targetLanguage, maxHits: 24);
        var fixedMatches = lookup.Matches
            .Where(m => m.Resolution.Entry.Source == TermSource.GameData &&
                        m.Resolution.Entry.Kind is TermKind.Action or TermKind.Job or TermKind.Status or TermKind.Content or TermKind.Place)
            .ToList();
        return (lookup with { Matches = fixedMatches }).ToHints();
    }

    public static bool Supported(string source, string target) =>
        (source, target) is ("en", "ja") or ("ja", "en");
}
