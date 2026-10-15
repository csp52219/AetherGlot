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
    [PluginService] internal static ISeStringEvaluator      SeStringEvaluator { get; private set; } = null!;
    [PluginService] internal static IGameGui                GameGui         { get; private set; } = null!;
    [PluginService] internal static IKeyState               KeyState        { get; private set; } = null!;

    public Configuration Configuration { get; init; }

    /// <summary>Secrets (Credential Manager behind an in-memory read cache).</summary>
    internal ISecretStore Secrets { get; }

    internal LlmHttpSender Sender { get; }
    internal TranslatorProviderFactory ProviderFactory { get; }
    internal ModelCapabilityStore Capabilities { get; }
    internal TranslationPipeline Pipeline { get; }
    internal ConnectionDiagnostics Diagnostics { get; }
    internal GlossaryManager Glossary { get; }
    internal TranslationLog TranslationLog { get; }
    internal BubbleBoard Bubbles { get; }

    private readonly WindowSystem           windowSystem = new("AetherGlot");
    private readonly ConfigWindow           configWindow;
    private readonly TranslationLogWindow   logWindow;
    private readonly BubbleOverlay          bubbleOverlay;
    private readonly SendPreviewWindow      sendPreviewWindow;
    private readonly InspectorWindow        inspectorWindow;
    private readonly GlossaryWindow         glossaryWindow;
    private readonly AutoTranslateWindow    autoTranslateWindow;
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

        Glossary = new GlossaryManager(
            Configuration, DataManager, Log, httpClient, BuildConnectionSettings,
            PluginInterface.GetPluginConfigDirectory(), PluginInterface.AssemblyLocation.DirectoryName!);
        _ = Glossary.ReloadAsync();

        TranslationLog = new TranslationLog(Configuration.Display.Log.Capacity);
        Bubbles        = new BubbleBoard(() => Configuration.Display.Bubble);

        var translationCtx = new TranslationContext();
        var chatSender     = new DalamudChatSender(Framework, Log);

        translationService = new ChatTranslationService(
            Configuration, Pipeline, translationCtx, BuildConnectionSettings,
            ChatGui, ClientState, ObjectTable, Condition, PartyList, DataManager, Framework, SeStringEvaluator, Glossary, TranslationLog, Bubbles, Log);

        // ── Windows ────────────────────────────────────────────────────────
        inspectorWindow     = new InspectorWindow(this);
        glossaryWindow      = new GlossaryWindow(this);
        autoTranslateWindow = new AutoTranslateWindow(this);
        sendPreviewWindow   = new SendPreviewWindow(Configuration, Pipeline, translationCtx, chatSender, BuildConnectionSettings,
                                                    ClientState, Glossary.Service, inspectorWindow.Inspect);
        configWindow = new ConfigWindow(this);
        logWindow    = new TranslationLogWindow(this);
        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(logWindow);
        bubbleOverlay = new BubbleOverlay(Configuration, Bubbles, GameGui, KeyState, OpenLog);
        windowSystem.AddWindow(sendPreviewWindow);
        windowSystem.AddWindow(inspectorWindow);
        windowSystem.AddWindow(glossaryWindow);
        windowSystem.AddWindow(autoTranslateWindow);

        // ── Commands ───────────────────────────────────────────────────────
        CommandManager.AddHandler(CommandName, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = Loc.Localize("CommandHelp",
                "Open AetherGlot settings. Subcommands: log (translation log), dict [text] (dictionary), at (auto-translate list), inspect (latest translation), test (connection test), models (model list), reload (reload dictionaries), lookup <text> (show dictionary hits)."),
        });
        CommandManager.AddHandler(SendCommandName, new CommandInfo(OnSendCommand)
        {
            HelpMessage = Loc.Localize("SendCommandHelp", "Translate and send: /agsend [channel] <text>"),
        });

        ChatGui.ChatMessage += OnChatMessage;
        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
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
        var trimmed = args.Trim();
        var space   = trimmed.IndexOf(' ');
        var sub     = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        var rest    = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();

        switch (sub)
        {
            case "test":
                _ = RunConnectionTestAsync();
                break;
            case "models":
                _ = ListModelsAsync();
                break;
            case "log":
                logWindow.Toggle();
                break;
            case "reload":
                _ = ReloadGlossaryAsync();
                break;
            case "lookup" when rest.Length > 0:
                LookupCommand(rest);
                break;
            case "dict":
                if (rest.Length > 0) glossaryWindow.OpenWithQuery(rest);
                else glossaryWindow.Toggle();
                break;
            case "at":
                autoTranslateWindow.Toggle();
                break;
            case "inspect":
                if (!inspectorWindow.InspectLatest())
                    Echo(Loc.Localize("InspectNothing", "Nothing to inspect yet: no translated message in the log."));
                break;
            default:
                configWindow.Toggle();
                break;
        }
    }

    // ── /aetherglot reload, /aetherglot lookup ─────────────────────────────

    private async Task ReloadGlossaryAsync()
    {
        Echo(Loc.Localize("GlossaryReloading", "Reloading dictionaries..."));
        await Glossary.ReloadAsync(forceDownload: true).ConfigureAwait(false);
        var s = Glossary.Service.Stats;
        var c = Glossary.Community;
        Echo($"My dictionary {s.UserCount} · game data {s.GameDataCount} · community {s.CommunityCount} " +
             $"(v{s.CommunityVersion ?? "?"}, {c?.Origin}) · auto-translate {Glossary.Catalog.Entries.Count}");
        if (c?.Error is { } error)
            Echo($"Community dictionary: {error}");
    }

    // Runs on the framework thread (command handler), so game state can be read
    private void LookupCommand(string text)
    {
        var clientCode = ChatTranslationService.LanguageCode(ClientState.ClientLanguage);
        var sourceCode = TermNormalizer.IsJapanese(text) ? "ja" : "en";
        var targetCode = sourceCode == "ja" ? "en" : "ja";
        if (sourceCode == clientCode && clientCode is not ("ja" or "en"))
        {
            Echo("Dictionary lookup supports Japanese ⇔ English only.");
            return;
        }

        var inCombat = Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat];
        var lookup   = Glossary.Service.Lookup(text, sourceCode, targetCode, inCombat, null, Configuration.Translation.MaxGlossaryHits);
        if (lookup.Matches.Count == 0)
        {
            Echo(Loc.Localize("LookupNoHits", "No dictionary hits."));
            return;
        }

        foreach (var m in lookup.Matches)
        {
            var entry   = m.Resolution.Entry;
            var targets = m.Resolution.Chosen is { } chosen
                ? chosen.TextFor(targetCode)
                : string.Join(" / ", m.Resolution.Candidates.Select(c => $"{c.Sense.TextFor(targetCode)} ({c.Score})"));
            var state = m.Resolution.IsAmbiguous ? " [ambiguous]" : string.Empty;
            Echo($"{m.Hit.Surface} → {targets}  <{entry.Source}/{entry.Kind}>{state}");
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

    private void DrawUI()
    {
        bubbleOverlay.Draw();
        windowSystem.Draw();
    }

    private void OnFrameworkUpdate(IFramework framework) => bubbleOverlay.Update();

    private void OpenConfigUi() => configWindow.IsOpen = true;

    private void OpenMainUi() => logWindow.IsOpen = true;

    internal void OpenSettings() => configWindow.IsOpen = true;

    internal void OpenLog() => logWindow.IsOpen = true;

    internal void OpenReply(XivChatType channel) => sendPreviewWindow.OpenForReply(channel);

    internal void OpenInspector(LogEntry entry) => inspectorWindow.Inspect(entry);

    internal bool InspectLatest() => inspectorWindow.InspectLatest();

    internal void OpenGlossary() => glossaryWindow.IsOpen = true;

    internal void OpenAutoTranslate() => autoTranslateWindow.IsOpen = true;

    /// <summary>Translates a log entry again with the same request, bypassing the cache, and updates the entry.</summary>
    internal async Task RetranslateAsync(LogEntry entry)
    {
        if (entry.Outcome?.Request is not { } request) return;
        var settings = BuildConnectionSettings();
        if (ConnectionSettingsBuilder.MissingRequirement(settings) is { } missing)
        {
            Echo(missing);
            return;
        }
        try
        {
            var outcome = await Pipeline.TranslateInboundAsync(request, settings, Configuration.Translation.ToPipelineOptions(),
                                                              disposeCts.Token, bypassCache: true).ConfigureAwait(false);
            TranslationLog.Complete(entry, outcome);
            if (outcome.Kind == InboundOutcomeKind.Partial)
                Glossary.RecordUnresolved(outcome);
        }
        catch (OperationCanceledException)
        {
            // Plugin unloading
        }
        catch (Exception ex)
        {
            Log.Error("[AetherGlot] Re-translation failed ({Type}).", ex.GetType().Name);
        }
    }

    /// <summary>Saves a word from the log as a My dictionary entry and rebuilds the glossary.</summary>
    internal void AddToMyDictionary(string surface, string meaning)
    {
        var clientJapanese = ClientState.ClientLanguage == Dalamud.Game.ClientLanguage.Japanese;
        var (en, ja) = clientJapanese ? (surface, meaning) : (meaning, surface);
        var id = "user:" + TermNormalizer.Normalize(surface);
        _ = Task.Run(() =>
        {
            try
            {
                Glossary.User.Upsert(TermEntry.Simple(id, TermKind.Other, TermSource.User, en, ja));
                if (Glossary.Pending.Remove(surface)) Glossary.Pending.Save();
                Glossary.Rebuild();
            }
            catch (Exception ex)
            {
                Log.Error("[AetherGlot] Could not save to {File} ({Type}).", GlossaryManager.UserFileName, ex.GetType().Name);
            }
        });
    }

    /// <summary>Adds sample entries to the log (display test, no model call).</summary>
    internal void ShowSampleMessages()
    {
        var now = DateTimeOffset.Now;
        var at  = new Dictionary<string, string> { ["at0"] = "【こんにちは！】" };

        InboundOutcome Outcome(string source, IReadOnlyDictionary<string, string> map, InboundOutcomeKind kind,
                               IReadOnlyList<DisplaySegment> segments, InboundResponse response) => new()
        {
            Request = new InboundRequest(source, map, "Japanese", "ja"), Kind = kind, Segments = segments, Response = response,
        };

        var m1 = Placeholderizer.Build([new AutoTranslatePart(2, 203, "こんにちは！"), new TextPart("opo > rapu > kua-ru, keep ur form uptime pls")]);
        var e1 = TranslationLog.Add(now, ChannelGroup.Party, (int)XivChatType.Party, "Kaela Rhyne", "Tomberry", m1, false);
        TranslationLog.Complete(e1, Outcome(m1.Text, m1.AutoTranslateTexts, InboundOutcomeKind.Translated,
            [new("【こんにちは！】", SegmentKind.AutoTranslate), new(" 魔猿 → 走竜 → 猛虎の順で回して、型を切らさないでね", SegmentKind.Translated)],
            new InboundResponse { Status = "ok", Lang = "en", Terms = [new() { Src = "opo", Dst = "壱の型：魔猿", From = "dict" }, new() { Src = "rapu", Dst = "弐の型：走竜", From = "dict" }] }));

        var m2 = Placeholderizer.Build([new TextPart("brb bio, 2 min")]);
        var e2 = TranslationLog.Add(now, ChannelGroup.Party, (int)XivChatType.Party, "Mirri Tolo", "Cactuar", m2, false);
        TranslationLog.Complete(e2, Outcome(m2.Text, m2.AutoTranslateTexts, InboundOutcomeKind.Translated,
            [new("ちょっとトイレ行ってきます、2分で戻ります", SegmentKind.Translated)],
            new InboundResponse { Status = "ok", Lang = "en", Terms = [new() { Src = "bio", Dst = "トイレ（離席）", Conf = 0.92, Alts = ["バイオ（技）"] }] }));

        var m3 = Placeholderizer.Build([new TextPart("ngl that pull was kinda mid, next one we do the zorp strat")]);
        var e3 = TranslationLog.Add(now, ChannelGroup.Party, (int)XivChatType.Party, "Bort Halvsson", "Behemoth", m3, true);
        TranslationLog.Complete(e3, Outcome(m3.Text, m3.AutoTranslateTexts, InboundOutcomeKind.Partial,
            [new("正直今のトライは微妙だった、次は ", SegmentKind.Translated), new("zorp strat", SegmentKind.Unresolved), new(" でやろう", SegmentKind.Translated)],
            new InboundResponse { Status = "partial", Lang = "en", Terms = [new() { Src = "pull", Dst = "トライ", From = "dict" }, new() { Src = "mid", Dst = "微妙", From = "slang", Conf = 0.88 }] }));

        var m4 = Placeholderizer.Build([new TextPart("了解です！")]);
        var e4 = TranslationLog.Add(now, ChannelGroup.Party, (int)XivChatType.Party, "ぷりん", "Tiamat", m4, false);
        TranslationLog.Complete(e4, Outcome(m4.Text, m4.AutoTranslateTexts, InboundOutcomeKind.Skipped, [], new InboundResponse { Status = "skip", Lang = "ja", Translation = "" }));

        // The same samples as bubbles, for members currently in the party (if any)
        foreach (var name in PartyList.Select(m => m.Name.TextValue).Where(n => !string.IsNullOrEmpty(n)).Take(3))
            Bubbles.Add(name, [new DisplaySegment(Loc.Localize("BubbleSample", "This is a sample translation bubble."), SegmentKind.Translated)], DateTimeOffset.Now);

        logWindow.IsOpen = true;
    }

    public void Dispose()
    {
        disposeCts.Cancel();
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(SendCommandName);
        ChatGui.ChatMessage -= OnChatMessage;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
        logWindow.Dispose();
        sendPreviewWindow.Dispose();
        inspectorWindow.Dispose();
        glossaryWindow.Dispose();
        autoTranslateWindow.Dispose();
        Glossary.Dispose();
        httpClient.Dispose();
        disposeCts.Dispose();
    }
}
