using System.Text.Json;
using AetherGlot.Core.Connection;
using AetherGlot.Providers;
using AetherGlot.Tests.Support;
using Xunit;

namespace AetherGlot.Tests.Connection;

public sealed class LiteLlmProviderTests
{
    [Fact]
    public async Task Completion_SendsBearer_CfHeaders_UserTag_AndResponseFormat()
    {
        var handler  = new FakeHttpHandler(_ => FakeHttpHandler.Completion("{\"ok\":true}", "fast-model"));
        var settings = new ConnectionSettings
        {
            Mode = ConnectionMode.LiteLlm, BaseUrl = "https://llm.example.test/", ApiKey = TestSettings.VirtualKey,
            UserTag = "aetherglot", CloudflareAccessEnabled = true,
            CloudflareClientId = TestSettings.CfId, CloudflareClientSecret = TestSettings.CfSecret,
            ExtraHeaders = [new("X-Team", "ffxiv")],
        };
        var provider = new LiteLlmProvider(new LlmHttpSender(new HttpClient(handler)), settings);

        var response = await provider.CompleteAsync(new LlmRequest("fast-model", "sys", "usr", JsonMode: true, MaxTokens: 64));

        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://llm.example.test/v1/chat/completions", req.Uri.ToString());
        Assert.Equal($"Bearer {TestSettings.VirtualKey}", req.Headers["Authorization"]);
        Assert.Equal(TestSettings.CfId, req.Headers["CF-Access-Client-Id"]);
        Assert.Equal(TestSettings.CfSecret, req.Headers["CF-Access-Client-Secret"]);
        Assert.Equal("ffxiv", req.Headers["X-Team"]);

        using var body = JsonDocument.Parse(req.Body);
        Assert.Equal("fast-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("aetherglot", body.RootElement.GetProperty("user").GetString());
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());

        Assert.Equal("{\"ok\":true}", response.Content);
        Assert.Equal(10, response.PromptTokens);
        Assert.Equal(5, response.CompletionTokens);
        Assert.True(response.JsonModeUsed);
    }

    [Fact]
    public async Task Completion_WithoutJsonMode_OmitsResponseFormat_AndCfHeadersWhenDisabled()
    {
        var handler  = new FakeHttpHandler(_ => FakeHttpHandler.Completion("x"));
        var provider = new LiteLlmProvider(new LlmHttpSender(new HttpClient(handler)), TestSettings.LiteLlm(cf: false));

        await provider.CompleteAsync(new LlmRequest("fast-model", "s", "u", JsonMode: false));

        var req = handler.Requests[0];
        Assert.False(req.Headers.ContainsKey("CF-Access-Client-Id"));
        using var body = JsonDocument.Parse(req.Body);
        Assert.False(body.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task ListModels_UsesV1Models_AndSendsAccessHeaders()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"data":[{"id":"good-model"},{"id":"fast-model"},{"id":"fast-model"}],"object":"list"}"""));
        var provider = new LiteLlmProvider(new LlmHttpSender(new HttpClient(handler)), TestSettings.LiteLlm());

        var models = await provider.ListModelsAsync();

        Assert.Equal(["fast-model", "good-model"], models);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal("https://llm.example.test/v1/models", req.Uri.ToString());
        Assert.Equal(TestSettings.CfSecret, req.Headers["CF-Access-Client-Secret"]);
    }

    [Theory]
    [InlineData("https://llm.example.test", "https://llm.example.test/v1")]
    [InlineData("https://llm.example.test/", "https://llm.example.test/v1")]
    [InlineData("https://llm.example.test/v1/", "https://llm.example.test/v1")]
    [InlineData("https://llm.example.test/v1/chat/completions", "https://llm.example.test/v1")]
    [InlineData("https://llm.example.test/proxy", "https://llm.example.test/proxy/v1")]
    public void BaseUrl_IsNormalised(string input, string expected)
        => Assert.Equal(expected, EndpointResolver.NormalizeV1Base(input));
}
