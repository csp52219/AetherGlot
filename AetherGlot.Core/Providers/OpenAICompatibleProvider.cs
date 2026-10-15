using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AetherGlot.Core.Connection;

namespace AetherGlot.Providers;

/// <summary>
/// Chat Completions client for any OpenAI-compatible API (OpenAI itself, LiteLLM, Ollama, vLLM, ...).
/// </summary>
public class OpenAICompatibleProvider : ITranslatorProvider, IModelLister
{
    private readonly LlmHttpSender sender;
    private readonly Uri chatEndpoint;
    private readonly Uri? modelsEndpoint;

    protected ConnectionSettings Settings { get; }

    public virtual string ProviderName => "OpenAICompatible";

    /// <param name="chatEndpoint">Full /chat/completions URL.</param>
    /// <param name="modelsEndpoint">Full /models URL, or null when listing is not supported.</param>
    public OpenAICompatibleProvider(LlmHttpSender sender, ConnectionSettings settings, Uri chatEndpoint, Uri? modelsEndpoint)
    {
        this.sender         = sender;
        Settings            = settings;
        this.chatEndpoint   = chatEndpoint;
        this.modelsEndpoint = modelsEndpoint;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new InvalidOperationException("No model is configured.");

        var payload = new JsonObject
        {
            ["model"]      = request.Model,
            ["max_tokens"] = request.MaxTokens,
            ["messages"]   = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JsonObject { ["role"] = "user",   ["content"] = request.UserPrompt },
            },
        };
        if (request.Temperature is { } t)
            payload["temperature"] = t;
        if (request.JsonMode)
            payload["response_format"] = new JsonObject { ["type"] = "json_object" };
        if (!string.IsNullOrWhiteSpace(Settings.UserTag))
            payload["user"] = Settings.UserTag;

        using var http = new HttpRequestMessage(HttpMethod.Post, chatEndpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        RequestDecorator.SetBearer(http, Settings.ApiKey);

        var exchange = await sender.SendAsync(http, Settings, expectJson: true, ct).ConfigureAwait(false);
        return ParseCompletion(exchange, request);
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        if (modelsEndpoint is null)
            throw new NotSupportedException("Model listing is not supported for this connection.");

        using var http = new HttpRequestMessage(HttpMethod.Get, modelsEndpoint);
        RequestDecorator.SetBearer(http, Settings.ApiKey);

        var exchange = await sender.SendAsync(http, Settings, expectJson: true, ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(exchange.Body);
            return doc.RootElement.GetProperty("data")
                      .EnumerateArray()
                      .Select(e => e.GetProperty("id").GetString())
                      .Where(id => !string.IsNullOrWhiteSpace(id))
                      .Select(id => id!)
                      .Distinct(StringComparer.Ordinal)
                      .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                      .ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new LlmHttpException(HttpFailureKind.UnexpectedContent, exchange.StatusCode,
                "The model list response was not in the expected OpenAI format.");
        }
    }

    private static LlmResponse ParseCompletion(HttpExchange exchange, LlmRequest request)
    {
        try
        {
            using var doc = JsonDocument.Parse(exchange.Body);
            var root    = doc.RootElement;
            var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            var model   = root.TryGetProperty("model", out var m) ? m.GetString() ?? request.Model : request.Model;

            int? prompt = null, completion = null;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv)) prompt = pv;
                if (usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cv)) completion = cv;
            }

            return new LlmResponse(content, model, prompt, completion, exchange.Elapsed, request.JsonMode);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new LlmHttpException(HttpFailureKind.UnexpectedContent, exchange.StatusCode,
                "The completion response was not in the expected OpenAI format.");
        }
    }
}
