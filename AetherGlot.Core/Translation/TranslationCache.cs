namespace AetherGlot.Core.Translation;

/// <summary>Small thread-safe LRU cache with a fixed capacity.</summary>
public sealed class TranslationCache<TValue>
{
    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, TValue Value)>> map = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, TValue Value)> order = new();
    private int capacity;

    public TranslationCache(int capacity) => this.capacity = Math.Max(0, capacity);

    public int Count
    {
        get { lock (gate) return map.Count; }
    }

    public int Capacity
    {
        get { lock (gate) return capacity; }
        set
        {
            lock (gate)
            {
                capacity = Math.Max(0, value);
                Trim();
            }
        }
    }

    public bool TryGet(string key, out TValue value)
    {
        lock (gate)
        {
            if (map.TryGetValue(key, out var node))
            {
                order.Remove(node);
                order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public void Set(string key, TValue value)
    {
        lock (gate)
        {
            if (capacity == 0) return;
            if (map.TryGetValue(key, out var existing))
            {
                order.Remove(existing);
                map.Remove(key);
            }
            map[key] = order.AddFirst((key, value));
            Trim();
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            map.Clear();
            order.Clear();
        }
    }

    private void Trim()
    {
        while (map.Count > capacity && order.Last is { } last)
        {
            map.Remove(last.Value.Key);
            order.RemoveLast();
        }
    }
}
