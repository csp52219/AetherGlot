using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AetherGlot.Helpers;

/// <summary>
/// Sends messages through the game's chat box (<c>UIModule.ProcessChatBoxEntry</c>), always on
/// the framework thread. The text is cleaned first (no control characters, at most 500 bytes)
/// and sanitised by the game so it cannot carry payloads or run a different command.
/// </summary>
public sealed unsafe class DalamudChatSender : IChatSender
{
    // Letters, numbers, symbols and CJK, but no payloads (links, auto-translate bytes, ...)
    private const AllowedEntities Allowed =
        AllowedEntities.UppercaseLetters | AllowedEntities.LowercaseLetters | AllowedEntities.Numbers |
        AllowedEntities.SpecialCharacters | AllowedEntities.CharacterList | AllowedEntities.OtherCharacters |
        AllowedEntities.CJK;

    private readonly IFramework framework;
    private readonly IPluginLog log;

    // Channel-to-command mapping (all channels supported by the send window).
    // Only TextCommand.Command / ShortCommand values are used: those exist in every client
    // language (EN/JA/DE/FR), unlike the localized aliases (e.g. "/n" and "/novice" are EN/FR only).
    private static readonly Dictionary<XivChatType, string> ChannelCommands = new()
    {
        [XivChatType.Say]             = "/s",
        [XivChatType.Shout]           = "/sh",
        [XivChatType.Yell]            = "/y",
        [XivChatType.Party]           = "/p",
        [XivChatType.Alliance]        = "/a",
        [XivChatType.FreeCompany]     = "/fc",
        [XivChatType.NoviceNetwork]   = "/beginner",
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

    public DalamudChatSender(IFramework framework, IPluginLog log)
    {
        this.framework = framework;
        this.log       = log;
    }

    public static bool Supports(XivChatType channel) => ChannelCommands.ContainsKey(channel);

    public void SendMessage(XivChatType channel, string text)
    {
        if (!ChannelCommands.TryGetValue(channel, out var cmd))
        {
            log.Warning("Unsupported outbound channel: {Channel}", channel);
            return;
        }

        var (clean, truncated) = ChatText.Sanitize(text);
        if (clean.Length == 0) return;
        if (truncated)
            log.Information("Message was longer than {Max} bytes and was shortened.", ChatText.MaxBytes);

        var line = $"{cmd} {clean}";
        _ = framework.RunOnFrameworkThread(() => Send(line));
    }

    // Framework thread only
    private void Send(string line)
    {
        var uiModule = UIModule.Instance();
        if (uiModule == null)
        {
            log.Warning("Chat is not available right now; message not sent.");
            return;
        }

        var str = Utf8String.FromString(line);
        try
        {
            str->SanitizeString(Allowed, null);
            uiModule->ProcessChatBoxEntry(str, nint.Zero, false);
            log.Information("Sent a message: {Command} ({Bytes} bytes).", line.Split(' ', 2)[0], (int)str->Length);
        }
        finally
        {
            str->Dtor(true);
        }
    }
}
