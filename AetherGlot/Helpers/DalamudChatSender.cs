namespace AetherGlot.Helpers;

/// <summary>
/// Sends messages via the in-game chat box.
/// Uses <c>UIModule.ProcessChatBoxEntry</c> (FFXIVClientStructs) marshalled to the
/// game's framework thread via <see cref="IFramework"/>.
/// </summary>
public sealed class DalamudChatSender : IChatSender
{
    [PluginService] private static IFramework Framework { get; set; } = null!;

    private readonly IPluginLog log;

    // Channel-to-command mapping (all channels supported by EnabledChannels)
    private static readonly Dictionary<XivChatType, string> ChannelCommands = new()
    {
        [XivChatType.Say]             = "/s",
        [XivChatType.Shout]           = "/sh",
        [XivChatType.Yell]            = "/y",
        [XivChatType.Party]           = "/p",
        [XivChatType.Alliance]        = "/a",
        [XivChatType.FreeCompany]     = "/fc",
        [XivChatType.Ls1]             = "/l1",
        [XivChatType.Ls2]             = "/l2",
        [XivChatType.Ls3]             = "/l3",
        [XivChatType.Ls4]             = "/l4",
        [XivChatType.Ls5]             = "/l5",
        [XivChatType.Ls6]             = "/l6",
        [XivChatType.Ls7]             = "/l7",
        [XivChatType.Ls8]             = "/l8",
        [XivChatType.CrossLinkShell1] = "/cwl1",
        [XivChatType.CrossLinkShell2] = "/cwl2",
        [XivChatType.CrossLinkShell3] = "/cwl3",
        [XivChatType.CrossLinkShell4] = "/cwl4",
        [XivChatType.CrossLinkShell5] = "/cwl5",
        [XivChatType.CrossLinkShell6] = "/cwl6",
        [XivChatType.CrossLinkShell7] = "/cwl7",
        [XivChatType.CrossLinkShell8] = "/cwl8",
    };

    public DalamudChatSender(IPluginLog log) => this.log = log;

    public void SendMessage(XivChatType channel, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (!ChannelCommands.TryGetValue(channel, out var cmd))
        {
            log.Warning("[AetherGlot] Unsupported outbound channel: {Channel}", channel);
            return;
        }

        var fullMessage = $"{cmd} {text}";

        // TODO: Implement via FFXIVClientStructs UIModule.ProcessChatBoxEntry.
        // Requires marshalling to framework thread with IFramework.RunOnFrameworkThread.
        // Left as stub until FFXIVClientStructs unsafe API is wired up.
        log.Warning("[AetherGlot] SendMessage stub — would send: {Message}", fullMessage);
    }
}
