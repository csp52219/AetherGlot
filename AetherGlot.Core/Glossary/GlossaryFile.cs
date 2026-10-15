using System.Text.Json;
using System.Text.Json.Serialization;

namespace AetherGlot.Core.Glossary;

/// <summary>
/// JSON format shared by the community dictionary and the user's dictionary (schema 1):
/// <code>
/// { "schema": 1, "name": "...", "version": "2026.10.15", "updated": "2026-10-15",
///   "entries": [
///     { "id": "opo", "kind": "status", "en": "Opo-opo Form", "ja": "壱の型：魔猿",
///       "aliases": ["opo", "opo-opo"], "aliasesJa": ["魔猿"] },
///     { "id": "bio", "kind": "slang", "aliases": ["bio"], "onAmbiguous": "showAll",
///       "senses": [
///         { "id": "action", "ja": "バイオ（技）", "en": "Bio", "priority": 1,
///           "when": { "inCombat": true, "words": ["dot", "reapply"], "partyJobs": ["SCH"] } },
///         { "id": "afk", "ja": "トイレ（離席）", "en": "bathroom break", "priority": 2,
///           "when": { "inCombat": false, "words": ["brb", "afk"] } } ] } ] }
/// </code>
/// An entry without "senses" has one sense made of its "en"/"ja".
/// </summary>
public static class GlossaryFile
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
        WriteIndented               = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Encoder                     = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters                  = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public sealed class FileDto
    {
        public int Schema { get; set; } = SchemaVersion;
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Updated { get; set; }
        public List<EntryDto> Entries { get; set; } = [];
    }

    public sealed class EntryDto
    {
        public string? Id { get; set; }
        public TermKind Kind { get; set; } = TermKind.Other;
        public string? En { get; set; }
        public string? Ja { get; set; }
        public List<string>? Aliases { get; set; }
        public List<string>? AliasesJa { get; set; }
        public List<SenseDto>? Senses { get; set; }
        public AmbiguityBehavior? OnAmbiguous { get; set; }
        public string? Note { get; set; }
    }

    public sealed class SenseDto
    {
        public string? Id { get; set; }
        public string? Ja { get; set; }
        public string? En { get; set; }
        public int Priority { get; set; }
        public WhenDto? When { get; set; }
        public string? Note { get; set; }
    }

    public sealed class WhenDto
    {
        public bool? InCombat { get; set; }
        public List<string>? Words { get; set; }
        public List<string>? PartyJobs { get; set; }
    }

    /// <summary>Parse result. Invalid entries are skipped and reported in <see cref="Errors"/>.</summary>
    public sealed record ParseResult(IReadOnlyList<TermEntry> Entries, string? Name, string? Version, string? Updated, IReadOnlyList<string> Errors)
    {
        public bool Ok => Errors.Count == 0;
    }

    public static ParseResult Parse(string json, TermSource source)
    {
        FileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(json, Options);
        }
        catch (JsonException ex)
        {
            return new ParseResult([], null, null, null, [$"Invalid JSON: {ex.Message}"]);
        }
        if (dto is null)
            return new ParseResult([], null, null, null, ["Empty document."]);
        if (dto.Schema > SchemaVersion)
            return new ParseResult([], dto.Name, dto.Version, dto.Updated, [$"Unsupported schema {dto.Schema} (this version reads {SchemaVersion})."]);

        var errors  = new List<string>();
        var entries = new List<TermEntry>();
        var ids     = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < (dto.Entries?.Count ?? 0); i++)
        {
            var e = dto.Entries![i];
            if (e is null) { errors.Add($"entries[{i}]: null."); continue; }
            var entry = ToEntry(e, source, i, errors);
            if (entry is null) continue;
            if (!ids.Add(entry.Id)) { errors.Add($"entries[{i}]: duplicate id '{entry.Id}'."); continue; }
            entries.Add(entry);
        }
        return new ParseResult(entries, dto.Name, dto.Version, dto.Updated, errors);
    }

    public static string Serialize(IEnumerable<TermEntry> entries, string? name = null, string? version = null)
    {
        var dto = new FileDto
        {
            Name    = name,
            Version = version,
            Updated = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Entries = entries.Select(ToDto).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    private static TermEntry? ToEntry(EntryDto e, TermSource source, int index, List<string> errors)
    {
        var senses = new List<TermSense>();
        if (e.Senses is { Count: > 0 })
        {
            for (var s = 0; s < e.Senses.Count; s++)
            {
                var sd = e.Senses[s];
                if (sd is null || (string.IsNullOrWhiteSpace(sd.Ja) && string.IsNullOrWhiteSpace(sd.En)))
                {
                    errors.Add($"entries[{index}].senses[{s}]: needs 'ja' or 'en'.");
                    return null;
                }
                senses.Add(new TermSense
                {
                    Id       = string.IsNullOrWhiteSpace(sd.Id) ? $"s{s}" : sd.Id!.Trim(),
                    Ja       = sd.Ja?.Trim() ?? string.Empty,
                    En       = sd.En?.Trim() ?? string.Empty,
                    Priority = sd.Priority == 0 ? s + 1 : sd.Priority,
                    Note     = sd.Note,
                    Rules    = new SenseRules
                    {
                        InCombat  = sd.When?.InCombat,
                        Words     = Clean(sd.When?.Words),
                        PartyJobs = Clean(sd.When?.PartyJobs),
                    },
                });
            }
        }
        else if (!string.IsNullOrWhiteSpace(e.Ja) || !string.IsNullOrWhiteSpace(e.En))
        {
            senses.Add(new TermSense { Id = "s0", Ja = e.Ja?.Trim() ?? string.Empty, En = e.En?.Trim() ?? string.Empty, Priority = 1, Note = e.Note });
        }
        else
        {
            errors.Add($"entries[{index}]: needs 'ja'/'en' or 'senses'.");
            return null;
        }

        var aliases   = Clean(e.Aliases);
        var aliasesJa = Clean(e.AliasesJa);
        // A multi-sense entry's own en/ja act as spellings to match
        if (e.Senses is { Count: > 0 })
        {
            if (!string.IsNullOrWhiteSpace(e.En)) aliases.Insert(0, e.En!.Trim());
            if (!string.IsNullOrWhiteSpace(e.Ja)) aliasesJa.Insert(0, e.Ja!.Trim());
        }

        var id = string.IsNullOrWhiteSpace(e.Id)
            ? aliases.FirstOrDefault() ?? (senses[0].En.Length > 0 ? senses[0].En : senses[0].Ja)
            : e.Id!.Trim();

        return new TermEntry
        {
            Id          = id,
            Kind        = e.Kind,
            Source      = source,
            AliasesEn   = aliases,
            AliasesJa   = aliasesJa,
            Senses      = senses,
            OnAmbiguous = e.OnAmbiguous ?? AmbiguityBehavior.ShowAll,
        };
    }

    private static EntryDto ToDto(TermEntry t)
    {
        if (t.Senses.Count == 1 && t.Senses[0].Rules.RuleCount == 0)
        {
            return new EntryDto
            {
                Id = t.Id, Kind = t.Kind, En = NullIfEmpty(t.En), Ja = NullIfEmpty(t.Ja),
                Aliases = NullIfEmpty(t.AliasesEn), AliasesJa = NullIfEmpty(t.AliasesJa), Note = t.Senses[0].Note,
            };
        }
        return new EntryDto
        {
            Id = t.Id, Kind = t.Kind, Aliases = NullIfEmpty(t.AliasesEn), AliasesJa = NullIfEmpty(t.AliasesJa),
            OnAmbiguous = t.OnAmbiguous,
            Senses = t.Senses.Select(s => new SenseDto
            {
                Id = s.Id, Ja = NullIfEmpty(s.Ja), En = NullIfEmpty(s.En), Priority = s.Priority, Note = s.Note,
                When = s.Rules.RuleCount == 0 ? null : new WhenDto
                {
                    InCombat = s.Rules.InCombat, Words = NullIfEmpty(s.Rules.Words), PartyJobs = NullIfEmpty(s.Rules.PartyJobs),
                },
            }).ToList(),
        };
    }

    private static List<string> Clean(List<string>? list) =>
        (list ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.Ordinal).ToList();

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static List<string>? NullIfEmpty(List<string> l) => l.Count == 0 ? null : l;
}
