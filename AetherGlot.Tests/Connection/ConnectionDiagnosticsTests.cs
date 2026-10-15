using System.Net;
using AetherGlot.Core.Connection;
using AetherGlot.Core.Translation;
using AetherGlot.Providers;
using AetherGlot.Tests.Support;
using Xunit;

namespace AetherGlot.Tests.Connection;

public sealed class ConnectionDiagnosticsTests
{
    private static (ConnectionDiagnostics Diag, FakeHttpHandler Handler, ModelCapabilityStore Caps) Create(
        Func<CapturedRequest, HttpResponseMessage> respond)
    {
        var handler = new FakeHttpHandler(respond);
        var sender  = new LlmHttpSender(new HttpClient(handler));
        var caps    = new ModelCapabilityStore();
        return (new ConnectionDiagnostics(sender, new TranslatorProviderFactory(sender), caps), handler, caps);
    }

    [Fact]
    public async Task AllStagesPass()
    {
        var (diag, handler, _) = Create(r => r.Uri.AbsolutePath switch
        {
            "/health/liveliness"   => FakeHttpHandler.Json("\"I'm alive!\""),
            "/v1/models"           => FakeHttpHandler.Json("""{"data":[{"id":"fast-model"},{"id":"good-model"}]}"""),
            "/v1/chat/completions" => FakeHttpHandler.Completion("{\"ok\":true}"),
            _                      => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var report = await diag.RunAsync(TestSettings.LiteLlm());

        Assert.True(report.AllPassed);
        Assert.Equal(
            [DiagnosticStepKind.CloudflareAccess, DiagnosticStepKind.Authentication, DiagnosticStepKind.ModelList, DiagnosticStepKind.JsonInbound, DiagnosticStepKind.JsonOutbound],
            report.Steps.Select(s => s.Kind));
        Assert.All(report.Steps, s => Assert.Equal(DiagnosticStatus.Ok, s.Status));
        Assert.Equal(2, report.Models.Count);
        Assert.All(handler.Requests, r => Assert.Equal(TestSettings.CfId, r.Headers["CF-Access-Client-Id"]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AccessRejection_StopsEarly_WithServiceAuthHint(bool redirect)
    {
        var (diag, handler, _) = Create(_ => redirect
            ? FakeHttpHandler.Redirect("https://team.cloudflareaccess.com/cdn-cgi/access/login/x")
            : FakeHttpHandler.Html(HttpStatusCode.Forbidden));

        var report = await diag.RunAsync(TestSettings.LiteLlm());

        var access = report.Steps[0];
        Assert.Equal(DiagnosticStatus.Failed, access.Status);
        Assert.Equal(ConnectionDiagnostics.HintAccess, access.Hint);
        Assert.Equal(redirect ? 302 : 403, access.StatusCode);
        Assert.All(report.Steps.Skip(1), s => Assert.Equal(DiagnosticStatus.Skipped, s.Status));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task OriginAnswering404_MeansAccessPassed()
    {
        var (diag, _, _) = Create(r => r.Uri.AbsolutePath switch
        {
            "/health/liveliness" => FakeHttpHandler.Json("""{"detail":"Not Found"}""", HttpStatusCode.NotFound),
            "/v1/models"         => FakeHttpHandler.Json("""{"error":"bad key"}""", HttpStatusCode.Unauthorized),
            _                    => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var report = await diag.RunAsync(TestSettings.LiteLlm());

        Assert.Equal(DiagnosticStatus.Ok, report.Steps[0].Status);
        var auth = report.Steps[1];
        Assert.Equal(DiagnosticStatus.Failed, auth.Status);
        Assert.Equal(401, auth.StatusCode);
        Assert.Equal(ConnectionDiagnostics.HintUnauthorized, auth.Hint);
    }

    [Fact]
    public async Task ModelNotListed_IsWarning_AndJsonModeRejection_RetriesWithoutResponseFormat()
    {
        var (diag, _, caps) = Create(r =>
        {
            if (r.Uri.AbsolutePath == "/health/liveliness") return FakeHttpHandler.Json("{}");
            if (r.Uri.AbsolutePath == "/v1/models") return FakeHttpHandler.Json("""{"data":[{"id":"fast-model"}]}""");
            return r.Body.Contains("response_format")
                ? FakeHttpHandler.Json("""{"error":"does not support response_format"}""", HttpStatusCode.BadRequest)
                : FakeHttpHandler.Completion("{\"ok\":true}");
        });

        var report = await diag.RunAsync(TestSettings.LiteLlm());

        Assert.Equal(DiagnosticStatus.Warning, report.Steps.Single(s => s.Kind == DiagnosticStepKind.ModelList).Status);
        Assert.Equal(DiagnosticStatus.Warning, report.Steps.Single(s => s.Kind == DiagnosticStepKind.JsonInbound).Status);
        Assert.False(caps.GetJsonModeSupport("fast-model"));
        Assert.True(report.AllPassed);
    }

    [Fact]
    public async Task CfDisabled_SkipsAccessStage()
    {
        var (diag, _, _) = Create(r => r.Uri.AbsolutePath == "/v1/models"
            ? FakeHttpHandler.Json("""{"data":[{"id":"fast-model"},{"id":"good-model"}]}""")
            : FakeHttpHandler.Completion("{\"ok\":true}"));

        var report = await diag.RunAsync(TestSettings.LiteLlm(cf: false));

        Assert.Equal(DiagnosticStatus.Skipped, report.Steps[0].Status);
        Assert.True(report.AllPassed);
    }
}
