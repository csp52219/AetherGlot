using AetherGlot.Core.Display;
using AetherGlot.Core.Log;
using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Display;

public sealed class DisplayAndLogTests
{
    [Fact]
    public void Recommended_MatchesTheSpec()
    {
        var d = new DisplayConfig();
        Assert.Equal(DisplayPreset.Recommended, d.Preset);
        Assert.Equal(Destinations.Bubble | Destinations.Log, d.Resolve(ChannelGroup.Party, false, false));
        Assert.Equal(Destinations.Log | Destinations.GameChat, d.Resolve(ChannelGroup.FreeCompany, false, false));
        Assert.Equal(Destinations.Log | Destinations.GameChat, d.Resolve(ChannelGroup.Tell, false, false));
        Assert.Equal(Destinations.Log, d.Resolve(ChannelGroup.Say, false, false));
        Assert.Equal(Destinations.Log, d.Resolve(ChannelGroup.CrossWorldLinkshell, false, false));
    }

    [Fact]
    public void Combat_Modes_AreApplied()
    {
        var d = new DisplayConfig();
        Assert.Equal(Destinations.Bubble | Destinations.Log, d.Resolve(ChannelGroup.Party, inCombat: true, mentionsMe: false));
        Assert.Equal(Destinations.None, d.Resolve(ChannelGroup.Alliance, inCombat: true, mentionsMe: false));
        Assert.Equal(Destinations.Log, d.Resolve(ChannelGroup.Alliance, inCombat: true, mentionsMe: true));
        Assert.Equal(Destinations.None, d.Resolve(ChannelGroup.Say, inCombat: true, mentionsMe: true));
        Assert.Equal(Destinations.Log, d.Resolve(ChannelGroup.Say, inCombat: false, mentionsMe: false));
    }

    [Fact]
    public void Bubble_IsOnlyForPartyAndAlliance()
    {
        var d = new DisplayConfig();
        d.Routes[ChannelGroup.Say].Bubble = true;
        Assert.Equal(Destinations.Log, d.Resolve(ChannelGroup.Say, false, false));
        Assert.True(DisplayConfig.SupportsBubble(ChannelGroup.Alliance));
        Assert.All(DisplayConfig.PresetRoutes(DisplayPreset.BubbleOnly).Where(kv => !DisplayConfig.SupportsBubble(kv.Key)),
                   kv => Assert.False(kv.Value.Bubble));
    }

    [Fact]
    public void EditingTheTable_SwitchesToCustom_AndBackWhenItMatchesAPreset()
    {
        var d = new DisplayConfig();
        d.Routes[ChannelGroup.Say].GameChat = true;
        d.OnTableEdited();
        Assert.Equal(DisplayPreset.Custom, d.Preset);

        d.Routes[ChannelGroup.Say].GameChat = false;
        d.OnTableEdited();
        Assert.Equal(DisplayPreset.Recommended, d.Preset);

        d.ApplyPreset(DisplayPreset.WindowOnly);
        Assert.Equal(DisplayPreset.WindowOnly, d.DetectPreset());
        Assert.Equal(Destinations.Log, d.Resolve(ChannelGroup.Party, false, false));
    }

    [Fact]
    public void Defaults_MatchTheMockup()
    {
        var d = new DisplayConfig();
        Assert.Equal(6f, d.Bubble.Seconds);
        Assert.Equal(2, d.Bubble.MaxPerMember);
        Assert.True(d.Bubble.ExtendByLength);
        Assert.Equal(GameChatSettings.DefaultPrefix, d.GameChat.Prefix);
        Assert.Equal(GameChatOutput.Echo, d.GameChat.Output);
        Assert.True(d.Log.OneLine && d.Log.PauseWhileScrolled && d.Log.HighlightMentions);
    }

    private static PlaceholderizedMessage Msg(string text) => Placeholderizer.Build([new TextPart(text)]);

    [Fact]
    public void Log_IsBounded_FilteredAndVersioned()
    {
        var log = new TranslationLog(10);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 15; i++)
            log.Add(now, i % 2 == 0 ? ChannelGroup.Party : ChannelGroup.Say, 0, $"P{i}", null, Msg($"m{i}"), mentionsMe: i == 14);

        Assert.Equal(10, log.Snapshot().Count);
        Assert.Equal("P5", log.Snapshot()[0].Sender);
        Assert.All(log.Snapshot(LogFilter.PartyOnly), e => Assert.Equal(ChannelGroup.Party, e.Group));
        Assert.Equal("P14", Assert.Single(log.Snapshot(LogFilter.MentionsOnly)).Sender);

        var v = log.Version;
        var entry = log.Snapshot()[^1];
        Assert.Equal(LogEntryState.Translating, entry.State);
        log.Complete(entry, null);
        Assert.Equal(LogEntryState.Done, entry.State);
        Assert.True(log.Version > v);
    }

    [Fact]
    public void Log_TracksLatencyAndConnectionState()
    {
        var log = new TranslationLog(10);
        var entry = log.Add(DateTimeOffset.UtcNow, ChannelGroup.Party, 0, "A", null, Msg("hi"), false);
        var outcome = new InboundOutcome
        {
            Request = new InboundRequest("hi", new Dictionary<string, string>(), "Japanese", "ja"),
            Kind = InboundOutcomeKind.Translated, Segments = [],
            Attempts = [new AttemptRecord("{}", "m", 1, 1, TimeSpan.FromMilliseconds(800), true, null)],
        };
        log.Complete(entry, outcome);

        Assert.Equal(800, log.AverageLatency!.Value.TotalMilliseconds, 1);
        Assert.True(log.LastCallOk);
        Assert.Same(outcome, entry.Outcome);
    }

    [Theory]
    [InlineData("sch your bio fell off", true)]
    [InlineData("Kaela pls stack", true)]
    [InlineData("healers watch the raidwide", true)]
    [InlineData("学者さん、バイオ切れてる", true)]
    [InlineData("schedule is fine", false)]
    [InlineData("tanks swap now", false)]
    public void Mentions_AreDetected(string text, bool expected)
    {
        var detector = new MentionDetector(
            MentionDetector.NameWords("Kaela Rhyne")
                .Concat(["Scholar", "SCH", "学者"])
                .Concat(MentionDetector.RoleWords(4)));
        Assert.Equal(expected, detector.Matches(text));
    }
}

public sealed class GameChatMigrationTests
{
    [Fact]
    public void Defaults_UseEcho_AndAPrefixTheGameFontCanShow()
    {
        var g = new AetherGlot.Core.Display.GameChatSettings();
        Assert.Equal(AetherGlot.Core.Display.GameChatOutput.Echo, g.Output);
        Assert.DoesNotContain('↳', g.Prefix);
    }

    [Fact]
    public void MigrateToV4_SwitchesToEcho_AndReplacesOnlyTheOldDefaultPrefix()
    {
        var old = new AetherGlot.Core.Display.GameChatSettings { Output = AetherGlot.Core.Display.GameChatOutput.OriginalChannel, Prefix = "[AG] ↳" };
        old.MigrateToV4();
        Assert.Equal(AetherGlot.Core.Display.GameChatOutput.Echo, old.Output);
        Assert.Equal(AetherGlot.Core.Display.GameChatSettings.DefaultPrefix, old.Prefix);

        var custom = new AetherGlot.Core.Display.GameChatSettings { Output = AetherGlot.Core.Display.GameChatOutput.OriginalChannel, Prefix = "»" };
        custom.MigrateToV4();
        Assert.Equal("»", custom.Prefix);
    }
}

public sealed class NotLoggedTests
{
    [Fact]
    public void MessagesShownOnlyElsewhere_AreCountedPerChannel()
    {
        var log = new TranslationLog(10);
        log.RecordNotLogged(ChannelGroup.Say, Destinations.GameChat);
        log.RecordNotLogged(ChannelGroup.Say, Destinations.GameChat);
        log.RecordNotLogged(ChannelGroup.Party, Destinations.Bubble);

        var counts = log.NotLogged;
        Assert.Equal(2, counts.Count);
        Assert.Contains(new NotLoggedCount(ChannelGroup.Say, 2, Destinations.GameChat), counts);
        Assert.Contains(new NotLoggedCount(ChannelGroup.Party, 1, Destinations.Bubble), counts);

        log.Clear();
        Assert.Empty(log.NotLogged);
    }
}
