namespace AetherGlot.Providers;

/// <summary>One chat-completion request, provider-agnostic.</summary>
public sealed record LlmRequest(
    string Model,
    string SystemPrompt,
    string UserPrompt,
    bool JsonMode,
    int MaxTokens = 1024,
    double? Temperature = null);

/// <summary>Raw model output plus metadata for the inspector.</summary>
public sealed record LlmResponse(
    string Content,
    string Model,
    int? PromptTokens,
    int? CompletionTokens,
    TimeSpan Elapsed,
    bool JsonModeUsed);

/// <summary>
/// Abstraction over a single LLM API backend. Implementations live in Core (no Dalamud
/// dependency) and receive their connection (endpoint, key, headers) at construction.
/// <para>
/// When <see cref="LlmRequest.JsonMode"/> is true the provider asks the API for structured
/// JSON output (response_format / responseMimeType). If the backend rejects that, the
/// provider throws <see cref="Core.Connection.LlmHttpException"/> with kind
/// <see cref="Core.Connection.HttpFailureKind.JsonModeNotSupported"/> and the caller retries
/// with prompt-only JSON instructions.
/// </para>
/// </summary>
public interface ITranslatorProvider
{
    string ProviderName { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}

/// <summary>Providers that can list available models (OpenAI-compatible /v1/models).</summary>
public interface IModelLister
{
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
}
