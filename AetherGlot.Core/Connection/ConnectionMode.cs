namespace AetherGlot.Core.Connection;

/// <summary>How the plugin reaches the LLM.</summary>
public enum ConnectionMode
{
    /// <summary>Straight to a vendor API (OpenAI, Claude, Gemini).</summary>
    Direct,

    /// <summary>Through a LiteLLM proxy (OpenAI-compatible, virtual keys, /v1/models).</summary>
    LiteLlm,

    /// <summary>Any other OpenAI-compatible endpoint (Ollama, LM Studio, vLLM, ...).</summary>
    OpenAICompatible,
}

/// <summary>Vendor used when <see cref="ConnectionMode.Direct"/> is selected.</summary>
public enum DirectProvider
{
    OpenAI,
    Claude,
    Gemini,
}
