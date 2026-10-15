using System.Net.Http.Headers;

namespace AetherGlot.Core.Connection;

/// <summary>
/// Adds Cloudflare Access service-token headers and user-defined extra headers.
/// Applied to every outgoing request (chat, model list, community dictionary, diagnostics).
/// </summary>
public static class RequestDecorator
{
    public const string CfClientIdHeader     = "CF-Access-Client-Id";
    public const string CfClientSecretHeader = "CF-Access-Client-Secret";

    public static void Apply(HttpRequestMessage request, ConnectionSettings settings)
    {
        if (settings.CloudflareAccessEnabled)
        {
            if (!string.IsNullOrWhiteSpace(settings.CloudflareClientId))
                request.Headers.TryAddWithoutValidation(CfClientIdHeader, settings.CloudflareClientId);
            if (!string.IsNullOrWhiteSpace(settings.CloudflareClientSecret))
                request.Headers.TryAddWithoutValidation(CfClientSecretHeader, settings.CloudflareClientSecret);
        }

        foreach (var (name, value) in settings.ExtraHeaders)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name.Trim(), value ?? string.Empty);
        }
    }

    public static void SetBearer(HttpRequestMessage request, string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Header names that carry secrets and must be masked when displayed.</summary>
    public static bool IsSensitiveHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals(CfClientIdHeader, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(CfClientSecretHeader, StringComparison.OrdinalIgnoreCase) ||
        name.Equals("x-api-key", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("x-goog-api-key", StringComparison.OrdinalIgnoreCase);
}
