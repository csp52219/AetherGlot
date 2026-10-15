using System.Net;
using System.Text;

namespace AetherGlot.Tests.Support;

/// <summary>A captured outgoing request (headers + body read before disposal).</summary>
public sealed record CapturedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);

/// <summary>HttpMessageHandler that answers from a delegate and records every request.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<CapturedRequest, int, HttpResponseMessage> respond;

    public List<CapturedRequest> Requests { get; } = [];

    public FakeHttpHandler(Func<CapturedRequest, int, HttpResponseMessage> respond) => this.respond = respond;

    public FakeHttpHandler(Func<CapturedRequest, HttpResponseMessage> respond) : this((r, _) => respond(r)) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body    = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        var captured = new CapturedRequest(request.Method, request.RequestUri!, headers, body);
        Requests.Add(captured);
        var response = respond(captured, Requests.Count - 1);
        response.RequestMessage ??= request;
        return response;
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Html(HttpStatusCode status, string html = "<!DOCTYPE html><html><body>Sign in ・ Cloudflare Access</body></html>") =>
        new(status) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    public static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var r = new HttpResponseMessage(status) { Content = new StringContent(string.Empty) };
        r.Headers.Location = new Uri(location);
        return r;
    }

    /// <summary>OpenAI chat-completions envelope around <paramref name="content"/>.</summary>
    public static HttpResponseMessage Completion(string content, string model = "test-model") =>
        Json(System.Text.Json.JsonSerializer.Serialize(new
        {
            model,
            choices = new[] { new { message = new { role = "assistant", content } } },
            usage   = new { prompt_tokens = 10, completion_tokens = 5 },
        }));
}
