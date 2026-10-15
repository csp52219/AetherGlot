using AetherGlot.Core.Connection;
using Xunit.Abstractions;

namespace AetherGlot.Tests.Integration;

// Direct vendor APIs. Endpoints can be overridden with AETHERGLOT_<VENDOR>_ENDPOINT;
// the defaults below are only used by these tests (the plugin reads them from configuration).

public sealed class OpenAIIntegrationTests(ITestOutputHelper output) : PipelineIntegrationTestsBase(output)
{
    protected override ConnectionSettings? Settings { get; } = IntegrationEnv.Direct(
        DirectProvider.OpenAI, "AETHERGLOT_OPENAI_KEY", "AETHERGLOT_OPENAI_MODEL", "AETHERGLOT_OPENAI_ENDPOINT",
        "https://api.openai.com/v1/chat/completions");

    protected override string SkipReason => "Set AETHERGLOT_OPENAI_KEY and AETHERGLOT_OPENAI_MODEL to run OpenAI integration tests.";
}

public sealed class ClaudeIntegrationTests(ITestOutputHelper output) : PipelineIntegrationTestsBase(output)
{
    protected override ConnectionSettings? Settings { get; } = IntegrationEnv.Direct(
        DirectProvider.Claude, "AETHERGLOT_CLAUDE_KEY", "AETHERGLOT_CLAUDE_MODEL", "AETHERGLOT_CLAUDE_ENDPOINT",
        "https://api.anthropic.com/v1/messages");

    protected override string SkipReason => "Set AETHERGLOT_CLAUDE_KEY and AETHERGLOT_CLAUDE_MODEL to run Claude integration tests.";
}

public sealed class GeminiIntegrationTests(ITestOutputHelper output) : PipelineIntegrationTestsBase(output)
{
    protected override ConnectionSettings? Settings { get; } = IntegrationEnv.Direct(
        DirectProvider.Gemini, "AETHERGLOT_GEMINI_KEY", "AETHERGLOT_GEMINI_MODEL", "AETHERGLOT_GEMINI_ENDPOINT",
        "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");

    protected override string SkipReason => "Set AETHERGLOT_GEMINI_KEY and AETHERGLOT_GEMINI_MODEL to run Gemini integration tests.";
}
