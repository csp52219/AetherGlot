namespace AetherGlot.Providers;

/// <summary>
/// LLM provider backed by the Google Gemini generateContent API.
/// The API key is embedded in the query string (Google's standard method).
/// </summary>
public sealed class GeminiProvider : ITranslatorProvider
{
    private const string FallbackModel       = "gemini-2.0-flash";
    private const string EndpointTemplate    =
        "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}";

    private readonly HttpClient httpClient;

    public string ProviderName => "Gemini";

    public GeminiProvider(HttpClient httpClient) => this.httpClient = httpClient;

    public async Task<string> TranslateAsync(
        string text,
        string systemPrompt,
        string userPrompt,
        string apiKey,
        string model,
        string? customEndpoint = null,
        CancellationToken ct = default)
    {
        var resolvedModel = string.IsNullOrWhiteSpace(model) ? FallbackModel : model;
        var endpoint = string.IsNullOrWhiteSpace(customEndpoint)
            ? string.Format(EndpointTemplate, resolvedModel, apiKey)
            : customEndpoint;

        var payload = JsonSerializer.Serialize(new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } },
            },
            contents = new[]
            {
                new { parts = new[] { new { text = userPrompt } } },
            },
            generationConfig = new { responseMimeType = "application/json" },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
                  .GetProperty("candidates")[0]
                  .GetProperty("content")
                  .GetProperty("parts")[0]
                  .GetProperty("text")
                  .GetString() ?? string.Empty;
    }
}
