using AetherGlot.Core.Connection;
using AetherGlot.Core.Glossary;
using Xunit;

namespace AetherGlot.Tests.Connection;

public sealed class RequestPreviewTests
{
    private static ConnectionSettings LiteLlm() => new()
    {
        Mode = ConnectionMode.LiteLlm,
        BaseUrl = "https://llm.example.com",
        ApiKey = "sk-virtual-123",
        CloudflareAccessEnabled = true,
        CloudflareClientId = "id.access",
        CloudflareClientSecret = "very-secret",
        ExtraHeaders = [new("X-Team", "team-token-xyz"), new("Authorization", "Basic abc")],
    };

    [Fact]
    public void Headers_NeverContainSecretValues()
    {
        var s = LiteLlm();
        var headers = RequestPreview.Headers(s);
        var all = string.Join("\n", headers.Select(h => h.Name + ": " + h.DisplayValue));

        foreach (var secret in s.SecretValues().Append("team-token-xyz").Append("Basic abc"))
            Assert.DoesNotContain(secret, all);
        Assert.Contains(headers, h => h.Name == RequestDecorator.CfClientIdHeader && h.DisplayValue == RequestPreview.Mask);
        Assert.Contains(headers, h => h.Name == RequestDecorator.CfClientSecretHeader && h.Sensitive);
        Assert.Contains(headers, h => h.Name == "X-Team" && h.DisplayValue == RequestPreview.Mask);
        // An extra header overrides the built-in one of the same name: listed once
        Assert.Single(headers, h => h.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Headers_FollowTheProvider()
    {
        var claude = new ConnectionSettings { Mode = ConnectionMode.Direct, DirectProvider = DirectProvider.Claude, ApiKey = "k" };
        Assert.Contains(RequestPreview.Headers(claude), h => h.Name == "x-api-key" && h.Sensitive);

        var gemini = new ConnectionSettings { Mode = ConnectionMode.Direct, DirectProvider = DirectProvider.Gemini, ApiKey = "k" };
        Assert.Contains(RequestPreview.Headers(gemini), h => h.Name == "x-goog-api-key");
        Assert.DoesNotContain(RequestPreview.Headers(gemini), h => h.Name == "Authorization");
    }

    [Fact]
    public void Endpoint_IsTheChatCompletionsUrl()
    {
        Assert.Equal("https://llm.example.com/v1/chat/completions", RequestPreview.Endpoint(LiteLlm()));
    }

    [Theory]
    [InlineData("@", AutoTranslateUsage.DisplayAsIs)]
    [InlineData("ClassJob[1-43]", AutoTranslateUsage.JobName)]
    [InlineData("PlaceName[20-37,39-59]", AutoTranslateUsage.PlaceName)]
    [InlineData("Action[9-9,15-17]", AutoTranslateUsage.ActionName)]
    [InlineData("GeneralAction[1-10]", AutoTranslateUsage.ActionName)]
    [InlineData("PetAction[1-6]", AutoTranslateUsage.ActionName)]
    [InlineData("Mount[1-1,4-6]", AutoTranslateUsage.DisplayAsIs)]
    [InlineData("Weather[col-1,1-20]", AutoTranslateUsage.DisplayAsIs)]
    [InlineData("", AutoTranslateUsage.DisplayAsIs)]
    public void AutoTranslateUsage_FollowsTheReferencedSheet(string table, AutoTranslateUsage expected)
    {
        Assert.Equal(expected, AutoTranslateUsages.Of(table));
    }
}
