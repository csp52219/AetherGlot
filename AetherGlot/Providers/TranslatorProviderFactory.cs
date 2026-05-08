using System.Net.Http.Headers;
using AetherGlot.Providers;

namespace AetherGlot.Providers;

/// <summary>
/// Creates <see cref="ITranslatorProvider"/> instances backed by a shared <see cref="HttpClient"/>.
/// </summary>
public sealed class TranslatorProviderFactory : ITranslatorProviderFactory
{
    private readonly HttpClient httpClient;

    public TranslatorProviderFactory(HttpClient httpClient) => this.httpClient = httpClient;

    public ITranslatorProvider GetProvider(string providerName) =>
        providerName switch
        {
            "OpenAI" => new OpenAIProvider(httpClient),
            "Gemini" => new GeminiProvider(httpClient),
            "Claude" => new ClaudeProvider(httpClient),
            "Custom" => new CustomEndpointProvider(httpClient),
            _ => throw new NotSupportedException($"Unknown provider: {providerName}"),
        };
}
