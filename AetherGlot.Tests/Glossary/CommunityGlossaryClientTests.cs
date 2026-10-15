using System.Net;
using AetherGlot.Core.Connection;
using AetherGlot.Core.Glossary;
using AetherGlot.Tests.Support;
using Xunit;

namespace AetherGlot.Tests.Glossary;

public sealed class CommunityGlossaryClientTests : IDisposable
{
    private const string Url = "https://raw.example-cdn.test/glossary.json";
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("ag-community-");
    private static readonly string Valid = File.ReadAllText(GlossaryTestData.BundledPath);

    private static HttpResponseMessage Ok(string body, string? etag = null)
    {
        var r = FakeHttpHandler.Json(body);
        if (etag is not null) r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return r;
    }

    [Fact]
    public async Task Download_SavesCache_ThenUsesEtag()
    {
        var handler = new FakeHttpHandler((r, i) => i == 0 ? Ok(Valid, "\"v1\"") : new HttpResponseMessage(HttpStatusCode.NotModified));
        var client  = new CommunityGlossaryClient(new HttpClient(handler));

        var first = await client.LoadAsync(Url, dir.FullName, null, null, download: true);
        Assert.Equal(GlossaryOrigin.Remote, first.Origin);
        Assert.True(first.Entries.Count >= 40);

        var second = await client.LoadAsync(Url, dir.FullName, null, null, download: true);
        Assert.Equal(GlossaryOrigin.NotModified, second.Origin);
        Assert.Equal("\"v1\"", handler.Requests[1].Headers["If-None-Match"]);
        Assert.Equal(first.Entries.Count, second.Entries.Count);
    }

    [Fact]
    public async Task Failure_FallsBackToCache_ThenBundled()
    {
        var ok      = new CommunityGlossaryClient(new HttpClient(new FakeHttpHandler(_ => Ok(Valid))));
        await ok.LoadAsync(Url, dir.FullName, null, null, download: true);

        var failing = new CommunityGlossaryClient(new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        var cached  = await failing.LoadAsync(Url, dir.FullName, GlossaryTestData.BundledPath, null, download: true);
        Assert.Equal(GlossaryOrigin.Cache, cached.Origin);
        Assert.Contains("404", cached.Error);

        var emptyDir = Directory.CreateTempSubdirectory("ag-community-empty-");
        var bundled  = await failing.LoadAsync(Url, emptyDir.FullName, GlossaryTestData.BundledPath, null, download: true);
        Assert.Equal(GlossaryOrigin.Bundled, bundled.Origin);
        Assert.True(bundled.Entries.Count >= 40);
        emptyDir.Delete(true);
    }

    [Fact]
    public async Task DownloadDisabled_UsesBundledWithoutNetwork()
    {
        var handler = new FakeHttpHandler(_ => throw new InvalidOperationException("no network expected"));
        var result  = await new CommunityGlossaryClient(new HttpClient(handler))
            .LoadAsync(Url, dir.FullName, GlossaryTestData.BundledPath, null, download: false);
        Assert.Equal(GlossaryOrigin.Bundled, result.Origin);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AccessHeaders_AreSentOnlyToTheLiteLlmHost()
    {
        var handler = new FakeHttpHandler(_ => Ok(Valid));
        var client  = new CommunityGlossaryClient(new HttpClient(handler));

        await client.LoadAsync(Url, dir.FullName, null, TestSettings.LiteLlm(), download: true);
        Assert.False(handler.Requests[0].Headers.ContainsKey("CF-Access-Client-Secret"));

        await client.LoadAsync("https://llm.example.test/glossary/community.json", dir.FullName, null, TestSettings.LiteLlm(), download: true);
        Assert.Equal(TestSettings.CfSecret, handler.Requests[1].Headers["CF-Access-Client-Secret"]);
    }

    [Fact]
    public async Task Redirect_IsFollowed_WithoutLeakingAccessHeaders()
    {
        var handler = new FakeHttpHandler((r, i) => i == 0
            ? FakeHttpHandler.Redirect("https://cdn.example.test/glossary.json", HttpStatusCode.MovedPermanently)
            : Ok(Valid));
        var client = new CommunityGlossaryClient(new HttpClient(handler));

        var result = await client.LoadAsync("https://llm.example.test/glossary.json", dir.FullName, null, TestSettings.LiteLlm(), download: true);

        Assert.Equal(GlossaryOrigin.Remote, result.Origin);
        Assert.True(handler.Requests[0].Headers.ContainsKey("CF-Access-Client-Id"));
        Assert.False(handler.Requests[1].Headers.ContainsKey("CF-Access-Client-Id"));
    }

    [Fact]
    public async Task HtmlPage_IsRejected_AndInvalidDocument_DoesNotReplaceCache()
    {
        var ok = new CommunityGlossaryClient(new HttpClient(new FakeHttpHandler(_ => Ok(Valid))));
        await ok.LoadAsync(Url, dir.FullName, null, null, download: true);

        var html = new CommunityGlossaryClient(new HttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Html(HttpStatusCode.OK))));
        var r1 = await html.LoadAsync(Url, dir.FullName, null, null, download: true);
        Assert.Equal(GlossaryOrigin.Cache, r1.Origin);

        var garbage = new CommunityGlossaryClient(new HttpClient(new FakeHttpHandler(_ => Ok("""{"schema":1,"entries":[]}"""))));
        var r2 = await garbage.LoadAsync(Url, dir.FullName, null, null, download: true);
        Assert.Equal(GlossaryOrigin.Cache, r2.Origin);
        Assert.True(r2.Entries.Count >= 40);
    }

    [Fact]
    public void SameOrigin_IsStrict()
    {
        var s = TestSettings.LiteLlm();
        Assert.True(CommunityGlossaryClient.SameOriginAsConnection(new Uri("https://llm.example.test/x.json"), s));
        Assert.False(CommunityGlossaryClient.SameOriginAsConnection(new Uri("https://llm.example.test.evil.test/x.json"), s));
        Assert.False(CommunityGlossaryClient.SameOriginAsConnection(new Uri("http://llm.example.test/x.json"), s));
        Assert.False(CommunityGlossaryClient.SameOriginAsConnection(new Uri("https://llm.example.test/x.json"), new ConnectionSettings { Mode = ConnectionMode.Direct, BaseUrl = "https://llm.example.test" }));
    }

    public void Dispose() => dir.Delete(true);
}
