namespace AetherGlot.Core.Security;

/// <summary>
/// Moves plaintext API keys from the v1 configuration (Dictionary provider → key) into the
/// secret store. An entry is removed from the dictionary only after it has been stored and
/// read back successfully, so a failing store never loses a key.
/// </summary>
public static class LegacySecretsMigrator
{
    /// <summary>Maps a v1 provider name to the secret name used by v2.</summary>
    public static string SecretNameFor(string legacyProvider) => legacyProvider switch
    {
        "Custom" => SecretNames.OpenAICompatibleKey,
        _        => SecretNames.DirectApiKey(legacyProvider),
    };

    /// <returns>Number of keys migrated.</returns>
    public static int Migrate(IDictionary<string, string> legacyApiKeys, ISecretStore store)
    {
        var migrated = 0;
        foreach (var (provider, key) in legacyApiKeys.ToArray())
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                legacyApiKeys.Remove(provider);
                continue;
            }

            var name = SecretNameFor(provider);
            // Do not overwrite a key the user already stored through the new UI
            if (string.IsNullOrEmpty(store.Get(name)))
                store.Set(name, key);

            if (!string.IsNullOrEmpty(store.Get(name)))
            {
                legacyApiKeys.Remove(provider);
                migrated++;
            }
        }
        return migrated;
    }
}
