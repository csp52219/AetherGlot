using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Core;

public sealed class OutboundPromptDefaultsTests
{
    [Fact]
    public void LegacyDefault_BecomesEmpty_EvenWithWindowsLineEndings()
    {
        Assert.Equal("", OutboundPromptDefaults.Migrate(OutboundPromptDefaults.LegacyDefault));
        Assert.Equal("", OutboundPromptDefaults.Migrate("  " + OutboundPromptDefaults.LegacyDefault.Replace("\n", "\r\n") + "\r\n"));
    }

    [Fact]
    public void EditedPrompt_IsKept()
    {
        var edited = OutboundPromptDefaults.LegacyDefault + "\n- Use British spelling.";
        Assert.Equal(edited, OutboundPromptDefaults.Migrate(edited));
        Assert.Equal("Always polite.", OutboundPromptDefaults.Migrate("Always polite."));
    }

    [Fact]
    public void Empty_StaysEmpty()
    {
        Assert.Equal("", OutboundPromptDefaults.Migrate(null));
        Assert.Equal("", OutboundPromptDefaults.Migrate("   "));
    }
}
