using System.Numerics;

namespace AetherGlot.UI;

/// <summary>Channel grouping, labels and colors.</summary>
internal static class ChannelInfo
{
    public static ChannelGroup? GroupOf(XivChatType type) => type switch
    {
        XivChatType.Party or XivChatType.CrossParty => ChannelGroup.Party,
        XivChatType.Alliance                        => ChannelGroup.Alliance,
        XivChatType.Say                             => ChannelGroup.Say,
        XivChatType.Shout                           => ChannelGroup.Shout,
        XivChatType.Yell                            => ChannelGroup.Yell,
        XivChatType.FreeCompany                     => ChannelGroup.FreeCompany,
        XivChatType.TellIncoming                    => ChannelGroup.Tell,
        XivChatType.NoviceNetwork                   => ChannelGroup.NoviceNetwork,
        >= XivChatType.Ls1 and <= XivChatType.Ls8   => ChannelGroup.Linkshell,
        XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3 or XivChatType.CrossLinkShell4 or
        XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6 or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8
                                                    => ChannelGroup.CrossWorldLinkshell,
        _                                           => null,
    };

    public static string GroupLabel(ChannelGroup g) => g switch
    {
        ChannelGroup.Party               => Loc.Localize("ChParty", "Party"),
        ChannelGroup.Alliance            => Loc.Localize("ChAlliance", "Alliance"),
        ChannelGroup.Say                 => Loc.Localize("ChSay", "Say"),
        ChannelGroup.Shout               => Loc.Localize("ChShout", "Shout"),
        ChannelGroup.Yell                => Loc.Localize("ChYell", "Yell"),
        ChannelGroup.FreeCompany         => Loc.Localize("ChFc", "FC"),
        ChannelGroup.Linkshell           => Loc.Localize("ChLs", "LS 1–8"),
        ChannelGroup.CrossWorldLinkshell => Loc.Localize("ChCwls", "CWLS 1–8"),
        ChannelGroup.Tell                => Loc.Localize("ChTell", "Tell"),
        ChannelGroup.NoviceNetwork       => Loc.Localize("ChNn", "Novice Network"),
        _                                => g.ToString(),
    };

    /// <summary>Short tag shown in the log, e.g. [PT], [CW3].</summary>
    public static string Tag(XivChatType type) => type switch
    {
        XivChatType.Party or XivChatType.CrossParty => "PT",
        XivChatType.Alliance                        => "AL",
        XivChatType.Say                             => "S",
        XivChatType.Shout                           => "SH",
        XivChatType.Yell                            => "Y",
        XivChatType.FreeCompany                     => "FC",
        XivChatType.TellIncoming                    => "Tell",
        XivChatType.NoviceNetwork                   => "NN",
        >= XivChatType.Ls1 and <= XivChatType.Ls8   => $"LS{(int)type - (int)XivChatType.Ls1 + 1}",
        _ => GroupOf(type) == ChannelGroup.CrossWorldLinkshell ? $"CW{CwlsIndex(type)}" : type.ToString(),
    };

    public static Vector4 Color(ChannelGroup g) => g switch
    {
        ChannelGroup.Party               => Theme.Hex(0x66E5FF),
        ChannelGroup.Alliance            => Theme.Hex(0xFF9E7A),
        ChannelGroup.FreeCompany         => Theme.Hex(0xABDBE5),
        ChannelGroup.Tell                => Theme.Hex(0xFFB8DE),
        ChannelGroup.Linkshell           => Theme.Hex(0xD4FF7D),
        ChannelGroup.CrossWorldLinkshell => Theme.Hex(0xD4FF7D),
        ChannelGroup.Shout               => Theme.Hex(0xFFA666),
        ChannelGroup.Yell                => Theme.Hex(0xFFFF00),
        _                                => Theme.Text,
    };

    private static int CwlsIndex(XivChatType t) => t switch
    {
        XivChatType.CrossLinkShell1 => 1, XivChatType.CrossLinkShell2 => 2, XivChatType.CrossLinkShell3 => 3,
        XivChatType.CrossLinkShell4 => 4, XivChatType.CrossLinkShell5 => 5, XivChatType.CrossLinkShell6 => 6,
        XivChatType.CrossLinkShell7 => 7, XivChatType.CrossLinkShell8 => 8, _ => 0,
    };
}
