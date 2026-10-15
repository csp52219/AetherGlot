namespace AetherGlot.Core.Security;

/// <summary>
/// In-memory read cache in front of a slower store (Credential Manager), so building a
/// connection snapshot per chat message does not hit the OS every time.
/// </summary>
public sealed class CachingSecretStore : ISecretStore
{
    private readonly ISecretStore inner;
    private readonly Dictionary<string, string?> cache = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public CachingSecretStore(ISecretStore inner) => this.inner = inner;

    public string? Get(string name)
    {
        lock (gate)
        {
            if (cache.TryGetValue(name, out var v)) return v;
        }
        var value = inner.Get(name);
        lock (gate) cache[name] = value;
        return value;
    }

    public void Set(string name, string? value)
    {
        inner.Set(name, value);
        lock (gate) cache.Remove(name); // re-read so a failed write is not masked
    }

    public void Delete(string name)
    {
        inner.Delete(name);
        lock (gate) cache.Remove(name);
    }
}
