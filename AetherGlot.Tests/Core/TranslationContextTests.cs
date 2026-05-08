using AetherGlot.Core;
using Xunit;

namespace AetherGlot.Tests.Core;

public sealed class TranslationContextTests
{
    [Fact]
    public void GetContextLanguage_NoHistory_ReturnsFallback()
    {
        var ctx = new TranslationContext();

        var lang = ctx.GetContextLanguage("Party", "EN");

        Assert.Equal("EN", lang);
    }

    [Fact]
    public void GetContextLanguage_AfterUpdate_ReturnsUpdated()
    {
        var ctx = new TranslationContext();
        ctx.UpdateDetectedLanguage("Party", "ja");

        var lang = ctx.GetContextLanguage("Party", "EN");

        // Should be normalised to uppercase
        Assert.Equal("JA", lang);
    }

    [Fact]
    public void GetContextLanguage_DifferentChannels_AreIndependent()
    {
        var ctx = new TranslationContext();
        ctx.UpdateDetectedLanguage("Party", "JA");
        ctx.UpdateDetectedLanguage("Say", "FR");

        Assert.Equal("JA", ctx.GetContextLanguage("Party", "EN"));
        Assert.Equal("FR", ctx.GetContextLanguage("Say",   "EN"));
        Assert.Equal("EN", ctx.GetContextLanguage("Shout", "EN")); // no history
    }

    [Fact]
    public void UpdateDetectedLanguage_OverwritesPreviousValue()
    {
        var ctx = new TranslationContext();
        ctx.UpdateDetectedLanguage("Party", "JA");
        ctx.UpdateDetectedLanguage("Party", "DE");

        Assert.Equal("DE", ctx.GetContextLanguage("Party", "EN"));
    }

    [Fact]
    public void UpdateDetectedLanguage_IgnoresBlankLanguage()
    {
        var ctx = new TranslationContext();
        ctx.UpdateDetectedLanguage("Party", "JA");
        ctx.UpdateDetectedLanguage("Party", "   ");  // blank — should not overwrite

        Assert.Equal("JA", ctx.GetContextLanguage("Party", "EN"));
    }

    [Fact]
    public void GetContextLanguage_KeyLookupIsCaseInsensitive()
    {
        var ctx = new TranslationContext();
        ctx.UpdateDetectedLanguage("party", "JA");

        // Keys should match regardless of case
        Assert.Equal("JA", ctx.GetContextLanguage("Party", "EN"));
        Assert.Equal("JA", ctx.GetContextLanguage("PARTY", "EN"));
    }
}
