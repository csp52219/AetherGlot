using AetherGlot.Core.Connection;

namespace AetherGlot.Providers;

/// <summary>
/// LiteLLM proxy: OpenAI-compatible /v1/chat/completions and /v1/models, virtual key as
/// Bearer token, user tag in the "user" field, optional Cloudflare Access headers.
/// </summary>
public sealed class LiteLlmProvider : OpenAICompatibleProvider
{
    public override string ProviderName => "LiteLLM";

    public LiteLlmProvider(LlmHttpSender sender, ConnectionSettings settings)
        : base(sender, settings,
               EndpointResolver.ChatCompletions(settings.BaseUrl),
               EndpointResolver.Models(settings.BaseUrl))
    {
    }
}
