namespace AetherGlot.Core.Translation;

/// <summary>Remembers per model whether response_format (JSON mode) is accepted.</summary>
public interface IModelCapabilityStore
{
    /// <summary>true = accepted, false = rejected, null = not tried yet.</summary>
    bool? GetJsonModeSupport(string model);

    void SetJsonModeSupport(string model, bool supported);
}

/// <summary>
/// Thread-safe store. Keeps its own copy of the data so the configuration object is never
/// mutated from a background thread; <c>onChanged</c> is invoked (outside the lock) and the
/// plugin persists <see cref="Snapshot"/> on the framework thread.
/// </summary>
public sealed class ModelCapabilityStore : IModelCapabilityStore
{
    private readonly Dictionary<string, bool> backing;
    private readonly Action? onChanged;
    private readonly object gate = new();

    public ModelCapabilityStore(IEnumerable<KeyValuePair<string, bool>>? initial = null, Action? onChanged = null)
    {
        backing        = new Dictionary<string, bool>(initial ?? [], StringComparer.Ordinal);
        this.onChanged = onChanged;
    }

    public Dictionary<string, bool> Snapshot()
    {
        lock (gate) return new Dictionary<string, bool>(backing, StringComparer.Ordinal);
    }

    public bool? GetJsonModeSupport(string model)
    {
        lock (gate) return backing.TryGetValue(model, out var v) ? v : null;
    }

    public void SetJsonModeSupport(string model, bool supported)
    {
        lock (gate)
        {
            if (backing.TryGetValue(model, out var v) && v == supported) return;
            backing[model] = supported;
        }
        onChanged?.Invoke();
    }
}
