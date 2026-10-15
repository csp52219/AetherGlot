using System.Text.RegularExpressions;

namespace AetherGlot.Core.Glossary;

/// <summary>One auto-translate phrase (Completion entry or a row of a referenced sheet).</summary>
/// <param name="Group">Completion group (as in AutoTranslatePayload.Group).</param>
/// <param name="Key">Completion RowId for "@" groups, otherwise the referenced sheet's RowId.</param>
public sealed record AutoTranslateEntry(uint Group, uint Key, string CategoryJa, string CategoryEn, string Ja, string En)
{
    public string Id => $"{Group}:{Key}";
}

/// <summary>A category (Completion group) with its entry count.</summary>
public sealed record AutoTranslateCategory(uint Group, string Ja, string En, string LookupTable, int Count);

/// <summary>
/// Parsed Completion.LookupTable: "@" (text in Completion), "Sheet" (all rows) or
/// "Sheet[col-1,noun,1-20,25]" (column overrides, flags and row ranges).
/// </summary>
public sealed partial record LookupTable(bool InCompletion, string Sheet, IReadOnlyList<int> Columns, IReadOnlyList<(uint From, uint To)> Ranges)
{
    public static LookupTable? Parse(string? text)
    {
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (s == "@") return new LookupTable(true, string.Empty, [], []);

        var m = SheetRegex().Match(s);
        if (!m.Success) return null;

        var sheet   = m.Groups["sheet"].Value;
        var columns = new List<int>();
        var ranges  = new List<(uint, uint)>();
        if (m.Groups["args"].Success)
        {
            foreach (var raw in m.Groups["args"].Value.Split(','))
            {
                var part = raw.Trim();
                if (part.Length == 0 || part is "noun" or "tail") continue;
                if (part.StartsWith("col-", StringComparison.Ordinal) && int.TryParse(part[4..], out var col))
                {
                    columns.Add(col);
                    continue;
                }
                var dash = part.IndexOf('-');
                if (dash < 0 && uint.TryParse(part, out var single)) ranges.Add((single, single));
                else if (dash > 0 && uint.TryParse(part[..dash], out var a) && uint.TryParse(part[(dash + 1)..], out var b) && b >= a) ranges.Add((a, b));
            }
        }
        return new LookupTable(false, sheet, columns, ranges);
    }

    /// <summary>Whether <paramref name="rowId"/> is covered (no ranges = every row).</summary>
    public bool Contains(uint rowId) => Ranges.Count == 0 || Ranges.Any(r => rowId >= r.From && rowId <= r.To);

    [GeneratedRegex(@"^(?<sheet>[A-Za-z0-9_]+)(\[(?<args>[^\]]*)\])?$")]
    private static partial Regex SheetRegex();
}

/// <summary>All auto-translate phrases loaded from the game data, for the viewer and term matching.</summary>
public sealed class AutoTranslateCatalog
{
    private readonly Dictionary<string, AutoTranslateEntry> byId;
    private readonly Dictionary<string, DateTimeOffset> recentlySeen = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public IReadOnlyList<AutoTranslateEntry> Entries { get; }
    public IReadOnlyList<AutoTranslateCategory> Categories { get; }

    public AutoTranslateCatalog(IReadOnlyList<AutoTranslateEntry> entries, IReadOnlyList<AutoTranslateCategory> categories)
    {
        Entries    = entries;
        Categories = categories;
        byId       = new Dictionary<string, AutoTranslateEntry>(StringComparer.Ordinal);
        foreach (var e in entries) byId.TryAdd(e.Id, e);
    }

    public static AutoTranslateCatalog Empty { get; } = new([], []);

    public bool TryGet(uint group, uint key, out AutoTranslateEntry entry) => byId.TryGetValue($"{group}:{key}", out entry!);

    /// <summary>Marks a phrase as having appeared in chat (for the "recent only" filter).</summary>
    public void MarkSeen(uint group, uint key, DateTimeOffset when)
    {
        lock (gate) recentlySeen[$"{group}:{key}"] = when;
    }

    public IReadOnlyDictionary<string, DateTimeOffset> RecentlySeen
    {
        get { lock (gate) return new Dictionary<string, DateTimeOffset>(recentlySeen); }
    }

    /// <summary>
    /// Short phrases (words / names, not sentences) as glossary terms, so a typed "bio" can be
    /// matched to the auto-translate 【バイオ】.
    /// </summary>
    public IEnumerable<TermEntry> ToTermEntries()
    {
        foreach (var e in Entries)
        {
            var en = TermNormalizer.StripDecorations(e.En);
            var ja = TermNormalizer.StripDecorations(e.Ja);
            if (en.Length == 0 || ja.Length == 0) continue;
            if (en.Count(c => c == ' ') >= 4 || en.EndsWith('?')) continue; // sentences

            yield return new TermEntry
            {
                Id              = $"at:{e.Id}",
                Kind            = TermKind.AutoTranslate,
                Source          = TermSource.GameData,
                AutoTranslateId = e.Id,
                Senses          = [new TermSense { Id = e.Id, En = en, Ja = ja }],
            };
        }
    }
}
