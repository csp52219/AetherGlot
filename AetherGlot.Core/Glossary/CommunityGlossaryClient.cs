using System.Net;
using System.Text.Json;
using AetherGlot.Core.Connection;

namespace AetherGlot.Core.Glossary;

public enum GlossaryOrigin
{
    None,
    Remote,
    NotModified,
    Cache,
    Bundled,
}

/// <summary>Result of loading the community dictionary.</summary>
public sealed record CommunityGlossaryResult(
    GlossaryFile.ParseResult? Parsed,
    GlossaryOrigin Origin,
    DateTimeOffset? FetchedAt,
    string? Error)
{
    public IReadOnlyList<TermEntry> Entries => Parsed?.Entries ?? [];
}

/// <summary>
/// Downloads the community dictionary, caches it on disk (with ETag) and falls back to the
/// cache, then to the copy bundled with the plugin.
/// <para>
/// Cloudflare Access / extra headers are attached ONLY when the dictionary is served from the
/// same host as the configured LiteLLM / OpenAI-compatible endpoint, so the service token is
/// never sent to a third party such as GitHub.
/// </para>
/// </summary>
public sealed class CommunityGlossaryClient
{
    public const string CacheFileName = "community-glossary.json";
    public const string MetaFileName  = "community-glossary.meta.json";
    private const int MaxBytes     = 8 * 1024 * 1024;
    private const int MaxRedirects = 3;

    private readonly HttpClient httpClient;

    public CommunityGlossaryClient(HttpClient httpClient) => this.httpClient = httpClient;

    private sealed record Meta(string? ETag, DateTimeOffset FetchedAt, string Url);

    public async Task<CommunityGlossaryResult> LoadAsync(
        string? url, string cacheDirectory, string? bundledPath, ConnectionSettings? connection,
        bool download, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        var cachePath = Path.Combine(cacheDirectory, CacheFileName);
        var metaPath  = Path.Combine(cacheDirectory, MetaFileName);
        var meta      = ReadMeta(metaPath);
        string? error = null;

        if (download && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                var etag = meta?.Url == url && File.Exists(cachePath) ? meta.ETag : null;
                var (status, body, newEtag) = await FetchAsync(new Uri(url), etag, connection, ct).ConfigureAwait(false);

                if (status == HttpStatusCode.NotModified && File.Exists(cachePath))
                {
                    var cached = GlossaryFile.Parse(await File.ReadAllTextAsync(cachePath, ct).ConfigureAwait(false), TermSource.Community);
                    var now = DateTimeOffset.UtcNow;
                    WriteMeta(metaPath, new Meta(etag, now, url));
                    return new CommunityGlossaryResult(cached, GlossaryOrigin.NotModified, now, null);
                }

                var parsed = GlossaryFile.Parse(body!, TermSource.Community);
                if (parsed.Entries.Count > 0)
                {
                    await File.WriteAllTextAsync(cachePath + ".tmp", body, ct).ConfigureAwait(false);
                    File.Move(cachePath + ".tmp", cachePath, overwrite: true);
                    var now = DateTimeOffset.UtcNow;
                    WriteMeta(metaPath, new Meta(newEtag, now, url));
                    return new CommunityGlossaryResult(parsed, GlossaryOrigin.Remote, now, parsed.Ok ? null : $"{parsed.Errors.Count} invalid entries skipped.");
                }
                error = parsed.Errors.FirstOrDefault() ?? "The downloaded dictionary has no entries.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (LlmHttpException ex)
            {
                error = ex.Message;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or IOException or InvalidOperationException)
            {
                error = ex switch
                {
                    TaskCanceledException => "The download timed out.",
                    HttpRequestException hre => $"Network error: {hre.HttpRequestError}.",
                    UriFormatException => "The dictionary URL is invalid.",
                    _ => ex.Message,
                };
            }
        }

        if (File.Exists(cachePath))
        {
            var cached = GlossaryFile.Parse(await File.ReadAllTextAsync(cachePath, ct).ConfigureAwait(false), TermSource.Community);
            if (cached.Entries.Count > 0)
                return new CommunityGlossaryResult(cached, GlossaryOrigin.Cache, meta?.FetchedAt, error);
        }

        if (bundledPath is not null && File.Exists(bundledPath))
        {
            var bundled = GlossaryFile.Parse(await File.ReadAllTextAsync(bundledPath, ct).ConfigureAwait(false), TermSource.Community);
            return new CommunityGlossaryResult(bundled, GlossaryOrigin.Bundled, null, error);
        }

        return new CommunityGlossaryResult(null, GlossaryOrigin.None, null, error ?? "No community dictionary available.");
    }

    /// <summary>True when Access headers may be sent to <paramref name="target"/>.</summary>
    public static bool SameOriginAsConnection(Uri target, ConnectionSettings? connection)
    {
        if (connection is null || connection.Mode == ConnectionMode.Direct || string.IsNullOrWhiteSpace(connection.BaseUrl))
            return false;
        try
        {
            var root = EndpointResolver.Root(connection.BaseUrl);
            return string.Equals(root.Host, target.Host, StringComparison.OrdinalIgnoreCase) &&
                   root.Scheme == target.Scheme && root.Port == target.Port;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(HttpStatusCode Status, string? Body, string? ETag)> FetchAsync(
        Uri uri, string? etag, ConnectionSettings? connection, CancellationToken ct)
    {
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
                throw new InvalidOperationException("The dictionary URL must use https.");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            var withAccess = SameOriginAsConnection(uri, connection);
            if (withAccess) RequestDecorator.Apply(request, connection!);

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var status = response.StatusCode;

            if (status == HttpStatusCode.NotModified)
                return (status, null, etag);

            if ((int)status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (withAccess && AccessResponseClassifier.IsCloudflareAccessLocation(next))
                    throw new LlmHttpException(HttpFailureKind.CloudflareAccessRedirect, status,
                        AccessResponseClassifier.Describe(HttpFailureKind.CloudflareAccessRedirect, status));
                uri = next;
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new LlmHttpException(HttpFailureKind.Other, status, $"The dictionary could not be downloaded (HTTP {(int)status}).");

            if (response.Content.Headers.ContentLength > MaxBytes)
                throw new InvalidOperationException("The dictionary is too large.");

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (body.Length > MaxBytes)
                throw new InvalidOperationException("The dictionary is too large.");
            if (AccessResponseClassifier.LooksLikeHtml(body))
                throw new LlmHttpException(withAccess ? HttpFailureKind.CloudflareAccessForbidden : HttpFailureKind.UnexpectedContent, status,
                    withAccess ? AccessResponseClassifier.Describe(HttpFailureKind.CloudflareAccessForbidden, status) : "The dictionary URL returned an HTML page.");

            return (status, body, response.Headers.ETag?.ToString());
        }
        throw new InvalidOperationException("Too many redirects.");
    }

    private static Meta? ReadMeta(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Meta>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteMeta(string path, Meta meta)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(meta)); }
        catch { /* cache metadata is best effort */ }
    }
}
