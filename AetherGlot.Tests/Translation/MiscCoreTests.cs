using AetherGlot.Core.Connection;
using AetherGlot.Core.Security;
using AetherGlot.Core.Translation;
using AetherGlot.Tests.Support;
using Xunit;

namespace AetherGlot.Tests.Translation;

public sealed class MiscCoreTests
{
    [Fact]
    public void Cache_EvictsLeastRecentlyUsed()
    {
        var cache = new TranslationCache<int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryGet("a", out _)); // a becomes most recent
        cache.Set("c", 3);

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Cache_ShrinksWhenCapacityLowered()
    {
        var cache = new TranslationCache<int>(3);
        cache.Set("a", 1); cache.Set("b", 2); cache.Set("c", 3);
        cache.Capacity = 1;
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Redactor_MasksAllSecrets()
    {
        var s = TestSettings.LiteLlm();
        var text = $"key={TestSettings.VirtualKey} id={TestSettings.CfId} secret={TestSettings.CfSecret}";
        var redacted = SecretRedactor.Redact(text, s.SecretValues());
        Assert.DoesNotContain(TestSettings.VirtualKey, redacted);
        Assert.DoesNotContain(TestSettings.CfId, redacted);
        Assert.DoesNotContain(TestSettings.CfSecret, redacted);
        Assert.Contains(SecretRedactor.Mask, redacted);
    }

    [Theory]
    [InlineData("sk-1234567890abcdef", "sk-••••cdef")]
    [InlineData("short", "••••")]
    [InlineData("", "")]
    public void Redactor_DisplayMask(string secret, string expected)
        => Assert.Equal(expected, SecretRedactor.MaskForDisplay(secret));

    [Fact]
    public void ConnectionSettings_ToString_HasNoSecrets()
    {
        var s = TestSettings.LiteLlm().ToString();
        Assert.DoesNotContain(TestSettings.VirtualKey, s);
        Assert.DoesNotContain(TestSettings.CfSecret, s);
    }

    [Fact]
    public void LegacyMigration_MovesKeys_AndClearsDictionary()
    {
        var legacy = new Dictionary<string, string> { ["OpenAI"] = "sk-openai", ["Claude"] = "sk-ant", ["Custom"] = "local", ["Gemini"] = "" };
        var store  = new InMemorySecretStore();

        var count = LegacySecretsMigrator.Migrate(legacy, store);

        Assert.Equal(3, count);
        Assert.Empty(legacy);
        Assert.Equal("sk-openai", store.Get(SecretNames.DirectApiKey("OpenAI")));
        Assert.Equal("sk-ant", store.Get(SecretNames.DirectApiKey("Claude")));
        Assert.Equal("local", store.Get(SecretNames.OpenAICompatibleKey));
    }

    [Fact]
    public void LegacyMigration_KeepsKey_WhenStoreFails()
    {
        var legacy = new Dictionary<string, string> { ["OpenAI"] = "sk-openai" };

        var count = LegacySecretsMigrator.Migrate(legacy, new BrokenStore());

        Assert.Equal(0, count);
        Assert.Equal("sk-openai", legacy["OpenAI"]);
    }

    [Fact]
    public void LegacyMigration_DoesNotOverwriteNewerKey()
    {
        var store = new InMemorySecretStore();
        store.Set(SecretNames.DirectApiKey("OpenAI"), "sk-new");
        var legacy = new Dictionary<string, string> { ["OpenAI"] = "sk-old" };

        LegacySecretsMigrator.Migrate(legacy, store);

        Assert.Equal("sk-new", store.Get(SecretNames.DirectApiKey("OpenAI")));
        Assert.Empty(legacy);
    }

    [Fact]
    public void CapabilityStore_NotifiesOnlyOnChange()
    {
        var calls = 0;
        var store = new ModelCapabilityStore(new Dictionary<string, bool>(), () => calls++);
        store.SetJsonModeSupport("m", false);
        store.SetJsonModeSupport("m", false);
        store.SetJsonModeSupport("m", true);
        Assert.Equal(2, calls);
        Assert.True(store.GetJsonModeSupport("m"));
        Assert.Null(store.GetJsonModeSupport("other"));
    }

    [Fact]
    public void SensitiveHeaders_AreRecognised()
    {
        Assert.True(RequestDecorator.IsSensitiveHeader("authorization"));
        Assert.True(RequestDecorator.IsSensitiveHeader("CF-Access-Client-Secret"));
        Assert.False(RequestDecorator.IsSensitiveHeader("Content-Type"));
    }

    private sealed class BrokenStore : ISecretStore
    {
        public string? Get(string name) => null;
        public void Set(string name, string? value) { }
        public void Delete(string name) { }
    }
}
