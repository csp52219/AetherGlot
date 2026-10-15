using AetherGlot.Core.Connection;
using AetherGlot.Core.Translation;
using AetherGlot.Providers;

namespace AetherGlot.Tests.Support;

internal static class TestSettings
{
    public const string VirtualKey   = "sk-litellm-virtual-key-1234";
    public const string CfId         = "cf-client-id-abcdef.access";
    public const string CfSecret     = "cf-client-secret-0123456789";

    public static ConnectionSettings LiteLlm(bool cf = true, bool json = true) => new()
    {
        Mode                    = ConnectionMode.LiteLlm,
        BaseUrl                 = "https://llm.example.test",
        ApiKey                  = VirtualKey,
        UserTag                 = "aetherglot-test",
        CloudflareAccessEnabled = cf,
        CloudflareClientId      = CfId,
        CloudflareClientSecret  = CfSecret,
        InboundModel            = "fast-model",
        OutboundModel           = "good-model",
        UseJsonResponseFormat   = json,
    };

    public static (TranslationPipeline Pipeline, FakeHttpHandler Handler, ModelCapabilityStore Caps) Pipeline(
        Func<CapturedRequest, int, HttpResponseMessage> respond, int cacheSize = 100)
    {
        var handler = new FakeHttpHandler(respond);
        var sender  = new LlmHttpSender(new HttpClient(handler));
        var caps    = new ModelCapabilityStore();
        var pipe    = new TranslationPipeline(new TranslatorProviderFactory(sender), new PromptBuilder(), caps,
                                              new TranslationCache<InboundOutcome>(cacheSize));
        return (pipe, handler, caps);
    }

    public static InboundRequest Inbound(string text, Dictionary<string, string>? at = null) =>
        new(text, at ?? new Dictionary<string, string>(), "Japanese", "ja");
}
