namespace AetherGlot.Core.Display;

/// <summary>Chat channels as shown in the display table (LS 1–8 and CWLS 1–8 are grouped).</summary>
public enum ChannelGroup
{
    Party,
    Alliance,
    Say,
    Shout,
    Yell,
    FreeCompany,
    Linkshell,
    CrossWorldLinkshell,
    Tell,
    NoviceNetwork,
}

/// <summary>Where a translation is shown.</summary>
[Flags]
public enum Destinations
{
    None     = 0,
    Bubble   = 1,
    Log      = 2,
    GameChat = 4,
}

/// <summary>Behaviour while in combat.</summary>
public enum CombatMode
{
    /// <summary>Same as out of combat.</summary>
    All,

    /// <summary>Only messages that mention you (name, job or role).</summary>
    MentionsOnly,

    /// <summary>Nothing is translated or shown.</summary>
    Stop,
}

public enum DisplayPreset
{
    Recommended,
    WindowOnly,
    GameChatOnly,
    BubbleOnly,
    Custom,
}

/// <summary>Destinations and combat behaviour of one channel group.</summary>
[Serializable]
public sealed class ChannelRoute
{
    public bool Bubble { get; set; }
    public bool Log { get; set; }
    public bool GameChat { get; set; }
    public CombatMode InCombat { get; set; } = CombatMode.All;

    public Destinations Destinations =>
        (Bubble ? Destinations.Bubble : 0) | (Log ? Destinations.Log : 0) | (GameChat ? Destinations.GameChat : 0);

    public ChannelRoute Clone() => new() { Bubble = Bubble, Log = Log, GameChat = GameChat, InCombat = InCombat };

    public bool SameAs(ChannelRoute o) =>
        Bubble == o.Bubble && Log == o.Log && GameChat == o.GameChat && InCombat == o.InCombat;
}

[Serializable]
public sealed class BubbleSettings
{
    public float Seconds { get; set; } = 6f;
    public int MaxPerMember { get; set; } = 2;
    public bool ExtendByLength { get; set; } = true;
}

[Serializable]
public sealed class LogSettings
{
    /// <summary>One line per message; click to expand the original and term chips.</summary>
    public bool OneLine { get; set; } = true;

    /// <summary>Stop auto-scrolling while scrolled up.</summary>
    public bool PauseWhileScrolled { get; set; } = true;

    public bool HighlightMentions { get; set; } = true;
    public bool ShowOriginal { get; set; } = true;
    public bool ShowTermChips { get; set; } = true;

    /// <summary>Which messages the log window lists.</summary>
    public LogFilter Filter { get; set; } = LogFilter.All;

    public int Capacity { get; set; } = 500;
}

public enum LogFilter
{
    PartyOnly,
    All,
    MentionsOnly,
}

public enum GameChatOutput
{
    /// <summary>The channel the message came from.</summary>
    OriginalChannel,

    /// <summary>The Echo channel.</summary>
    Echo,
}

[Serializable]
public sealed class GameChatSettings
{
    public GameChatOutput Output { get; set; } = GameChatOutput.OriginalChannel;
    public string Prefix { get; set; } = "[AG] ↳";

    /// <summary>Replace the original line with the translation (only possible for cached translations).</summary>
    public bool ReplaceOriginal { get; set; }
}

/// <summary>Display settings (part of Configuration.json).</summary>
[Serializable]
public sealed class DisplayConfig
{
    public DisplayPreset Preset { get; set; } = DisplayPreset.Recommended;
    public Dictionary<ChannelGroup, ChannelRoute> Routes { get; set; } = PresetRoutes(DisplayPreset.Recommended);
    public BubbleSettings Bubble { get; set; } = new();
    public LogSettings Log { get; set; } = new();
    public GameChatSettings GameChat { get; set; } = new();

    /// <summary>Bubbles sit next to the party list, so only these channels can use them.</summary>
    public static bool SupportsBubble(ChannelGroup group) => group is ChannelGroup.Party or ChannelGroup.Alliance;

    public ChannelRoute RouteFor(ChannelGroup group) =>
        Routes.TryGetValue(group, out var r) ? r : new ChannelRoute();

    /// <summary>
    /// Destinations for a message. Combat "stop" hides everything, "mentions only" hides
    /// messages that do not mention you; bubbles are dropped for channels that cannot use them.
    /// </summary>
    public Destinations Resolve(ChannelGroup group, bool inCombat, bool mentionsMe)
    {
        var route = RouteFor(group);
        if (inCombat)
        {
            if (route.InCombat == CombatMode.Stop) return Destinations.None;
            if (route.InCombat == CombatMode.MentionsOnly && !mentionsMe) return Destinations.None;
        }
        var d = route.Destinations;
        if (!SupportsBubble(group)) d &= ~Destinations.Bubble;
        return d;
    }

    public void ApplyPreset(DisplayPreset preset)
    {
        if (preset == DisplayPreset.Custom) return;
        Routes = PresetRoutes(preset);
        Preset = preset;
    }

    /// <summary>The preset the current table equals, or Custom.</summary>
    public DisplayPreset DetectPreset()
    {
        foreach (var p in new[] { DisplayPreset.Recommended, DisplayPreset.WindowOnly, DisplayPreset.GameChatOnly, DisplayPreset.BubbleOnly })
        {
            var expected = PresetRoutes(p);
            if (Enum.GetValues<ChannelGroup>().All(g => RouteFor(g).SameAs(expected[g])))
                return p;
        }
        return DisplayPreset.Custom;
    }

    /// <summary>Call after the user edits the table.</summary>
    public void OnTableEdited() => Preset = DetectPreset();

    /// <summary>
    /// Presets. Recommended: party → bubble + log, FC and Tell → log + game chat, others → log;
    /// in combat party shows everything, alliance only mentions, open channels stop.
    /// </summary>
    public static Dictionary<ChannelGroup, ChannelRoute> PresetRoutes(DisplayPreset preset)
    {
        var routes = new Dictionary<ChannelGroup, ChannelRoute>();
        foreach (var g in Enum.GetValues<ChannelGroup>())
        {
            var bubbleOk = SupportsBubble(g);
            routes[g] = preset switch
            {
                DisplayPreset.WindowOnly   => new ChannelRoute { Log = true },
                DisplayPreset.GameChatOnly => new ChannelRoute { GameChat = true },
                DisplayPreset.BubbleOnly   => bubbleOk ? new ChannelRoute { Bubble = true } : new ChannelRoute(),
                _ => g switch
                {
                    ChannelGroup.Party                           => new ChannelRoute { Bubble = true, Log = true },
                    ChannelGroup.FreeCompany or ChannelGroup.Tell => new ChannelRoute { Log = true, GameChat = true },
                    _                                            => new ChannelRoute { Log = true },
                },
            };
            routes[g].InCombat = g switch
            {
                ChannelGroup.Party or ChannelGroup.Tell => CombatMode.All,
                ChannelGroup.Alliance                   => CombatMode.MentionsOnly,
                _                                       => CombatMode.Stop,
            };
        }
        return routes;
    }
}
