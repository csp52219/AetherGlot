using System.Net;

namespace AetherGlot.Core.Connection;

/// <summary>What kind of failure an HTTP response represents.</summary>
public enum HttpFailureKind
{
    /// <summary>Cloudflare Access redirected to its login page (302 etc.).</summary>
    CloudflareAccessRedirect,

    /// <summary>Cloudflare Access answered 403 with its own (HTML) page.</summary>
    CloudflareAccessForbidden,

    /// <summary>401 from the origin (e.g. invalid or expired LiteLLM virtual key).</summary>
    Unauthorized,

    /// <summary>The requested model does not exist or is not allowed for this key.</summary>
    ModelNotFound,

    /// <summary>The model / proxy rejected response_format.</summary>
    JsonModeNotSupported,

    /// <summary>The server answered with something that is not JSON when JSON was expected.</summary>
    UnexpectedContent,

    /// <summary>Any other non-success status.</summary>
    Other,
}

/// <summary>
/// HTTP-level failure talking to the LLM backend.
/// The message never contains request headers, keys or the raw response body.
/// </summary>
public sealed class LlmHttpException : Exception
{
    public HttpFailureKind Kind { get; }
    public HttpStatusCode? StatusCode { get; }

    public LlmHttpException(HttpFailureKind kind, HttpStatusCode? statusCode, string message)
        : base(message)
    {
        Kind       = kind;
        StatusCode = statusCode;
    }

    public bool IsCloudflareAccess =>
        Kind is HttpFailureKind.CloudflareAccessRedirect or HttpFailureKind.CloudflareAccessForbidden;
}
