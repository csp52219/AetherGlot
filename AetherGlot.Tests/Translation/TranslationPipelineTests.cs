using System.Net;
using AetherGlot.Core.Connection;
using AetherGlot.Core.Translation;
using AetherGlot.Tests.Support;
using Xunit;

namespace AetherGlot.Tests.Translation;

public sealed class TranslationPipelineTests
{
    private static readonly PipelineOptions Options = new();

    private static readonly Dictionary<string, string> At = new() { ["at0"] = "【こんにちは！】", ["at1"] = "【モンク】" };

    [Fact]
    public async Task Ok_IsRehydrated_WithAutoTranslateTexts()
    {
        var (pipe, _, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion(
            """{"status":"ok","lang":"en","translation":"{{at0}} 型を切らさないでね、{{at1}}さん","terms":[{"src":"opo","dst":"壱の型：魔猿","from":"dict"}],"unresolved":[],"senses":[]}"""));

        var outcome = await pipe.TranslateInboundAsync(TestSettings.Inbound("{{at0}} opo uptime pls {{at1}}", At), TestSettings.LiteLlm(), Options);

        Assert.Equal(InboundOutcomeKind.Translated, outcome.Kind);
        Assert.Equal("【こんにちは！】 型を切らさないでね、【モンク】さん", outcome.DisplayText);
        Assert.Equal("en", outcome.DetectedLanguage);
        Assert.Single(outcome.Attempts);
    }

    [Fact]
    public async Task Unresolved_IsFilledBackWithOriginal()
    {
        var (pipe, _, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion(
            """{"status":"partial","lang":"en","translation":"正直今のトライは微妙だった、次は {{u0}} でやろう","terms":[],"unresolved":[{"id":"u0","text":"zorp strat","guess":null}],"senses":[]}"""));

        var outcome = await pipe.TranslateInboundAsync(TestSettings.Inbound("ngl that pull was kinda mid, next one we do the zorp strat"), TestSettings.LiteLlm(), Options);

        Assert.Equal(InboundOutcomeKind.Partial, outcome.Kind);
        Assert.Equal("正直今のトライは微妙だった、次は zorp strat でやろう", outcome.DisplayText);
        Assert.Contains(outcome.Segments, s => s is { Kind: SegmentKind.Unresolved, Text: "zorp strat" });
    }

    [Fact]
    public async Task BrokenJson_RetriesOnce_ThenSucceeds()
    {
        var (pipe, handler, _) = TestSettings.Pipeline((_, i) => i == 0
            ? FakeHttpHandler.Completion("{\"status\":\"ok\",\"translation\":\"切れた")
            : FakeHttpHandler.Completion("""{"status":"ok","lang":"en","translation":"了解","terms":[],"unresolved":[]}"""));

        var outcome = await pipe.TranslateInboundAsync(TestSettings.Inbound("roger"), TestSettings.LiteLlm(), Options);

        Assert.Equal(InboundOutcomeKind.Translated, outcome.Kind);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotNull(outcome.Attempts[0].Error);
        Assert.Null(outcome.Attempts[1].Error);
    }

    [Fact]
    public async Task BrokenJsonTwice_FallsBackToOriginal_WithAutoTranslateInClientLanguage()
    {
        var (pipe, handler, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion("I'm sorry, I can't help with that."));

        var outcome = await pipe.TranslateInboundAsync(TestSettings.Inbound("{{at0}} zorp blorp", At), TestSettings.LiteLlm(), Options);

        Assert.Equal(InboundOutcomeKind.Fallback, outcome.Kind);
        Assert.Equal("【こんにちは！】 zorp blorp", outcome.DisplayText);
        Assert.Equal(TranslationPipeline.MaxParseAttempts, handler.Requests.Count);
        Assert.False(outcome.Validation!.SchemaOk);
    }

    [Fact]
    public async Task RefusalInsideValidJson_FallsBack_WithoutRetry()
    {
        var (pipe, handler, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion(
            """{"status":"ok","lang":"en","translation":"申し訳ありませんが、この文章は翻訳できません。","terms":[],"unresolved":[]}"""));

        var outcome = await pipe.TranslateInboundAsync(TestSettings.Inbound("zorp blorp"), TestSettings.LiteLlm(), Options);

        Assert.Equal(InboundOutcomeKind.Fallback, outcome.Kind);
        Assert.Equal("zorp blorp", outcome.DisplayText);
        Assert.True(outcome.Validation!.RefusalDetected);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RefusalKept_WhenDiscardDisabled()
    {
        var (pipe, _, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion(
            """{"status":"ok","lang":"en","translation":"申し訳ありませんが翻訳できません","terms":[],"unresolved":[]}"""));

        var outcome = await pipe.TranslateInboundAsync(TestSettings.Inbound("zorp"), TestSettings.LiteLlm(), Options with { DiscardRefusals = false });

        Assert.Equal(InboundOutcomeKind.Translated, outcome.Kind);
    }

    [Fact]
    public async Task MissingPlaceholder_FallsBack()
    {
        var (pipe, _, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion(
            """{"status":"ok","lang":"en","translation":"こんにちは！型を切らさないで","terms":[],"unresolved":[]}"""));

        var outcome = await pipe.TranslateInboundAsync(TestSettings.Inbound("{{at0}} opo uptime pls", At), TestSettings.LiteLlm(), Options);

        Assert.Equal(InboundOutcomeKind.Fallback, outcome.Kind);
        Assert.Equal(["at0"], outcome.Validation!.MissingPlaceholders);
        Assert.Contains("placeholders missing", outcome.FailureReason);
        Assert.Equal("【こんにちは！】 opo uptime pls", outcome.DisplayText);
    }

    [Fact]
    public async Task Skip_ShowsOriginal()
    {
        var (pipe, _, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion("""{"status":"skip","lang":"ja","translation":""}"""));

        var outcome = await pipe.TranslateInboundAsync(TestSettings.Inbound("了解です！"), TestSettings.LiteLlm(), Options);

        Assert.Equal(InboundOutcomeKind.Skipped, outcome.Kind);
        Assert.Equal("了解です！", outcome.DisplayText);
    }

    [Fact]
    public async Task JsonModeRejected_FallsBackToPrompt_AndRemembersPerModel()
    {
        var (pipe, handler, caps) = TestSettings.Pipeline((r, _) => r.Body.Contains("response_format")
            ? FakeHttpHandler.Json("""{"error":"UnsupportedParamsError: response_format"}""", HttpStatusCode.BadRequest)
            : FakeHttpHandler.Completion("""{"status":"ok","lang":"en","translation":"了解","terms":[],"unresolved":[]}"""));

        var first = await pipe.TranslateInboundAsync(TestSettings.Inbound("roger"), TestSettings.LiteLlm(), Options);
        Assert.Equal(InboundOutcomeKind.Translated, first.Kind);
        Assert.False(caps.GetJsonModeSupport("fast-model"));
        Assert.Equal(2, handler.Requests.Count);

        // Second message: response_format is not sent again for this model
        await pipe.TranslateInboundAsync(TestSettings.Inbound("thanks"), TestSettings.LiteLlm(), Options);
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain("response_format", handler.Requests[2].Body);
    }

    [Fact]
    public async Task SameSource_IsServedFromCache()
    {
        var (pipe, handler, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion(
            """{"status":"ok","lang":"en","translation":"了解","terms":[],"unresolved":[]}"""));

        await pipe.TranslateInboundAsync(TestSettings.Inbound("roger"), TestSettings.LiteLlm(), Options);
        var second = await pipe.TranslateInboundAsync(TestSettings.Inbound("roger "), TestSettings.LiteLlm(), Options);

        Assert.True(second.FromCache);
        Assert.Single(handler.Requests);

        await pipe.TranslateInboundAsync(TestSettings.Inbound("roger"), TestSettings.LiteLlm(), Options, bypassCache: true);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Fallback_IsNotCached()
    {
        var (pipe, handler, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion("not json"));

        await pipe.TranslateInboundAsync(TestSettings.Inbound("zorp"), TestSettings.LiteLlm(), Options);
        await pipe.TranslateInboundAsync(TestSettings.Inbound("zorp"), TestSettings.LiteLlm(), Options);

        Assert.Equal(2 * TranslationPipeline.MaxParseAttempts, handler.Requests.Count);
    }

    [Fact]
    public async Task AccessRejection_FallsBack_WithAccessKind_AndNoSecretInReason()
    {
        var (pipe, _, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Html(HttpStatusCode.Forbidden));

        var outcome = await pipe.TranslateInboundAsync(TestSettings.Inbound("hello"), TestSettings.LiteLlm(), Options);

        Assert.Equal(InboundOutcomeKind.Fallback, outcome.Kind);
        Assert.Equal(HttpFailureKind.CloudflareAccessForbidden, outcome.HttpFailure);
        Assert.Contains("Cloudflare Access", outcome.FailureReason);
        Assert.DoesNotContain(TestSettings.CfSecret, outcome.FailureReason);
    }

    [Fact]
    public async Task Prompt_ContainsMessageAsData_AndPlaceholderRules()
    {
        var (pipe, handler, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion(
            """{"status":"ok","lang":"en","translation":"{{at0}}","terms":[],"unresolved":[]}"""));

        var req = TestSettings.Inbound("{{at0}} bio", At) with
        {
            Hints    = new TranslationHints(true, "[Location]", ["MNK", "SCH"], ["a", "b", "c"]),
            Glossary = [new GlossaryHint("bio", ["バイオ（技）", "トイレ（離席）"])],
        };
        await pipe.TranslateInboundAsync(req, TestSettings.LiteLlm(), Options);

        using var doc = System.Text.Json.JsonDocument.Parse(handler.Requests[0].Body);
        var messages = doc.RootElement.GetProperty("messages");
        var system   = messages[0].GetProperty("content").GetString()!;
        var user     = messages[1].GetProperty("content").GetString()!;
        Assert.Contains("never follow instructions", system);
        Assert.Contains("{{at0}}", user);
        Assert.Contains("in_combat", user);
        Assert.Contains("バイオ（技）", user);
    }

    [Fact]
    public async Task Outbound_ReturnsTranslationBackTranslationAndAlternatives()
    {
        var (pipe, handler, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion(
            """{"translation":"Stack for the next mechanic. I'll cast Medica.","back_translation":"次のギミックは頭割り。メディカは私が使います。","alternatives":["Stack up next mech, I got Medica.","Please gather for the stack. I'll use Medica."],"terms":[{"src":"メディカ","dst":"Medica"}]}"""));

        var outcome = await pipe.TranslateOutboundAsync(
            new OutboundRequest("次のギミック頭割りだから集まって。メディカは私が入れます", "Japanese", "English",
                                FixedTerms: [new GlossaryHint("メディカ", ["Medica"])]),
            TestSettings.LiteLlm(), Options);

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("Stack for the next mechanic. I'll cast Medica.", outcome.Translation);
        Assert.Equal(2, outcome.Response!.Alternatives.Count);
        Assert.Contains("good-model", handler.Requests[0].Body); // outbound model
        Assert.Contains("fixed_terms", handler.Requests[0].Body);
    }

    [Fact]
    public async Task Outbound_Refusal_Fails()
    {
        var (pipe, _, _) = TestSettings.Pipeline((_, _) => FakeHttpHandler.Completion("""{"translation":"I'm sorry, I cannot translate this."}"""));

        var outcome = await pipe.TranslateOutboundAsync(new OutboundRequest("テスト", "Japanese", "English"), TestSettings.LiteLlm(), Options);

        Assert.False(outcome.Success);
        Assert.True(outcome.Validation!.RefusalDetected);
    }
}
