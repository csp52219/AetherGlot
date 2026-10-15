using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AetherGlot.Core.Connection;

namespace AetherGlot.Providers;

/// <summary>
/// Anthropic Messages API. The endpoint URL comes from configuration.
/// The Messages API has no response_format, so JSON is requested through the prompt only.
/// </summary>
public sealed class ClaudeProvider : ITranslatorProvider
{
    // Wire-protocol version of the Messages API (not a model or URL)
    private const string AnthropicVersion = "2023-06-01";

    private readonly LlmHttpSender sender;
    private readonly ConnectionSettings settings;

    public string ProviderName => "Claude";

    public ClaudeProvider(LlmHttpSender sender, ConnectionSettings settings)
    {
        this.sender   = sender;
        this.settings = settings;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new InvalidOperationException("No model is configured.");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            throw new InvalidOperationException("The Claude endpoint is not configured.");

        var payload = new JsonObject
        {
            ["model"]      = request.Model,
            ["max_tokens"] = request.MaxTokens,
            ["system"]     = request.SystemPrompt,
            ["messages"]   = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt } },
        };
        if (request.Temperature is { } t)
            payload["temperature"] = t;

        using var http = new HttpRequestMessage(HttpMethod.Post, settings.BaseUrl)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        http.Headers.TryAddWithoutValidation("x-api-key", settings.ApiKey);
        http.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

        var exchange = await sender.SendAsync(http, settings, expectJson: true, ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(exchange.Body);
            var root = doc.RootElement;
            var text = string.Concat(root.GetProperty("content").EnumerateArray()
                .Where(b => b.TryGetProperty("type", out var ty) && ty.GetString() == "text")
                .Select(b => b.GetProperty("text").GetString()));
            var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? request.Model : request.Model;

            int? input = null, output = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var i) && i.TryGetInt32(out var iv)) input = iv;
                if (usage.TryGetProperty("output_tokens", out var o) && o.TryGetInt32(out var ov)) output = ov;
            }
            return new LlmResponse(text, model, input, output, exchange.Elapsed, JsonModeUsed: false);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new LlmHttpException(HttpFailureKind.UnexpectedContent, exchange.StatusCode,
                "The Claude response was not in the expected format.");
        }
    }
}
