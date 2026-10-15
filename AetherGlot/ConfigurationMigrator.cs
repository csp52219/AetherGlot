namespace AetherGlot;

/// <summary>
/// Upgrades older Configuration.json files. Runs once at startup on the framework thread.
/// Plaintext API keys are moved to the secret store and then removed from the file.
/// </summary>
internal static class ConfigurationMigrator
{
    public static void Run(Configuration config, ISecretStore secrets, IPluginLog log)
    {
        var changed = false;

        if (config.Version < 2)
        {
            MigrateConnectionV1(config);
            changed = true;
        }

        // Secrets: always attempt while plaintext keys remain (also retries a failed earlier run)
        if (config.ApiKeys is { Count: > 0 } keys)
        {
            try
            {
                var moved = LegacySecretsMigrator.Migrate(keys, secrets);
                log.Information("[AetherGlot] Moved {Count} API key(s) from Configuration.json to Windows Credential Manager.", moved);
                if (keys.Count > 0)
                    log.Warning("[AetherGlot] {Count} API key(s) could not be stored in Credential Manager and remain in Configuration.json.", keys.Count);
            }
            catch (Exception ex)
            {
                // Never log the exception message: it could in theory echo the value
                log.Error("[AetherGlot] Could not move API keys to Credential Manager ({Type}). They remain in Configuration.json.", ex.GetType().Name);
            }
            changed = true;
        }

        if (config.ApiKeys is { Count: 0 })
        {
            config.ApiKeys = null;
            changed = true;
        }

        // Older builds let the loader append saved channels to the defaults (duplicates)
        var distinct = config.EnabledChannels.Distinct().ToList();
        if (distinct.Count != config.EnabledChannels.Count)
        {
            config.EnabledChannels = distinct;
            changed = true;
        }

        if (config.Version < Configuration.CurrentVersion)
        {
            config.Version = Configuration.CurrentVersion;
            changed = true;
        }

        if (changed)
            config.Save();
    }

    private static void MigrateConnectionV1(Configuration config)
    {
        var c        = config.Connection;
        var provider = config.SelectedProvider ?? string.Empty;
        var endpoint = config.CustomEndpoint ?? string.Empty;

        if (provider == "Custom")
        {
            c.Mode = ConnectionMode.OpenAICompatible;
            c.OpenAICompatibleBaseUrl = endpoint;
        }
        else if (Enum.TryParse<DirectProvider>(provider, out var direct))
        {
            c.Mode = ConnectionMode.Direct;
            c.DirectProvider = direct;
            // v1 used CustomEndpoint as a per-provider override. Never carry over a URL with a key in it.
            if (!string.IsNullOrWhiteSpace(endpoint) && !endpoint.Contains("key=", StringComparison.OrdinalIgnoreCase))
                c.DirectEndpoints[direct.ToString()] = endpoint;
        }

        if (!string.IsNullOrWhiteSpace(config.SelectedModel))
        {
            c.InboundModel  = config.SelectedModel;
            c.OutboundModel = config.SelectedModel;
        }

        config.SelectedProvider = null;
        config.SelectedModel    = null;
        config.CustomEndpoint   = null;
    }
}
