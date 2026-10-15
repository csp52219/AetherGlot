using System.Text.Json;

namespace AetherGlot.Core.Glossary;

/// <summary>A word the model could not translate; offered as "add to dictionary".</summary>
public sealed class UnresolvedCandidate
{
    public string Text { get; set; } = string.Empty;
    public string? Guess { get; set; }
    public int Count { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }

    /// <summary>One message it appeared in, for context in the dictionary window.</summary>
    public string? Example { get; set; }
}

/// <summary>Persisted list of unresolved words ("pending approval"), most recent first, capped.</summary>
public sealed class UnresolvedCandidateStore
{
    private readonly string path;
    private readonly int capacity;
    private readonly object gate = new();
    private readonly Dictionary<string, UnresolvedCandidate> items = new(StringComparer.Ordinal);

    public UnresolvedCandidateStore(string path, int capacity = 200)
    {
        this.path     = path;
        this.capacity = capacity;
    }

    public IReadOnlyList<UnresolvedCandidate> Items
    {
        get { lock (gate) return items.Values.OrderByDescending(i => i.LastSeen).ToList(); }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(path)) return;
            var list = JsonSerializer.Deserialize<List<UnresolvedCandidate>>(File.ReadAllText(path)) ?? [];
            lock (gate)
            {
                items.Clear();
                foreach (var c in list.Where(c => !string.IsNullOrWhiteSpace(c.Text)))
                    items[TermNormalizer.Normalize(c.Text)] = c;
            }
        }
        catch
        {
            // A corrupt pending list is not worth failing startup for
        }
    }

    /// <summary>Records occurrences; returns true when something changed.</summary>
    public bool Record(IEnumerable<(string Text, string? Guess)> spans, string? example, DateTimeOffset now)
    {
        var changed = false;
        lock (gate)
        {
            foreach (var (text, guess) in spans)
            {
                var key = TermNormalizer.Normalize(text);
                if (key.Length < 2 || text.Length > 60) continue;

                if (!items.TryGetValue(key, out var c))
                    items[key] = c = new UnresolvedCandidate { Text = text.Trim(), FirstSeen = now };
                c.Count++;
                c.LastSeen = now;
                c.Guess  ??= guess;
                c.Example  = example is { Length: > 200 } ? example[..200] : example;
                changed = true;
            }

            foreach (var old in items.OrderByDescending(kv => kv.Value.LastSeen).Skip(capacity).Select(kv => kv.Key).ToList())
                items.Remove(old);
        }
        return changed;
    }

    public bool Remove(string text)
    {
        lock (gate) return items.Remove(TermNormalizer.Normalize(text));
    }

    public void Save()
    {
        List<UnresolvedCandidate> snapshot;
        lock (gate) snapshot = items.Values.OrderByDescending(i => i.LastSeen).ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(snapshot));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch
        {
            // Best effort
        }
    }
}
