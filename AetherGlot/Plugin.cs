using AetherGlot.Helpers;

namespace AetherGlot;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName     = "/aetherglot";
    private const string SendCommandName = "/agsend";
    private const string EchoPrefix      = "[AetherGlot]";

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
    [PluginService] internal static ICondition              Condition       { get; private set; } = null!;
    [PluginService] internal static IPartyList              PartyList       { get; private set; } = null!;
    [PluginService] internal static IDataManager            DataManager     { get; private set; } = null!;

    public Configuration Configuration { get; init; }

    /// <summary>Secrets (Credential Manager behind an in-memory read cache).</summary>
    internal ISecretStore Secrets { get; }

    internal LlmHttpSender Sender { get; }
    internal TranslatorProviderFactory ProviderFactory { get; }
    internal ModelCapabilityStore Capabilities { get; }
    internal TranslationPipeline Pipeline { get; }
    internal ConnectionDiagnostics Diagnostics { get; }

    private readonly WindowSystem           windowSystem = new("AetherGlot");
    private readonly ConfigWindow           configWindow;
    private readonly SendPreviewWindow      sendPreviewWindow;
    private readonly ChatTranslationService translationService;
    private readonly HttpClient             httpClient;
    private readonly CancellationTokenSource disposeCts = new();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        InitialiseLocalization();

        Secrets = new CachingSecretStore(CreateSecretStore());
        ConfigurationMigrator.Run(Configuration, Secrets, Log);

        // ── Core layer ─────────────────────────────────────────────────────
        // One HttpClient for the plugin lifetime. Redirects are not followed so a
        // Cloudflare Access login redirect is detected instead of fetched.
        httpClient = new HttpClient(LlmHttpSender.CreateDefaultHandler())
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(Configuration.Connection.RequestTimeoutSeconds, 5, 120)),
        };
        Sender          = new LlmHttpSender(httpClient);
        ProviderFactory = new TranslatorProviderFactory(Sender);
        Capabilities    = new ModelCapabilityStore(Configuration.Connection.ModelJsonSupport, PersistCapabilities);
        Pipeline        = new TranslationPipeline(ProviderFactory, new PromptBuilder(), Capabilities,
                                                  new TranslationCache<InboundOutcome>(Configuration.Translation.CacheSize));
        Diagnostics     = new ConnectionDiagnostics(Sender, ProviderFactory, Capabilities);

        var translationCtx = new TranslationContext();
        var chatSender     = new DalamudChatSender(Log);

        translationService = new ChatTranslationService(
            Configuration, Pipeline, translationCtx, BuildConnectionSettings,
            ChatGui, ClientState, ObjectTable, Condition, PartyList, DataManager, Framework, Log);

        // ── Windows ────────────────────────────────────────────────────────
        sendPreviewWindow = new SendPreviewWindow(Configuration, Pipeline, translationCtx, chatSender, BuildConnectionSettings, ClientState);
        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(sendPreviewWindow);

        // ── Commands ───────────────────────────────────────────────────────
        CommandManager.AddHandler(CommandName, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = Loc.Localize("CommandHelp",
                "Open AetherGlot configuration. \"/aetherglot test\" runs the connection test, \"/aetherglot models\" lists models."),
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

    /// <summary>Current connection snapshot (config + secrets). Safe to call from any thread.</summary>
    internal ConnectionSettings BuildConnectionSettings() =>
        ConnectionSettingsBuilder.Build(Configuration.Connection, Secrets);

    private static ISecretStore CreateSecretStore()
    {
        if (OperatingSystem.IsWindows())
            return new CredentialManagerSecretStore();

        // Dalamud only runs on Windows (or Wine, which provides advapi32); keep a safe fallback anyway
        Log.Warning("[AetherGlot] Windows Credential Manager is unavailable; secrets will not be persisted.");
        return new InMemorySecretStore();
    }

    // Called from background threads when a model's JSON-mode support is learned
    private void PersistCapabilities() =>
        _ = Framework.RunOnFrameworkThread(() =>
        {
            Configuration.Connection.ModelJsonSupport = Capabilities.Snapshot();
            Configuration.Save();
        });

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

    private void OnConfigCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "test":
                _ = RunConnectionTestAsync();
                break;
            case "models":
                _ = ListModelsAsync();
                break;
            default:
                configWindow.Toggle();
                break;
        }
    }

    // ── /aetherglot test ───────────────────────────────────────────────────

    internal async Task<DiagnosticReport> RunConnectionTestAsync(bool echo = true)
    {
        var settings = BuildConnectionSettings();
        if (echo)
            Echo(Loc.Localize("TestStarted", "Running connection test..."));

        DiagnosticReport report;
        try
        {
            report = await Diagnostics.RunAsync(settings, disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new DiagnosticReport([], []);
        }

        if (echo)
        {
            foreach (var step in report.Steps)
            {
                var mark = step.Status switch
                {
                    DiagnosticStatus.Ok      => "OK  ",
                    DiagnosticStatus.Warning => "WARN",
                    DiagnosticStatus.Failed  => "FAIL",
                    _                        => "SKIP",
                };
                var detail = SecretRedactor.Redact(step.Detail, settings.SecretValues());
                Echo($"{mark} {StepLabel(step.Kind)}: {detail}");
                if (step.Hint is not null && step.Status == DiagnosticStatus.Failed)
                    Echo($"     → {step.Hint}");
            }
            Echo(report.AllPassed
                ? Loc.Localize("TestPassed", "Connection test passed.")
                : Loc.Localize("TestFailed", "Connection test failed."));
            Echo(Loc.Localize("SecretsNotice", "Keys and secrets are stored in Windows Credential Manager, not in the configuration file."));
        }
        return report;
    }

    internal static string StepLabel(DiagnosticStepKind kind) => kind switch
    {
        DiagnosticStepKind.CloudflareAccess => Loc.Localize("StepAccess", "Cloudflare Access"),
        DiagnosticStepKind.Authentication   => Loc.Localize("StepAuth", "LiteLLM authentication"),
        DiagnosticStepKind.ModelList        => Loc.Localize("StepModels", "Model list"),
        DiagnosticStepKind.JsonInbound      => Loc.Localize("StepJsonInbound", "JSON response (inbound model)"),
        DiagnosticStepKind.JsonOutbound     => Loc.Localize("StepJsonOutbound", "JSON response (outbound model)"),
        _                                   => kind.ToString(),
    };

    // ── /aetherglot models ─────────────────────────────────────────────────

    internal async Task<IReadOnlyList<string>> ListModelsAsync(bool echo = true)
    {
        var settings = BuildConnectionSettings();
        try
        {
            if (ProviderFactory.GetProvider(settings) is not IModelLister lister)
            {
                if (echo) Echo(Loc.Localize("ModelsUnsupported", "Model listing is only available through LiteLLM / OpenAI-compatible."));
                return [];
            }

            var models = await lister.ListModelsAsync(disposeCts.Token).ConfigureAwait(false);
            if (echo)
                Echo($"{models.Count} models: {string.Join(", ", models)}");
            return models;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            var msg = ex is LlmHttpException or InvalidOperationException ? ex.Message : ex.GetType().Name;
            if (echo) Echo(SecretRedactor.Redact(msg, settings.SecretValues()));
            return [];
        }
    }

    private static void Echo(string text) =>
        _ = Framework.RunOnFrameworkThread(() => ChatGui.Print($"{EchoPrefix} {text}", null, (ushort)XivChatType.Echo));

    // ── /agsend ────────────────────────────────────────────────────────────

    private void OnSendCommand(string command, string args)
    {
        var usage = Loc.Localize("SendCommandUsage",
            "[AetherGlot] Usage: /agsend [channel] <text>\nPrefixes: s sh y p a fc nn l1-l8 cw1-cw8");

        if (!Configuration.OutboundEnabled)
        {
            ChatGui.Print(Loc.Localize("OutboundDisabled", "[AetherGlot] Outbound translation is disabled."), null, (ushort)XivChatType.Echo);
            return;
        }

        if (string.IsNullOrWhiteSpace(args))
        {
            ChatGui.Print(usage, null, (ushort)XivChatType.Echo);
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
            ChatGui.Print(usage, null, (ushort)XivChatType.Echo);
            return;
        }

        sendPreviewWindow.OpenForText(text, channel);
    }

    private void DrawUI() => windowSystem.Draw();

    private void OpenConfigUi() => configWindow.IsOpen = true;

    public void Dispose()
    {
        disposeCts.Cancel();
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
        disposeCts.Dispose();
    }
}
