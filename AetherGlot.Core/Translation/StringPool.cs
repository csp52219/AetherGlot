using System.Collections.Concurrent;

namespace AetherGlot.Core.Translation;

/// <summary>
/// Returns one shared instance for equal strings. The system prompt is almost the same for
/// every message, and each outcome keeps it for the inspector; sharing it saves a few KB per
/// translation log / cache entry. Bounded: cleared when it holds too many distinct strings.
/// </summary>
public sealed class StringPool
{
    private readonly ConcurrentDictionary<string, string> pool = new(StringComparer.Ordinal);
    private readonly int capacity;

    public StringPool(int capacity = 64) => this.capacity = Math.Max(1, capacity);

    public int Count => pool.Count;

    public string Share(string value)
    {
        if (pool.TryGetValue(value, out var shared)) return shared;
        if (pool.Count >= capacity) pool.Clear();
        return pool.GetOrAdd(value, value);
    }
}
