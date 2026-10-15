namespace AetherGlot.Core.Connection;

/// <summary>A request header as shown in the inspector. Values are never shown, only names.</summary>
public sealed record HeaderPreview(string Name, string DisplayValue, bool Sensitive);

/// <summary>
/// Describes the chat request the current connection sends (endpoint and header names) for the
/// inspector. Every header value except a few fixed, non-secret ones is replaced with a mask;
/// extra headers are user-defined and may hold secrets, so they are masked too.
/// </summary>
public static class RequestPreview
{
    public const string Mask = "••••";

    public static IReadOnlyList<HeaderPreview> Headers(ConnectionSettings settings)
    {
        var list = new List<HeaderPreview> { new("Content-Type", "application/json", false) };

        switch (settings.Mode)
        {
            case ConnectionMode.Direct when settings.DirectProvider == DirectProvider.Claude:
                if (!string.IsNullOrWhiteSpace(settings.ApiKey)) list.Add(Masked("x-api-key"));
                list.Add(new HeaderPreview("anthropic-version", "(fixed)", false));
                break;
            case ConnectionMode.Direct when settings.DirectProvider == DirectProvider.Gemini:
                if (!string.IsNullOrWhiteSpace(settings.ApiKey)) list.Add(Masked("x-goog-api-key"));
                break;
            default:
                if (!string.IsNullOrWhiteSpace(settings.ApiKey)) list.Add(new HeaderPreview("Authorization", "Bearer " + Mask, true));
                break;
        }

        if (settings.CloudflareAccessEnabled)
        {
            if (!string.IsNullOrWhiteSpace(settings.CloudflareClientId)) list.Add(Masked(RequestDecorator.CfClientIdHeader));
            if (!string.IsNullOrWhiteSpace(settings.CloudflareClientSecret)) list.Add(Masked(RequestDecorator.CfClientSecretHeader));
        }

        foreach (var (name, _) in settings.ExtraHeaders)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            list.RemoveAll(h => h.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            list.Add(Masked(name.Trim()));
        }
        return list;
    }

    /// <summary>Where chat requests go (no credentials are ever part of the URL).</summary>
    public static string Endpoint(ConnectionSettings settings)
    {
        try
        {
            return settings.Mode switch
            {
                ConnectionMode.LiteLlm or ConnectionMode.OpenAICompatible => EndpointResolver.ChatCompletions(settings.BaseUrl).ToString(),
                _ => settings.BaseUrl,
            };
        }
        catch (Exception)
        {
            return settings.BaseUrl;
        }
    }

    private static HeaderPreview Masked(string name) => new(name, Mask, true);
}
