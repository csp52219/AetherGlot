using AetherGlot.Core.Glossary;
using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Glossary;

public sealed class BrowserAndChatTextTests
{
    private static List<TermEntry> Entries() =>
    [
        TermEntry.Simple("action:1", TermKind.Action, TermSource.GameData, "Bio", "バイオ"),
        new TermEntry { Id = "at:56:1", Kind = TermKind.AutoTranslate, Source = TermSource.GameData, Senses = [new TermSense { En = "Bioblaster", Ja = "バイオブラスト" }] },
        .. GlossaryTestData.Community(),
        TermEntry.Simple("user:zorp", TermKind.Slang, TermSource.User, "zorp strat", "ゾープ作戦"),
    ];

    [Fact]
    public void Buckets_AreCounted()
    {
        var counts = GlossaryBrowser.Counts(Entries());
        Assert.Equal(1, counts[GlossaryBucket.GameNames]);
        Assert.Equal(1, counts[GlossaryBucket.AutoTranslate]);
        Assert.Equal(1, counts[GlossaryBucket.Mine]);
        Assert.True(counts[GlossaryBucket.Community] >= 40);
        Assert.Equal(counts[GlossaryBucket.All], Entries().Count);
    }

    [Fact]
    public void Search_RanksExactThenPrefixThenContains_AndFiltersBucket()
    {
        var all = GlossaryBrowser.Search(Entries(), "bio", GlossaryBucket.All);
        Assert.Equal("bio", all[0].Id == "bio" ? "bio" : all[0].En.ToLowerInvariant()); // exact first
        Assert.Contains(all, e => e.Id == "at:56:1");                                   // prefix "Bioblaster"

        var game = GlossaryBrowser.Search(Entries(), "bio", GlossaryBucket.GameNames);
        Assert.Equal("action:1", Assert.Single(game).Id);

        Assert.Single(GlossaryBrowser.Search(Entries(), "ゾープ", GlossaryBucket.Mine));
        Assert.Contains(GlossaryBrowser.Search(Entries(), "opo-opo", GlossaryBucket.Community), e => e.Id == "opo-opo-form");
        Assert.Equal(Entries().Count, GlossaryBrowser.Search(Entries(), "", GlossaryBucket.All).Count);
    }

    [Fact]
    public void RuleCount_AndListParsing()
    {
        var bio = GlossaryTestData.Community().Single(e => e.Id == "bio");
        Assert.Equal(5, GlossaryBrowser.RuleCount(bio)); // 3 rules + 2 rules
        Assert.Equal(["dot", "reapply", "fell off"], GlossaryBrowser.ParseList(" dot, reapply ,fell off,dot,"));
        Assert.Equal(["学者", "SCH"], GlossaryBrowser.ParseList("学者、SCH"));
        Assert.Equal("a, b", GlossaryBrowser.FormatList(["a", "b"]));
    }

    [Fact]
    public void CopyAsUser_IsDeep()
    {
        var bio  = GlossaryTestData.Community().Single(e => e.Id == "bio");
        var copy = GlossaryBrowser.CopyAsUser(bio);
        copy.Senses[0].Rules.Words.Add("new");
        Assert.Equal(TermSource.User, copy.Source);
        Assert.Equal("user:bio", copy.Id);
        Assert.DoesNotContain("new", bio.Senses[0].Rules.Words);
    }

    [Theory]
    [InlineData("Stack for the next mechanic.\nI'll cast Medica.", "Stack for the next mechanic. I'll cast Medica.", false)]
    [InlineData("  a\t\tb  ", "a b", false)]
    [InlineData("x\u0002y", "x y", false)]
    public void ChatText_IsOneCleanLine(string input, string expected, bool truncated)
    {
        var (text, cut) = ChatText.Sanitize(input);
        Assert.Equal(expected, text);
        Assert.Equal(truncated, cut);
    }

    [Fact]
    public void ChatText_IsCutAt500BytesWithoutSplittingCharacters()
    {
        var (text, cut) = ChatText.Sanitize(new string('あ', 200)); // 600 bytes
        Assert.True(cut);
        Assert.Equal(166, text.Length);                           // 498 bytes
        Assert.True(ChatText.ByteCount(text) <= ChatText.MaxBytes);
    }
}
