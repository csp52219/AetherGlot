using System.Net;
using System.Net.Http.Headers;

namespace AetherGlot.Core.Connection;

/// <summary>
/// Classifies HTTP responses so that a Cloudflare Access login page (redirect or HTML 403)
/// is reported as such instead of being fed to a JSON parser.
/// </summary>
public static class AccessResponseClassifier
{
    /// <summary>
    /// Returns null for a 2xx response, otherwise the failure kind.
    /// <paramref name="bodySnippet"/> is a short prefix of the body (may be empty) used only
    /// for classification; it is never put into exception messages.
    /// </summary>
    public static HttpFailureKind? Classify(HttpResponseMessage response, string bodySnippet)
    {
        var status = (int)response.StatusCode;
        if (status is >= 200 and < 300) return null;

        if (status is >= 300 and < 400)
        {
            // Cloudflare Access redirects to https://<team>.cloudflareaccess.com/cdn-cgi/access/login/...
            // Any other redirect is treated the same way: following it would only lead to an HTML page.
            return HttpFailureKind.CloudflareAccessRedirect;
        }

        if (status == 403 && (IsHtml(response.Content?.Headers.ContentType) || LooksLikeHtml(bodySnippet) || IsCloudflare(response)))
            return HttpFailureKind.CloudflareAccessForbidden;

        if (status == 401)
            return HttpFailureKind.Unauthorized;

        if (status is 400 or 422 && MentionsResponseFormat(bodySnippet))
            return HttpFailureKind.JsonModeNotSupported;

        if (status is 400 or 404 && MentionsMissingModel(bodySnippet))
            return HttpFailureKind.ModelNotFound;

        // LiteLLM answers 401 for bad keys, but some deployments use 403 + JSON for model ACLs
        if (status == 403 && MentionsModel(bodySnippet))
            return HttpFailureKind.ModelNotFound;

        return HttpFailureKind.Other;
    }

    /// <summary>True when the Location header points at a Cloudflare Access login page.</summary>
    public static bool IsCloudflareAccessLocation(Uri? location)
    {
        if (location is null) return false;
        var s = location.ToString();
        return s.Contains("cloudflareaccess.com", StringComparison.OrdinalIgnoreCase)
            || s.Contains("/cdn-cgi/access", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsHtml(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType is { } mt &&
        (mt.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
         mt.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase));

    public static bool LooksLikeHtml(string body)
    {
        var s = body.TrimStart();
        return s.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCloudflare(HttpResponseMessage response) =>
        response.Headers.Contains("cf-ray") &&
        response.Headers.Server.Any(p => p.Product?.Name?.Equals("cloudflare", StringComparison.OrdinalIgnoreCase) == true) &&
        !IsJson(response.Content?.Headers.ContentType);

    private static bool IsJson(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

    private static bool MentionsResponseFormat(string body) =>
        body.Contains("response_format", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("json_object", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsModel(string body) =>
        body.Contains("model", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsMissingModel(string body) =>
        MentionsModel(body) &&
        (body.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("not allowed", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("Invalid model", StringComparison.OrdinalIgnoreCase));

    /// <summary>User-facing explanation (no secrets, no body).</summary>
    public static string Describe(HttpFailureKind kind, HttpStatusCode? status)
    {
        var code = status is null ? "?" : ((int)status).ToString();
        return kind switch
        {
            HttpFailureKind.CloudflareAccessRedirect  => $"Rejected by Cloudflare Access (HTTP {code}, redirected to the login page). Check that the Access policy uses Service Auth and that the service token is correct.",
            HttpFailureKind.CloudflareAccessForbidden => $"Rejected by Cloudflare Access (HTTP {code}). Check that the Access policy uses Service Auth and that the service token is correct.",
            HttpFailureKind.Unauthorized              => $"Unauthorized (HTTP {code}). The API key / LiteLLM virtual key is invalid or expired.",
            HttpFailureKind.ModelNotFound             => $"Model not found (HTTP {code}). Check that the key is allowed to use this model.",
            HttpFailureKind.JsonModeNotSupported      => $"The model rejected response_format (HTTP {code}).",
            HttpFailureKind.UnexpectedContent         => $"Unexpected non-JSON response (HTTP {code}).",
            _                                         => $"Request failed (HTTP {code}).",
        };
    }
}
