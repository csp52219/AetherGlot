using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AetherGlot.Core.Connection;

namespace AetherGlot.Providers;

/// <summary>
/// Google Gemini generateContent API. The endpoint template comes from configuration and
/// contains "{model}". The key is sent in the x-goog-api-key header, never in the URL,
/// so it cannot leak through logged URLs.
/// </summary>
public sealed class GeminiProvider : ITranslatorProvider
{
    private readonly LlmHttpSender sender;
    private readonly ConnectionSettings settings;

    public string ProviderName => "Gemini";

    public GeminiProvider(LlmHttpSender sender, ConnectionSettings settings)
    {
        this.sender   = sender;
        this.settings = settings;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new InvalidOperationException("No model is configured.");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            throw new InvalidOperationException("The Gemini endpoint is not configured.");

        var endpoint = settings.BaseUrl.Replace("{model}", Uri.EscapeDataString(request.Model), StringComparison.Ordinal);

        var generationConfig = new JsonObject { ["maxOutputTokens"] = request.MaxTokens };
        if (request.JsonMode)
            generationConfig["responseMimeType"] = "application/json";
        if (request.Temperature is { } t)
            generationConfig["temperature"] = t;

        var payload = new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = request.SystemPrompt } } },
            ["contents"]           = new JsonArray { new JsonObject { ["role"] = "user", ["parts"] = new JsonArray { new JsonObject { ["text"] = request.UserPrompt } } } },
            ["generationConfig"]   = generationConfig,
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        http.Headers.TryAddWithoutValidation("x-goog-api-key", settings.ApiKey);

        var exchange = await sender.SendAsync(http, settings, expectJson: true, ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(exchange.Body);
            var root = doc.RootElement;
            var text = string.Concat(root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")
                .EnumerateArray().Select(p => p.TryGetProperty("text", out var x) ? x.GetString() : null));

            int? input = null, output = null;
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var i) && i.TryGetInt32(out var iv)) input = iv;
                if (usage.TryGetProperty("candidatesTokenCount", out var o) && o.TryGetInt32(out var ov)) output = ov;
            }
            return new LlmResponse(text, request.Model, input, output, exchange.Elapsed, request.JsonMode);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new LlmHttpException(HttpFailureKind.UnexpectedContent, exchange.StatusCode,
                "The Gemini response was not in the expected format.");
        }
    }
}
