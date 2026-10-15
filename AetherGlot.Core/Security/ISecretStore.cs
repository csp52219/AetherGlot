namespace AetherGlot.Core.Security;

/// <summary>
/// Storage for secrets (API keys, LiteLLM virtual key, Cloudflare service token, extra header values).
/// The plugin uses Windows Credential Manager; tests use <see cref="InMemorySecretStore"/>.
/// Secrets must never be written to Configuration.json.
/// </summary>
public interface ISecretStore
{
    string? Get(string name);

    /// <summary>Stores <paramref name="value"/>; null or empty deletes the entry.</summary>
    void Set(string name, string? value);

    void Delete(string name);
}

/// <summary>Well-known secret names.</summary>
public static class SecretNames
{
    public const string LiteLlmKey          = "LiteLLM.VirtualKey";
    public const string OpenAICompatibleKey = "OpenAICompatible.ApiKey";
    public const string CfClientId          = "CloudflareAccess.ClientId";
    public const string CfClientSecret      = "CloudflareAccess.ClientSecret";

    public static string DirectApiKey(string provider) => $"Direct.{provider}.ApiKey";

    public static string ExtraHeader(string headerName) => $"Header.{headerName.Trim()}";
}

/// <summary>Non-persistent store for tests and for running without Credential Manager.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public string? Get(string name)
    {
        lock (gate) return values.TryGetValue(name, out var v) ? v : null;
    }

    public void Set(string name, string? value)
    {
        lock (gate)
        {
            if (string.IsNullOrEmpty(value)) values.Remove(name);
            else values[name] = value;
        }
    }

    public void Delete(string name)
    {
        lock (gate) values.Remove(name);
    }
}
