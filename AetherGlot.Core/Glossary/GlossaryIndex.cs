using System.Text.RegularExpressions;

namespace AetherGlot.Core.Glossary;

/// <summary>A term found in a message.</summary>
public sealed record GlossaryHit(string Surface, int Start, int Length, TermEntry Entry);

/// <summary>
/// Immutable lookup structure over all glossary sources, for text written in one language.
/// <para>Precedence for the same spelling: the user dictionary always wins. Between game data
/// and the community dictionary, game data wins unless the community entry has several senses
/// (it is then a disambiguation entry that already includes the game meaning, e.g. "bio").</para>
/// </summary>
public sealed partial class GlossaryIndex
{
    /// <summary>Common English words never matched from game data on their own (too noisy).</summary>
    private static readonly HashSet<string> GameDataStopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "you", "your", "are", "was", "but", "not", "all", "can", "will", "one", "two", "out",
        "off", "now", "new", "get", "got", "let", "lets", "yes", "yeah", "ok", "okay", "go", "come", "back", "up",
        "down", "left", "right", "here", "there", "this", "that", "with", "from", "have", "has", "had", "just",
        "time", "good", "nice", "well", "first", "last", "next", "more", "less", "full", "fast", "slow", "high",
        "low", "big", "small", "free", "open", "close", "start", "end", "stop", "wait", "ready", "help", "thanks",
        "sorry", "please", "attack", "action", "enemy", "leader", "master", "link", "active", "passive", "rest",
        "return", "jump", "run", "walk", "sit", "stand", "look", "wake", "sleep", "heavy", "bind", "drain",
        "rise", "rush", "shot", "fire", "water", "wind", "earth", "ice", "light", "dark", "storm", "blood",
        "level", "lot", "loot", "pet", "dps",
    };

    private readonly Dictionary<string, TermEntry> map;
    private readonly string languageCode;

    public int MaxWords { get; }
    public int MaxLength { get; }
    public int Count => map.Count;

    private GlossaryIndex(Dictionary<string, TermEntry> map, string languageCode, int maxWords, int maxLength)
    {
        this.map          = map;
        this.languageCode = languageCode;
        MaxWords          = maxWords;
        MaxLength         = maxLength;
    }

    /// <summary>Builds the index of spellings written in <paramref name="sourceLanguage"/> ("en" or "ja").</summary>
    public static GlossaryIndex Build(IEnumerable<TermEntry> entries, string sourceLanguage)
    {
        var japanese = sourceLanguage == "ja";
        var map      = new Dictionary<string, TermEntry>(StringComparer.Ordinal);
        var maxWords = 1;
        var maxLen   = 1;

        foreach (var entry in entries)
        {
            foreach (var surface in entry.SurfaceForms(sourceLanguage).Distinct())
            {
                var cleaned = TermNormalizer.StripDecorations(surface);
                var key     = TermNormalizer.Normalize(cleaned, collapseRepeat: !japanese);
                if (!Accept(key, cleaned, entry, japanese)) continue;

                if (map.TryGetValue(key, out var existing) && !Wins(entry, existing))
                    continue;

                map[key] = entry;
                maxWords = Math.Max(maxWords, WordRegex().Matches(cleaned).Count);
                maxLen   = Math.Max(maxLen, key.Length);
            }
        }

        return new GlossaryIndex(map, sourceLanguage, Math.Min(maxWords, 6), Math.Min(maxLen, 40));
    }

    public bool TryGet(string normalizedKey, out TermEntry entry) => map.TryGetValue(normalizedKey, out entry!);

    /// <summary>Finds glossary terms in <paramref name="text"/>, longest match first, non-overlapping.</summary>
    public IReadOnlyList<GlossaryHit> FindHits(string text, int maxHits = 16)
    {
        if (string.IsNullOrWhiteSpace(text) || map.Count == 0) return [];

        // Placeholders ({{at0}}, {{u1}}) are not words; blank them but keep positions
        var masked = AetherGlot.Core.Translation.Placeholders.TokenRegex().Replace(text, m => new string(' ', m.Length));

        var hits = languageCode == "ja" ? FindJapanese(masked) : FindLatin(masked);
        return hits.Take(maxHits).ToList();
    }

    private List<GlossaryHit> FindLatin(string text)
    {
        var tokens = WordRegex().Matches(text).Cast<Match>().ToList();
        var hits   = new List<GlossaryHit>();
        var i      = 0;
        while (i < tokens.Count)
        {
            var matched = false;
            for (var n = Math.Min(MaxWords, tokens.Count - i); n >= 1 && !matched; n--)
            {
                var first = tokens[i];
                var last  = tokens[i + n - 1];
                var span  = text.Substring(first.Index, last.Index + last.Length - first.Index);
                var key   = TermNormalizer.Normalize(span);

                if (!map.TryGetValue(key, out var entry) && n == 1 && key.Length > 3 && key.EndsWith('s'))
                    map.TryGetValue(key[..^1], out entry); // simple plural

                if (entry is not null)
                {
                    hits.Add(new GlossaryHit(span, first.Index, span.Length, entry));
                    i += n;
                    matched = true;
                }
            }
            if (!matched) i++;
        }
        return hits;
    }

    private List<GlossaryHit> FindJapanese(string text)
    {
        var (norm, posMap) = TermNormalizer.NormalizeWithMap(text);
        var hits = new List<GlossaryHit>();
        var i = 0;
        while (i < norm.Length)
        {
            var matched = false;
            for (var len = Math.Min(MaxLength, norm.Length - i); len >= 2 && !matched; len--)
            {
                if (!map.TryGetValue(norm.Substring(i, len), out var entry)) continue;

                var start = posMap[i];
                var end   = posMap[i + len - 1] + 1;
                hits.Add(new GlossaryHit(text[start..end], start, end - start, entry));
                i += len;
                matched = true;
            }
            if (!matched) i++;
        }
        return hits;
    }

    /// <summary>True when <paramref name="candidate"/> should replace <paramref name="existing"/>.</summary>
    public static bool Wins(TermEntry candidate, TermEntry existing)
    {
        if (candidate.Source == existing.Source) return false; // first one wins within a source
        if (candidate.Source == TermSource.User) return true;
        if (existing.Source == TermSource.User) return false;

        // GameData vs Community
        var community = candidate.Source == TermSource.Community ? candidate : existing;
        var communityWins = community.IsPolysemous;
        return candidate == community ? communityWins : !communityWins;
    }

    private static bool Accept(string key, string cleaned, TermEntry entry, bool japanese)
    {
        if (key.Length < 2) return false;
        if (entry.Source != TermSource.GameData) return true;

        if (japanese) return key.Length >= 2;

        // Game data: skip very short words except job abbreviations, and common English words
        if (key.Length < 3 && entry.Kind != TermKind.Job) return false;
        if (!cleaned.Contains(' ') && GameDataStopWords.Contains(key)) return false;
        return true;
    }

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    /// <summary>Tokens of a message, for sense context.</summary>
    public static IEnumerable<string> Tokenize(string text) =>
        WordRegex().Matches(text).Select(m => m.Value);
}
