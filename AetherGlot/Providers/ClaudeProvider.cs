namespace AetherGlot.Providers;

/// <summary>
/// LLM provider backed by the Anthropic Claude Messages API.
/// Uses the x-api-key / anthropic-version headers per the official spec.
/// </summary>
public sealed class ClaudeProvider : ITranslatorProvider
{
    private const string FallbackModel      = "claude-haiku-4-5-20251001";
    private const string DefaultEndpoint    = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion   = "2023-06-01";
    private const int    MaxTokens          = 1024;

    private readonly HttpClient httpClient;

    public string ProviderName => "Claude";

    public ClaudeProvider(HttpClient httpClient) => this.httpClient = httpClient;

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
            model      = resolvedModel,
            max_tokens = MaxTokens,
            system     = systemPrompt,
            messages   = new[]
            {
                new { role = "user", content = userPrompt },
            },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-api-key",          apiKey);
        request.Headers.Add("anthropic-version",  AnthropicVersion);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
                  .GetProperty("content")[0]
                  .GetProperty("text")
                  .GetString() ?? string.Empty;
    }
}
