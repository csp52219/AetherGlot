using System.Diagnostics;
using System.Net;

namespace AetherGlot.Core.Connection;

/// <summary>Result of a successful HTTP exchange.</summary>
public sealed record HttpExchange(HttpStatusCode StatusCode, string Body, TimeSpan Elapsed);

/// <summary>
/// Thin wrapper over a shared <see cref="HttpClient"/> that decorates requests, classifies
/// failures (Cloudflare Access, 401, model not found, ...) and never parses an HTML page.
/// <para>The HttpClient must be created with AllowAutoRedirect = false so Access redirects are visible.</para>
/// </summary>
public sealed class LlmHttpSender
{
    private const int SnippetLength = 2048;

    private readonly HttpClient httpClient;

    public LlmHttpSender(HttpClient httpClient) => this.httpClient = httpClient;

    /// <summary>Creates a handler suitable for the shared client (no auto redirect, pooled connections).</summary>
    public static HttpMessageHandler CreateDefaultHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect        = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression   = DecompressionMethods.All,
    };

    /// <summary>
    /// Sends the request after applying Cloudflare Access / extra headers.
    /// Throws <see cref="LlmHttpException"/> for non-success responses.
    /// When <paramref name="expectJson"/> is true, a 2xx HTML body is also rejected.
    /// </summary>
    public async Task<HttpExchange> SendAsync(
        HttpRequestMessage request,
        ConnectionSettings settings,
        bool expectJson,
        CancellationToken ct)
    {
        RequestDecorator.Apply(request, settings);

        var sw = Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        sw.Stop();

        var snippet = body.Length > SnippetLength ? body[..SnippetLength] : body;
        var kind    = AccessResponseClassifier.Classify(response, snippet);

        if (kind is { } failure)
        {
            // A redirect that does not point at Access is still reported as Access when Access is on;
            // otherwise report it as a generic failure.
            if (failure == HttpFailureKind.CloudflareAccessRedirect &&
                !settings.CloudflareAccessEnabled &&
                !AccessResponseClassifier.IsCloudflareAccessLocation(response.Headers.Location))
            {
                failure = HttpFailureKind.Other;
            }

            throw new LlmHttpException(failure, response.StatusCode,
                AccessResponseClassifier.Describe(failure, response.StatusCode));
        }

        if (expectJson &&
            (AccessResponseClassifier.IsHtml(response.Content?.Headers.ContentType) ||
             AccessResponseClassifier.LooksLikeHtml(snippet)))
        {
            // 200 + HTML: almost always an Access interstitial or a misconfigured base URL
            var htmlKind = settings.CloudflareAccessEnabled
                ? HttpFailureKind.CloudflareAccessForbidden
                : HttpFailureKind.UnexpectedContent;
            throw new LlmHttpException(htmlKind, response.StatusCode,
                AccessResponseClassifier.Describe(htmlKind, response.StatusCode));
        }

        return new HttpExchange(response.StatusCode, body, sw.Elapsed);
    }
}
