namespace AetherGlot.Core.Connection;

/// <summary>Builds OpenAI-compatible endpoint URLs from a user-entered base URL.</summary>
public static class EndpointResolver
{
    /// <summary>
    /// Normalises "https://host", "https://host/", "https://host/v1" and "https://host/v1/"
    /// to "https://host/v1".
    /// </summary>
    public static string NormalizeV1Base(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Base URL is not configured.");

        var s = baseUrl.Trim().TrimEnd('/');
        // Accept a pasted full endpoint as well
        foreach (var suffix in new[] { "/chat/completions", "/models" })
        {
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                s = s[..^suffix.Length];
        }

        if (!s.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            s += "/v1";
        return s;
    }

    public static Uri ChatCompletions(string baseUrl) => new($"{NormalizeV1Base(baseUrl)}/chat/completions");

    public static Uri Models(string baseUrl) => new($"{NormalizeV1Base(baseUrl)}/models");

    /// <summary>Origin root (scheme://host[:port]/), used for reachability checks.</summary>
    public static Uri Root(string baseUrl)
    {
        var v1 = new Uri(NormalizeV1Base(baseUrl));
        return new Uri(v1.GetLeftPart(UriPartial.Authority) + "/");
    }
}
