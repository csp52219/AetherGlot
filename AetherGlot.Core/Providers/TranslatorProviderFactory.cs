using AetherGlot.Core.Connection;

namespace AetherGlot.Providers;

/// <summary>Creates providers backed by one shared <see cref="LlmHttpSender"/>.</summary>
public sealed class TranslatorProviderFactory : ITranslatorProviderFactory
{
    private readonly LlmHttpSender sender;

    public TranslatorProviderFactory(LlmHttpSender sender) => this.sender = sender;

    public ITranslatorProvider GetProvider(ConnectionSettings settings) => settings.Mode switch
    {
        ConnectionMode.LiteLlm => new LiteLlmProvider(sender, settings),
        ConnectionMode.OpenAICompatible => new OpenAICompatibleProvider(
            sender, settings,
            EndpointResolver.ChatCompletions(settings.BaseUrl),
            EndpointResolver.Models(settings.BaseUrl)),
        ConnectionMode.Direct => settings.DirectProvider switch
        {
            // OpenAI direct: BaseUrl is the full chat/completions endpoint from configuration
            DirectProvider.OpenAI => new OpenAICompatibleProvider(
                sender, settings,
                new Uri(RequireUrl(settings.BaseUrl)),
                ModelsUrlNextTo(settings.BaseUrl)),
            DirectProvider.Claude => new ClaudeProvider(sender, settings),
            DirectProvider.Gemini => new GeminiProvider(sender, settings),
            _ => throw new NotSupportedException($"Unknown provider: {settings.DirectProvider}"),
        },
        _ => throw new NotSupportedException($"Unknown connection mode: {settings.Mode}"),
    };

    private static string RequireUrl(string url) =>
        string.IsNullOrWhiteSpace(url) ? throw new InvalidOperationException("The endpoint is not configured.") : url;

    private static Uri? ModelsUrlNextTo(string chatEndpoint) =>
        chatEndpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? new Uri(chatEndpoint[..^"/chat/completions".Length] + "/models")
            : null;
}
