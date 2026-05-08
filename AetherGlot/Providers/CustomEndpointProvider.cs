using System.Net.Http.Headers;

namespace AetherGlot.Providers;

/// <summary>
/// LLM provider for any OpenAI-compatible endpoint (Ollama, LM Studio, vLLM, etc.).
/// The model name and endpoint are read entirely from user configuration.
/// </summary>
public sealed class CustomEndpointProvider : ITranslatorProvider
{
    private const string FallbackModel   = "llama3.2";
    private const string DefaultEndpoint = "http://localhost:11434/v1/chat/completions";

    private readonly HttpClient httpClient;

    public string ProviderName => "Custom";

    public CustomEndpointProvider(HttpClient httpClient) => this.httpClient = httpClient;

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
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        // API key is optional for local endpoints like Ollama
        if (!string.IsNullOrWhiteSpace(apiKey))
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
