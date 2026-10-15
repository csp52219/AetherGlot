using AetherGlot.Core.Display;
using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Display;

public sealed class BubbleBoardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 21, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<DisplaySegment> Seg(string text) => [new DisplaySegment(text, SegmentKind.Translated)];

    [Fact]
    public void Duration_GrowsWithLength_UpToDouble()
    {
        var s = new BubbleSettings { Seconds = 6, ExtendByLength = true };
        Assert.Equal(6, BubbleBoard.DurationFor("short", s).TotalSeconds);
        Assert.Equal(6 + 0.08 * 30, BubbleBoard.DurationFor(new string('a', 50), s).TotalSeconds, 3);
        Assert.Equal(12, BubbleBoard.DurationFor(new string('a', 500), s).TotalSeconds);
        Assert.Equal(6, BubbleBoard.DurationFor(new string('a', 500), new BubbleSettings { Seconds = 6, ExtendByLength = false }).TotalSeconds);
    }

    [Fact]
    public void Bubbles_FadeOverTheLastSecond_ThenDisappear()
    {
        var board = new BubbleBoard(() => new BubbleSettings { Seconds = 6, ExtendByLength = false });
        board.Add("Kaela Rhyne", Seg("ok"), T0);

        Assert.Equal(1f, Assert.Single(board.Visible(T0.AddSeconds(4))).Alpha);
        Assert.Equal(0.5f, Assert.Single(board.Visible(T0.AddSeconds(5.5))).Alpha, 3);
        Assert.Empty(board.Visible(T0.AddSeconds(6)));
    }

    [Fact]
    public void MaxPerMember_DropsOldestUnpinned()
    {
        var board = new BubbleBoard(() => new BubbleSettings { Seconds = 6, MaxPerMember = 2 });
        var first = board.Add("A", Seg("1"), T0);
        board.TogglePin(first.Id, T0);
        board.Add("A", Seg("2"), T0.AddSeconds(1));
        board.Add("A", Seg("3"), T0.AddSeconds(2));
        board.Add("B", Seg("x"), T0.AddSeconds(2));

        var texts = board.Visible(T0.AddSeconds(3)).Where(v => v.Bubble.Member == "A").Select(v => v.Bubble.Text).ToList();
        Assert.Equal(["1", "3"], texts); // pinned "1" survives, "2" was dropped
    }

    [Fact]
    public void Pinned_StaysUntilUnpinned_ThenFadesFromNow()
    {
        var board = new BubbleBoard(() => new BubbleSettings { Seconds = 6, ExtendByLength = false });
        var b = board.Add("A", Seg("hi"), T0);
        board.TogglePin(b.Id, T0.AddSeconds(1));

        Assert.Equal(1f, Assert.Single(board.Visible(T0.AddMinutes(10))).Alpha);

        board.TogglePin(b.Id, T0.AddMinutes(10));
        Assert.Single(board.Visible(T0.AddMinutes(10).AddSeconds(3)));
        Assert.Empty(board.Visible(T0.AddMinutes(10).AddSeconds(7)));
    }

    [Fact]
    public void History_KeepsTheLastThree_NewestFirst()
    {
        var board = new BubbleBoard(() => new BubbleSettings());
        for (var i = 1; i <= 5; i++) board.Add($"P{i}", Seg($"m{i}"), T0.AddSeconds(i));
        Assert.Equal(["m5", "m4", "m3"], board.History.Select(h => h.Text));
    }

    [Fact]
    public void Hotkey_DefaultsToCtrlShiftT()
        => Assert.Equal("Ctrl+Shift+T", new BubbleSettings().HistoryHotkey.ToString());
}

public sealed class PartyNameMatcherTests
{
    [Theory]
    [InlineData(" Kaela Rhyne", "Kaela Rhyne", true)]
    [InlineData("Lv100 K. Rhyne", "Kaela Rhyne", true)]
    [InlineData("Kaela R.", "Kaela Rhyne", true)]
    [InlineData("K. R.", "Kaela Rhyne", true)]
    [InlineData("kaela   rhyne", "Kaela Rhyne", true)]
    [InlineData("Kaela Rhyner", "Kaela Rhyne", false)]
    [InlineData("Mirri Tolo", "Kaela Rhyne", false)]
    [InlineData("", "Kaela Rhyne", false)]
    public void Matches_FullAndAbbreviatedNames(string row, string sender, bool expected)
        => Assert.Equal(expected, PartyNameMatcher.Matches(row, sender));
}
