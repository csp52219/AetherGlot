namespace AetherGlot.Core.Glossary;

/// <summary>Where a term comes from. Lower value = higher priority.</summary>
public enum TermSource
{
    /// <summary>The user's own dictionary (always wins).</summary>
    User = 0,

    /// <summary>Loaded from the game client data (Completion, Action, Status, ...).</summary>
    GameData = 1,

    /// <summary>Downloaded community dictionary (slang, mechanics, abbreviations).</summary>
    Community = 2,
}

public enum TermKind
{
    Other,
    AutoTranslate,
    Action,
    Status,
    Job,
    Content,
    Place,
    Mechanic,
    Slang,
    Abbreviation,
}

/// <summary>What to show when the sense of a word cannot be decided.</summary>
public enum AmbiguityBehavior
{
    /// <summary>Show every sense as chips (default).</summary>
    ShowAll,

    /// <summary>Use the sense with the best priority.</summary>
    PreferFirst,
}

/// <summary>Context conditions that make a sense more likely.</summary>
public sealed class SenseRules
{
    /// <summary>true = in combat, false = out of combat, null = either.</summary>
    public bool? InCombat { get; set; }

    /// <summary>Words / phrases that tend to appear with this sense (e.g. "brb", "dot", "fell off").</summary>
    public List<string> Words { get; set; } = [];

    /// <summary>Job abbreviations (e.g. "SCH") whose presence in the party supports this sense.</summary>
    public List<string> PartyJobs { get; set; } = [];

    public int RuleCount =>
        (InCombat is null ? 0 : 1) + (Words.Count > 0 ? 1 : 0) + (PartyJobs.Count > 0 ? 1 : 0);
}

/// <summary>One meaning of a term.</summary>
public sealed class TermSense
{
    public string Id { get; set; } = string.Empty;
    public string Ja { get; set; } = string.Empty;
    public string En { get; set; } = string.Empty;

    /// <summary>Lower = preferred when nothing else decides.</summary>
    public int Priority { get; set; }

    public SenseRules Rules { get; set; } = new();
    public string? Note { get; set; }

    public string TextFor(string languageCode) => languageCode == "ja" ? Ja : En;
}

/// <summary>A glossary entry: canonical texts, aliases and one or more senses.</summary>
public sealed class TermEntry
{
    public string Id { get; set; } = string.Empty;
    public TermKind Kind { get; set; }
    public TermSource Source { get; set; }

    /// <summary>Extra English spellings / abbreviations (e.g. "opo", "opo-opo").</summary>
    public List<string> AliasesEn { get; set; } = [];

    /// <summary>Extra Japanese spellings (e.g. "魔猿").</summary>
    public List<string> AliasesJa { get; set; } = [];

    public List<TermSense> Senses { get; set; } = [];
    public AmbiguityBehavior OnAmbiguous { get; set; } = AmbiguityBehavior.ShowAll;

    /// <summary>For auto-translate entries: "group:key" as used in the payload.</summary>
    public string? AutoTranslateId { get; set; }

    public bool IsPolysemous => Senses.Count > 1;

    /// <summary>Canonical text of the first sense.</summary>
    public string Ja => Senses.Count > 0 ? Senses[0].Ja : string.Empty;
    public string En => Senses.Count > 0 ? Senses[0].En : string.Empty;

    /// <summary>Spellings to match for text written in <paramref name="languageCode"/>.</summary>
    public IEnumerable<string> SurfaceForms(string languageCode)
    {
        if (languageCode == "ja")
        {
            foreach (var s in Senses) if (!string.IsNullOrWhiteSpace(s.Ja)) yield return s.Ja;
            foreach (var a in AliasesJa) yield return a;
        }
        else
        {
            foreach (var s in Senses) if (!string.IsNullOrWhiteSpace(s.En)) yield return s.En;
            foreach (var a in AliasesEn) yield return a;
        }
    }

    /// <summary>Simple one-sense entry.</summary>
    public static TermEntry Simple(string id, TermKind kind, TermSource source, string en, string ja, params string[] aliasesEn) => new()
    {
        Id = id, Kind = kind, Source = source, AliasesEn = aliasesEn.ToList(),
        Senses = [new TermSense { Id = id, En = en, Ja = ja }],
    };
}
