namespace AetherGlot.Core.Glossary;

/// <summary>Groups shown in the dictionary window's left pane.</summary>
public enum GlossaryBucket
{
    All,

    /// <summary>Auto-translate phrases from the game data.</summary>
    AutoTranslate,

    /// <summary>Other game data (actions, statuses, jobs, duties, places).</summary>
    GameNames,
    Community,
    Mine,
}

/// <summary>Search and helpers for the dictionary window.</summary>
public static class GlossaryBrowser
{
    public static GlossaryBucket BucketOf(TermEntry e) => e.Source switch
    {
        TermSource.User                                   => GlossaryBucket.Mine,
        TermSource.Community                              => GlossaryBucket.Community,
        _ when e.Kind == TermKind.AutoTranslate           => GlossaryBucket.AutoTranslate,
        _                                                 => GlossaryBucket.GameNames,
    };

    public static IReadOnlyDictionary<GlossaryBucket, int> Counts(IEnumerable<TermEntry> entries)
    {
        var counts = Enum.GetValues<GlossaryBucket>().ToDictionary(b => b, _ => 0);
        foreach (var e in entries)
        {
            counts[GlossaryBucket.All]++;
            counts[BucketOf(e)]++;
        }
        return counts;
    }

    /// <summary>
    /// Entries in <paramref name="bucket"/> whose spellings or meanings contain
    /// <paramref name="query"/> (normalised: case, width, hyphens and spaces ignored).
    /// Exact matches come first.
    /// </summary>
    public static IReadOnlyList<TermEntry> Search(IEnumerable<TermEntry> entries, string? query, GlossaryBucket bucket, TermKind? kind = null)
    {
        var q = TermNormalizer.Normalize(query, collapseRepeat: false);
        var result = new List<(TermEntry Entry, int Rank)>();
        foreach (var e in entries)
        {
            if (bucket != GlossaryBucket.All && BucketOf(e) != bucket) continue;
            if (kind is { } k && e.Kind != k) continue;
            if (q.Length == 0)
            {
                result.Add((e, 1));
                continue;
            }

            var best = int.MaxValue;
            foreach (var text in Texts(e))
            {
                var n = TermNormalizer.Normalize(text, collapseRepeat: false);
                if (n == q) { best = 0; break; }
                if (n.StartsWith(q, StringComparison.Ordinal)) best = Math.Min(best, 1);
                else if (n.Contains(q, StringComparison.Ordinal)) best = Math.Min(best, 2);
            }
            if (best != int.MaxValue) result.Add((e, best));
        }
        return result.OrderBy(r => r.Rank).Select(r => r.Entry).ToList();
    }

    /// <summary>Total number of context rules over all senses (shown as "2 rules").</summary>
    public static int RuleCount(TermEntry e) => e.Senses.Sum(s => s.Rules.RuleCount);

    /// <summary>"dot, reapply, fell off" ⇄ list.</summary>
    public static List<string> ParseList(string? text) =>
        (text ?? string.Empty).Split([',', '、', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .Distinct(StringComparer.Ordinal).ToList();

    public static string FormatList(IEnumerable<string> items) => string.Join(", ", items);

    /// <summary>Deep copy as a user entry (for "copy to My dictionary and edit").</summary>
    public static TermEntry CopyAsUser(TermEntry e) => new()
    {
        Id          = e.Source == TermSource.User ? e.Id : "user:" + e.Id,
        Kind        = e.Kind,
        Source      = TermSource.User,
        AliasesEn   = e.AliasesEn.ToList(),
        AliasesJa   = e.AliasesJa.ToList(),
        OnAmbiguous = e.OnAmbiguous,
        Senses      = e.Senses.Select(s => new TermSense
        {
            Id = s.Id, Ja = s.Ja, En = s.En, Priority = s.Priority, Note = s.Note,
            Rules = new SenseRules { InCombat = s.Rules.InCombat, Words = s.Rules.Words.ToList(), PartyJobs = s.Rules.PartyJobs.ToList() },
        }).ToList(),
    };

    /// <summary>
    /// Cleans an edited My dictionary entry before saving: trims text, drops empty senses and
    /// aliases, gives new entries an id and every sense a unique id.
    /// Returns an error message (English, for the log / fallback text) or null when it can be saved.
    /// </summary>
    public static string? PrepareUserEntry(TermEntry e)
    {
        e.Source = TermSource.User;
        e.AliasesEn = CleanList(e.AliasesEn);
        e.AliasesJa = CleanList(e.AliasesJa);
        foreach (var s in e.Senses)
        {
            s.En = s.En.Trim();
            s.Ja = s.Ja.Trim();
            s.Note = string.IsNullOrWhiteSpace(s.Note) ? null : s.Note.Trim();
            s.Rules.Words = CleanList(s.Rules.Words);
            s.Rules.PartyJobs = CleanList(s.Rules.PartyJobs).Select(j => j.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();
        }
        e.Senses = e.Senses.Where(s => s.En.Length > 0 || s.Ja.Length > 0).ToList();
        if (e.Senses.Count == 0) return "Enter at least one meaning (English or Japanese).";
        if (e.Senses.Any(s => s.En.Length == 0 || s.Ja.Length == 0)) return "Every meaning needs both an English and a Japanese text.";

        if (string.IsNullOrWhiteSpace(e.Id) || e.Id == "user:")
        {
            var basis = TermNormalizer.Normalize(e.AliasesEn.FirstOrDefault() ?? e.Senses[0].En, collapseRepeat: false);
            if (basis.Length == 0) basis = TermNormalizer.Normalize(e.Senses[0].Ja, collapseRepeat: false);
            e.Id = "user:" + basis;
        }
        for (var i = 0; i < e.Senses.Count; i++)
            e.Senses[i].Id = e.Senses.Count == 1 ? e.Id : $"{e.Id}#{i + 1}";
        return null;
    }

    private static List<string> CleanList(IEnumerable<string> items) =>
        items.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    private static IEnumerable<string> Texts(TermEntry e)
    {
        foreach (var s in e.Senses) { yield return s.En; yield return s.Ja; }
        foreach (var a in e.AliasesEn) yield return a;
        foreach (var a in e.AliasesJa) yield return a;
    }
}
