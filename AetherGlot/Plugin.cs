using AetherGlot.Core;
using AetherGlot.Helpers;

namespace AetherGlot;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName     = "/aetherglot";
    private const string SendCommandName = "/agsend";

    // Maps short channel prefixes to XivChatType for the /agsend command
    private static readonly Dictionary<string, XivChatType> ChannelPrefixMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["s"]   = XivChatType.Say,
            ["sh"]  = XivChatType.Shout,
            ["y"]   = XivChatType.Yell,
            ["p"]   = XivChatType.Party,
            ["a"]   = XivChatType.Alliance,
            ["fc"]  = XivChatType.FreeCompany,
            ["nn"]  = XivChatType.NoviceNetwork,
            ["l1"]  = XivChatType.Ls1,
            ["l2"]  = XivChatType.Ls2,
            ["l3"]  = XivChatType.Ls3,
            ["l4"]  = XivChatType.Ls4,
            ["l5"]  = XivChatType.Ls5,
            ["l6"]  = XivChatType.Ls6,
            ["l7"]  = XivChatType.Ls7,
            ["l8"]  = XivChatType.Ls8,
            ["cw1"] = XivChatType.CrossLinkShell1,
            ["cw2"] = XivChatType.CrossLinkShell2,
            ["cw3"] = XivChatType.CrossLinkShell3,
            ["cw4"] = XivChatType.CrossLinkShell4,
            ["cw5"] = XivChatType.CrossLinkShell5,
            ["cw6"] = XivChatType.CrossLinkShell6,
            ["cw7"] = XivChatType.CrossLinkShell7,
            ["cw8"] = XivChatType.CrossLinkShell8,
        };

    // Services injected by Dalamud before the constructor runs
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;
    [PluginService] internal static IChatGui                ChatGui         { get; private set; } = null!;
    [PluginService] internal static IClientState            ClientState     { get; private set; } = null!;
    [PluginService] internal static IObjectTable            ObjectTable     { get; private set; } = null!;
    [PluginService] internal static ICommandManager         CommandManager  { get; private set; } = null!;
    [PluginService] internal static IFramework              Framework       { get; private set; } = null!;

    public Configuration Configuration { get; init; }

    private readonly WindowSystem          windowSystem = new("AetherGlot");
    private readonly ConfigWindow          configWindow;
    private readonly SendPreviewWindow     sendPreviewWindow;
    private readonly ChatTranslationService translationService;
    private readonly HttpClient            httpClient;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        InitialiseLocalization();

        // ── Wire up the Core layer ──────────────────────────────────────────
        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var providerFactory  = new TranslatorProviderFactory(httpClient);
        var promptBuilder    = new PromptBuilder();
        var orchestrator     = new TranslationOrchestrator(providerFactory, promptBuilder);
        var translationCtx   = new TranslationContext();
        var chatSender       = new DalamudChatSender(Log);

        translationService = new ChatTranslationService(
            Configuration, orchestrator, translationCtx, ChatGui, ClientState, ObjectTable, Log);

        // ── Windows ────────────────────────────────────────────────────────
        sendPreviewWindow = new SendPreviewWindow(
            Configuration, orchestrator, translationCtx, promptBuilder, chatSender);
        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(sendPreviewWindow);

        // ── Commands ───────────────────────────────────────────────────────
        CommandManager.AddHandler(CommandName, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = Loc.Localize("CommandHelp", "Open AetherGlot configuration"),
        });
        CommandManager.AddHandler(SendCommandName, new CommandInfo(OnSendCommand)
        {
            HelpMessage = Loc.Localize("SendCommandHelp", "Translate and send: /agsend [channel] <text>"),
        });

        ChatGui.ChatMessage += OnChatMessage;
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenConfigUi;
    }

    private static void InitialiseLocalization()
    {
        var locDir = Path.Combine(PluginInterface.AssemblyLocation.DirectoryName!, "loc");
        if (!Directory.Exists(locDir)) return;

        var lang    = PluginInterface.UiLanguage ?? "en";
        var locFile = Path.Combine(locDir, $"{lang}.json");
        if (!File.Exists(locFile))
            locFile = Path.Combine(locDir, "en.json");

        if (File.Exists(locFile))
            Loc.Setup(File.ReadAllText(locFile));
    }

    private void OnChatMessage(IHandleableChatMessage chatMessage)
        => translationService.HandleInboundMessage(chatMessage);

    private void OnConfigCommand(string command, string args) => configWindow.Toggle();

    private void OnSendCommand(string command, string args)
    {
        if (!Configuration.OutboundEnabled)
        {
            ChatGui.Print(
                Loc.Localize("OutboundDisabled", "[AetherGlot] Outbound translation is disabled."),
                null,
                (ushort)XivChatType.Echo);
            return;
        }

        if (string.IsNullOrWhiteSpace(args))
        {
            ChatGui.Print(
                Loc.Localize("SendCommandUsage",
                    "[AetherGlot] Usage: /agsend [channel] <text>\nPrefixes: s sh y p a fc nn l1-l8 cw1-cw8"),
                null,
                (ushort)XivChatType.Echo);
            return;
        }

        var trimmed = args.Trim();
        var channel = Configuration.DefaultOutboundChannel;
        var text    = trimmed;

        // Parse optional channel prefix: /agsend <prefix> <text>
        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx > 0)
        {
            var prefix = trimmed[..spaceIdx];
            if (ChannelPrefixMap.TryGetValue(prefix, out var mapped))
            {
                channel = mapped;
                text    = trimmed[(spaceIdx + 1)..].TrimStart();
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            ChatGui.Print(
                Loc.Localize("SendCommandUsage",
                    "[AetherGlot] Usage: /agsend [channel] <text>\nPrefixes: s sh y p a fc nn l1-l8 cw1-cw8"),
                null,
                (ushort)XivChatType.Echo);
            return;
        }

        sendPreviewWindow.OpenForText(text, channel);
    }

    private void DrawUI() => windowSystem.Draw();

    private void OpenConfigUi() => configWindow.IsOpen = true;

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(SendCommandName);
        ChatGui.ChatMessage -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenConfigUi;
        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
        sendPreviewWindow.Dispose();
        httpClient.Dispose();
    }
}
