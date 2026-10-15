namespace AetherGlot.Core.Translation;

/// <summary>
/// Returns one shared instance for equal strings. The system prompt is almost the same for
/// every message, and each outcome keeps it for the inspector; sharing it saves a few KB per
/// translation log / cache entry. Bounded: cleared when it holds too many distinct strings.
/// Thread-safe: inbound and outbound translations share one pool.
/// </summary>
public sealed class StringPool
{
    private readonly Dictionary<string, string> pool = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private readonly int capacity;

    public StringPool(int capacity = 64) => this.capacity = Math.Max(1, capacity);

    public int Count
    {
        get { lock (gate) return pool.Count; }
    }

    public string Share(string value)
    {
        lock (gate)
        {
            if (pool.TryGetValue(value, out var shared)) return shared;
            if (pool.Count >= capacity) pool.Clear();
            pool[value] = value;
            return value;
        }
    }
}
