using System.Net.Http.Headers;

namespace AetherGlot.Providers;

/// <summary>
/// LLM provider backed by the OpenAI Chat Completions API.
/// Endpoint, model, and API key are all supplied at call time — nothing is hardcoded.
/// </summary>
public sealed class OpenAIProvider : ITranslatorProvider
{
    private const string FallbackModel    = "gpt-4o-mini";
    private const string DefaultEndpoint  = "https://api.openai.com/v1/chat/completions";

    private readonly HttpClient httpClient;

    public string ProviderName => "OpenAI";

    public OpenAIProvider(HttpClient httpClient) => this.httpClient = httpClient;

    public async Task<string> TranslateAsync(
        string text,
        string systemPrompt,
        string userPrompt,
        string apiKey,
        string model,
        string? customEndpoint = null,
        CancellationToken ct = default)
    {
        var endpoint      = string.IsNullOrWhiteSpace(customEndpoint) ? DefaultEndpoint : customEndpoint;
        var resolvedModel = string.IsNullOrWhiteSpace(model)          ? FallbackModel   : model;

        var payload = JsonSerializer.Serialize(new
        {
            model    = resolvedModel,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   },
            },
            response_format = new { type = "json_object" },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
                  .GetProperty("choices")[0]
                  .GetProperty("message")
                  .GetProperty("content")
                  .GetString() ?? string.Empty;
    }
}
