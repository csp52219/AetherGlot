using AetherGlot.Core.Connection;
using AetherGlot.Core.Security;
using Xunit;

namespace AetherGlot.Tests.Connection;

public sealed class ConnectionSettingsBuilderTests
{
    private static InMemorySecretStore Secrets()
    {
        var s = new InMemorySecretStore();
        s.Set(SecretNames.LiteLlmKey, "sk-virtual");
        s.Set(SecretNames.CfClientId, "id.access");
        s.Set(SecretNames.CfClientSecret, "cf-secret");
        s.Set(SecretNames.DirectApiKey("Claude"), "sk-ant");
        s.Set(SecretNames.ExtraHeader("X-Team"), "ffxiv");
        return s;
    }

    [Fact]
    public void LiteLlm_UsesVirtualKey_CfToken_AndExtraHeaders()
    {
        var cfg = new ConnectionConfig
        {
            Mode = ConnectionMode.LiteLlm, LiteLlmBaseUrl = "https://llm.example.test", CloudflareAccessEnabled = true,
            ExtraHeaderNames = ["X-Team", "x-team", " "], InboundModel = "fast", OutboundModel = "",
        };

        var s = ConnectionSettingsBuilder.Build(cfg, Secrets());

        Assert.Equal("sk-virtual", s.ApiKey);
        Assert.True(s.CloudflareAccessEnabled);
        Assert.Equal("cf-secret", s.CloudflareClientSecret);
        Assert.Equal([new KeyValuePair<string, string>("X-Team", "ffxiv")], s.ExtraHeaders);
        Assert.Equal("fast", s.OutboundModel); // falls back to inbound model
        Assert.Equal("aetherglot", s.UserTag);
        Assert.Null(ConnectionSettingsBuilder.MissingRequirement(s));
    }

    [Fact]
    public void Direct_UsesVendorKeyAndEndpoint_AndNeverSendsCfHeaders()
    {
        var cfg = new ConnectionConfig
        {
            Mode = ConnectionMode.Direct, DirectProvider = DirectProvider.Claude, CloudflareAccessEnabled = true,
            ExtraHeaderNames = ["X-Team"], InboundModel = "m",
        };

        var s = ConnectionSettingsBuilder.Build(cfg, Secrets());

        Assert.Equal("sk-ant", s.ApiKey);
        Assert.Equal(cfg.DirectEndpoints["Claude"], s.BaseUrl);
        Assert.False(s.CloudflareAccessEnabled);
        Assert.Empty(s.CloudflareClientSecret);
        Assert.Empty(s.ExtraHeaders);
        Assert.Empty(s.UserTag);
    }

    [Fact]
    public void MissingRequirement_ReportsIncompleteCfToken()
    {
        var store = new InMemorySecretStore();
        store.Set(SecretNames.LiteLlmKey, "k");
        var cfg = new ConnectionConfig { Mode = ConnectionMode.LiteLlm, LiteLlmBaseUrl = "https://x", CloudflareAccessEnabled = true, InboundModel = "m" };

        Assert.Contains("Cloudflare Access", ConnectionSettingsBuilder.MissingRequirement(ConnectionSettingsBuilder.Build(cfg, store)));
    }

    [Fact]
    public void CachingStore_ReadsThroughOnce_AndInvalidatesOnWrite()
    {
        var inner = new CountingStore();
        var store = new CachingSecretStore(inner);

        store.Get("a"); store.Get("a");
        Assert.Equal(1, inner.Reads);

        store.Set("a", "v");
        Assert.Equal("v", store.Get("a"));
        Assert.Equal(2, inner.Reads);
    }

    private sealed class CountingStore : ISecretStore
    {
        private readonly InMemorySecretStore s = new();
        public int Reads { get; private set; }
        public string? Get(string name) { Reads++; return s.Get(name); }
        public void Set(string name, string? value) => s.Set(name, value);
        public void Delete(string name) => s.Delete(name);
    }
}
