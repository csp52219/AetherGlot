using System.Net;
using AetherGlot.Core.Connection;
using AetherGlot.Providers;
using AetherGlot.Tests.Support;
using Xunit;

namespace AetherGlot.Tests.Connection;

public sealed class AccessDetectionTests
{
    private static LiteLlmProvider Provider(FakeHttpHandler handler, bool cf = true) =>
        new(new LlmHttpSender(new HttpClient(handler)), TestSettings.LiteLlm(cf));

    [Fact]
    public async Task Redirect_ToAccessLogin_IsReportedAsAccess()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Redirect("https://team.cloudflareaccess.com/cdn-cgi/access/login/llm.example.test"));
        var ex = await Assert.ThrowsAsync<LlmHttpException>(() => Provider(handler).ListModelsAsync());

        Assert.Equal(HttpFailureKind.CloudflareAccessRedirect, ex.Kind);
        Assert.True(ex.IsCloudflareAccess);
        Assert.Equal(HttpStatusCode.Found, ex.StatusCode);
        Assert.Contains("Cloudflare Access", ex.Message);
    }

    [Fact]
    public async Task Forbidden_HtmlPage_IsReportedAsAccess_NotParsedAsJson()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Html(HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<LlmHttpException>(() =>
            Provider(handler).CompleteAsync(new LlmRequest("fast-model", "s", "u", true)));

        Assert.Equal(HttpFailureKind.CloudflareAccessForbidden, ex.Kind);
        Assert.DoesNotContain("<html", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ok_WithHtmlBody_IsReportedAsAccess_WhenAccessEnabled()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Html(HttpStatusCode.OK));
        var ex = await Assert.ThrowsAsync<LlmHttpException>(() => Provider(handler).ListModelsAsync());
        Assert.Equal(HttpFailureKind.CloudflareAccessForbidden, ex.Kind);
    }

    [Fact]
    public async Task Forbidden_Json_FromLiteLlm_IsNotAccess()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"error":{"message":"key not allowed to access model fast-model"}}""", HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<LlmHttpException>(() =>
            Provider(handler).CompleteAsync(new LlmRequest("fast-model", "s", "u", false)));
        Assert.Equal(HttpFailureKind.ModelNotFound, ex.Kind);
        Assert.False(ex.IsCloudflareAccess);
    }

    [Fact]
    public async Task Unauthorized_IsReportedAsKeyProblem()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"error":{"message":"Authentication Error, Invalid proxy server token passed"}}""", HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<LlmHttpException>(() => Provider(handler).ListModelsAsync());
        Assert.Equal(HttpFailureKind.Unauthorized, ex.Kind);
    }

    [Fact]
    public async Task ResponseFormatRejection_IsDetected()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            """{"error":{"message":"litellm.UnsupportedParamsError: model does not support parameters: ['response_format']"}}""", HttpStatusCode.BadRequest));
        var ex = await Assert.ThrowsAsync<LlmHttpException>(() =>
            Provider(handler).CompleteAsync(new LlmRequest("fast-model", "s", "u", true)));
        Assert.Equal(HttpFailureKind.JsonModeNotSupported, ex.Kind);
    }

    [Fact]
    public async Task ErrorMessages_NeverContainSecretsOrBody()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            $$"""{"error":"bad key {{TestSettings.VirtualKey}}"}""", HttpStatusCode.InternalServerError));
        var ex = await Assert.ThrowsAsync<LlmHttpException>(() => Provider(handler).ListModelsAsync());
        Assert.DoesNotContain(TestSettings.VirtualKey, ex.Message);
        Assert.DoesNotContain(TestSettings.CfSecret, ex.ToString());
    }

    [Theory]
    [InlineData("https://team.cloudflareaccess.com/cdn-cgi/access/login/x", true)]
    [InlineData("https://llm.example.test/cdn-cgi/access/login", true)]
    [InlineData("https://llm.example.test/v1/models", false)]
    public void AccessLocation_IsRecognised(string url, bool expected)
        => Assert.Equal(expected, AccessResponseClassifier.IsCloudflareAccessLocation(new Uri(url)));
}
