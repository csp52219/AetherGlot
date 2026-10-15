using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherGlot.Windows;

/// <summary>⑤ Settings: General / Connection / AI & translation / Channels / Dictionary / Display.</summary>
public sealed class ConfigWindow : Window, IDisposable
{
    private enum Tab { General, Connection, Ai, Channels, Dictionary, Display }

    private readonly Plugin plugin;
    private readonly Configuration config;
    private Tab? selectTab;

    // Secret edit buffers, keyed by secret name; loaded lazily from the secret store
    private readonly Dictionary<string, string> secretBuffers = new(StringComparer.Ordinal);
    private readonly HashSet<string> dirtySecrets = new(StringComparer.Ordinal);
    private readonly HashSet<string> visibleSecrets = new(StringComparer.Ordinal);

    // Extra headers being edited (name + value buffer); values are secrets
    private List<(string Name, string Value)> headerRows = [];
    private List<string> loadedHeaderNames = [];
    private bool headersDirty;

    // Results of background operations
    private volatile DiagnosticReport? lastReport;
    private volatile bool testRunning;
    private volatile IReadOnlyList<string> fetchedModels = [];
    private volatile bool fetchingModels;
    private volatile string? fetchError;
    private volatile bool reloadingGlossary;

    private const float LabelWidth = 130f;

    private static readonly (ConnectionMode Mode, string LocKey, string Fallback)[] Modes =
    [
        (ConnectionMode.Direct,           "ModeDirect",           "Direct (vendor API)"),
        (ConnectionMode.LiteLlm,          "ModeLiteLlm",          "LiteLLM proxy"),
        (ConnectionMode.OpenAICompatible, "ModeOpenAICompatible", "OpenAI-compatible"),
    ];

    private static readonly (XivChatType Type, string LocKey, string Fallback)[] Channels =
    [
        (XivChatType.Party,           "ChannelParty",        "Party"),
        (XivChatType.Alliance,        "ChannelAlly",         "Alliance"),
        (XivChatType.Say,             "ChannelSay",          "Say"),
        (XivChatType.Shout,           "ChannelShout",        "Shout"),
        (XivChatType.Yell,            "ChannelYell",         "Yell"),
        (XivChatType.FreeCompany,     "ChannelFc",           "Free Company"),
        (XivChatType.TellIncoming,    "ChannelTell",         "Tell"),
        (XivChatType.NoviceNetwork,   "ChannelNoviceNetwork","Novice Network"),
        (XivChatType.Ls1,             "ChannelLs1",          "LS 1"),
        (XivChatType.Ls2,             "ChannelLs2",          "LS 2"),
        (XivChatType.Ls3,             "ChannelLs3",          "LS 3"),
        (XivChatType.Ls4,             "ChannelLs4",          "LS 4"),
        (XivChatType.Ls5,             "ChannelLs5",          "LS 5"),
        (XivChatType.Ls6,             "ChannelLs6",          "LS 6"),
        (XivChatType.Ls7,             "ChannelLs7",          "LS 7"),
        (XivChatType.Ls8,             "ChannelLs8",          "LS 8"),
        (XivChatType.CrossLinkShell1, "ChannelCwls1",        "CWLS 1"),
        (XivChatType.CrossLinkShell2, "ChannelCwls2",        "CWLS 2"),
        (XivChatType.CrossLinkShell3, "ChannelCwls3",        "CWLS 3"),
        (XivChatType.CrossLinkShell4, "ChannelCwls4",        "CWLS 4"),
        (XivChatType.CrossLinkShell5, "ChannelCwls5",        "CWLS 5"),
        (XivChatType.CrossLinkShell6, "ChannelCwls6",        "CWLS 6"),
        (XivChatType.CrossLinkShell7, "ChannelCwls7",        "CWLS 7"),
        (XivChatType.CrossLinkShell8, "ChannelCwls8",        "CWLS 8"),
    ];

    public ConfigWindow(Plugin plugin)
        : base(Loc.Localize("ConfigWindowTitle", "AetherGlot Settings") + "###AetherGlotSettings")
    {
        this.plugin = plugin;
        config      = plugin.Configuration;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(900, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen()
    {
        // Re-read secrets every time the window opens
        secretBuffers.Clear();
        dirtySecrets.Clear();
        visibleSecrets.Clear();
        loadedHeaderNames = config.Connection.ExtraHeaderNames.ToList();
        headerRows = loadedHeaderNames.Select(n => (n, ReadSecret(SecretNames.ExtraHeader(n)))).ToList();
        headersDirty = false;
    }

    public override void Draw()
    {
        var footer = ImGui.GetFrameHeightWithSpacing() + 6;
        if (ImGui.BeginTabBar("##settingsTabs"))
        {
            DrawTab(Tab.General,    Loc.Localize("TabGeneral", "General"),            DrawGeneralTab, footer);
            DrawTab(Tab.Connection, Loc.Localize("TabConnection", "Connection"),      DrawConnectionTab, footer);
            DrawTab(Tab.Ai,         Loc.Localize("TabAi", "AI & translation"),        DrawAiTab, footer);
            DrawTab(Tab.Channels,   Loc.Localize("TabChannels", "Channels"),          DrawChannelsTab, footer);
            DrawTab(Tab.Dictionary, Loc.Localize("TabDictionary", "Dictionary"),      DrawDictionaryTab, footer);
            DrawTab(Tab.Display,    Loc.Localize("TabDisplay", "Display"),            DrawDisplayTab, footer);
            ImGui.EndTabBar();
        }
        selectTab = null;

        ImGui.Separator();
        DrawFooter();
    }

    private void DrawTab(Tab tab, string label, Action body, float footer)
    {
        var flags = selectTab == tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (!ImGui.BeginTabItem($"{label}###tab{tab}", flags)) return;
        if (ImGui.BeginChild($"##body{tab}", new Vector2(0, -footer)))
            body();
        ImGui.EndChild();
        ImGui.EndTabItem();
    }

    private void DrawFooter()
    {
        ImGui.TextColored(Theme.TextMuted, Loc.Localize("SecretsFooter", "Keys and secrets are stored in Windows Credential Manager, not in the settings file."));
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - 80));
        if (AccentButton(Loc.Localize("SaveBtn", "Save")))
            SaveAll();
    }

    // ═════ General ═══════════════════════════════════════════════════════

    private void DrawGeneralTab()
    {
        var inbound = config.InboundEnabled;
        if (ImGui.Checkbox(Loc.Localize("InboundLabel", "Translate received messages"), ref inbound)) config.InboundEnabled = inbound;
        var outbound = config.OutboundEnabled;
        if (ImGui.Checkbox(Loc.Localize("OutboundLabel", "Enable /agsend (translate before sending)"), ref outbound)) config.OutboundEnabled = outbound;
        var skipOwn = config.SkipOwnMessages;
        if (ImGui.Checkbox(Loc.Localize("SkipOwnLabel", "Skip my own messages"), ref skipOwn)) config.SkipOwnMessages = skipOwn;

        ImGui.Spacing();
        if (ImGui.Button(Loc.Localize("OpenLogBtn", "Open translation log")))
            plugin.OpenLog();

        Section(Loc.Localize("OutboundSection", "Send (/agsend)"));
        var prompt = config.OutboundPrompt;
        if (ImGui.InputTextMultiline("##outboundprompt", ref prompt, 2048, new Vector2(-1f, 100f)))
            config.OutboundPrompt = prompt;
        if (ImGui.Button(Loc.Localize("ResetPromptBtn", "Reset to default")))
            config.OutboundPrompt = Configuration.DefaultOutboundPrompt;

        var current = Channels.FirstOrDefault(c => c.Type == config.DefaultOutboundChannel);
        Label(Loc.Localize("DefaultOutboundChannelLabel", "Default send channel"));
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo("##defaultchannel", current.Fallback is null ? config.DefaultOutboundChannel.ToString() : Loc.Localize(current.LocKey, current.Fallback)))
        {
            foreach (var (type, locKey, fallback) in Channels.Where(c => c.Type != XivChatType.TellIncoming))
            {
                if (ImGui.Selectable(Loc.Localize(locKey, fallback), type == config.DefaultOutboundChannel))
                    config.DefaultOutboundChannel = type;
            }
            ImGui.EndCombo();
        }

        Section(Loc.Localize("CommandHelpSection", "Commands"));
        ImGui.TextColored(Theme.TextDim, "/aetherglot            " + Loc.Localize("HelpSettings", "settings"));
        ImGui.TextColored(Theme.TextDim, "/aetherglot log        " + Loc.Localize("HelpLog", "translation log"));
        ImGui.TextColored(Theme.TextDim, "/aetherglot test       " + Loc.Localize("HelpTest", "connection test"));
        ImGui.TextColored(Theme.TextDim, "/aetherglot lookup …   " + Loc.Localize("HelpLookup", "show dictionary hits"));
        ImGui.TextColored(Theme.TextDim, "/agsend [p|fc|l1…] …   " + Loc.Localize("HelpSend", "translate and send"));
    }

    // ═════ Connection ════════════════════════════════════════════════════

    private void DrawConnectionTab()
    {
        if (!ImGui.BeginTable("##conn", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("form", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("test", ImGuiTableColumnFlags.WidthFixed, 300f);
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        DrawConnectionForm();

        ImGui.TableNextColumn();
        DrawConnectionTest();

        ImGui.EndTable();
    }

    private void DrawConnectionForm()
    {
        var c = config.Connection;

        Label(Loc.Localize("ModeLabel", "Connection"));
        foreach (var (mode, locKey, fallback) in Modes)
        {
            if (Toggle(Loc.Localize(locKey, fallback), c.Mode == mode)) c.Mode = mode;
            ImGui.SameLine(0, 2);
        }
        ImGui.NewLine();

        switch (c.Mode)
        {
            case ConnectionMode.Direct:
                Section(Loc.Localize("DirectSection", "Vendor API"));
                Label(Loc.Localize("ProviderLabel", "Provider"));
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##provider", c.DirectProvider.ToString()))
                {
                    foreach (var p in Enum.GetValues<DirectProvider>())
                        if (ImGui.Selectable(p.ToString(), p == c.DirectProvider)) c.DirectProvider = p;
                    ImGui.EndCombo();
                }
                var key = c.DirectProvider.ToString();
                var endpoint = c.DirectEndpoints.TryGetValue(key, out var e) ? e : string.Empty;
                if (LabeledInput(Loc.Localize("EndpointLabel", "Endpoint"), "##endpoint", ref endpoint)) c.DirectEndpoints[key] = endpoint;
                SecretInput(Loc.Localize("ApiKeyLabel", "API key"), SecretNames.DirectApiKey(key));
                break;

            case ConnectionMode.LiteLlm:
                Section("LiteLLM");
                var liteUrl = c.LiteLlmBaseUrl;
                if (LabeledInput(Loc.Localize("BaseUrlLabel", "Base URL"), "##liteurl", ref liteUrl, "https://llm.example.com")) c.LiteLlmBaseUrl = liteUrl;
                SecretInput(Loc.Localize("VirtualKeyLabel", "Virtual key"), SecretNames.LiteLlmKey);
                var tag = c.UserTag;
                if (LabeledInput(Loc.Localize("UserTagLabel", "User tag"), "##usertag", ref tag)) c.UserTag = tag;
                break;

            case ConnectionMode.OpenAICompatible:
                Section(Loc.Localize("ModeOpenAICompatible", "OpenAI-compatible"));
                var compatUrl = c.OpenAICompatibleBaseUrl;
                if (LabeledInput(Loc.Localize("BaseUrlLabel", "Base URL"), "##compaturl", ref compatUrl, "http://localhost:11434")) c.OpenAICompatibleBaseUrl = compatUrl;
                SecretInput(Loc.Localize("ApiKeyOptionalLabel", "API key (optional)"), SecretNames.OpenAICompatibleKey);
                var tag2 = c.UserTag;
                if (LabeledInput(Loc.Localize("UserTagLabel", "User tag"), "##usertag2", ref tag2)) c.UserTag = tag2;
                break;
        }

        if (c.Mode != ConnectionMode.Direct)
            DrawCloudflareAccess(c);

        DrawModels(c);
    }

    private void DrawCloudflareAccess(ConnectionConfig c)
    {
        Section(Loc.Localize("CfSection", "Cloudflare Access (service token)"));
        var cf = c.CloudflareAccessEnabled;
        if (ImGui.Checkbox(Loc.Localize("CfEnabledLabel", "Use") + "##cf", ref cf)) c.CloudflareAccessEnabled = cf;
        if (c.CloudflareAccessEnabled)
        {
            SecretInput("Client ID", SecretNames.CfClientId);
            SecretInput("Client Secret", SecretNames.CfClientSecret);
            Label(Loc.Localize("CfHeadersLabel", "Headers sent"));
            ImGui.TextColored(Theme.TextDim, $"{RequestDecorator.CfClientIdHeader} / {RequestDecorator.CfClientSecretHeader}");
        }

        Label(Loc.Localize("ExtraHeadersLabel", "Extra headers"));
        if (ImGui.SmallButton(Loc.Localize("AddHeaderBtn", "+ Add header")))
        {
            headerRows.Add((string.Empty, string.Empty));
            headersDirty = true;
        }
        for (var i = 0; i < headerRows.Count; i++)
        {
            var (name, value) = headerRows[i];
            ImGui.PushID(i);
            ImGui.Dummy(new Vector2(LabelWidth, 0));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputTextWithHint("##hname", Loc.Localize("HeaderName", "Name"), ref name, 128)) headersDirty = true;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Math.Max(80, ImGui.GetContentRegionAvail().X - 30));
            if (ImGui.InputTextWithHint("##hvalue", Loc.Localize("HeaderValue", "Value"), ref value, 512, ImGuiInputTextFlags.Password)) headersDirty = true;
            headerRows[i] = (name, value);
            ImGui.SameLine();
            if (ImGui.SmallButton("×"))
            {
                headerRows.RemoveAt(i);
                headersDirty = true;
                ImGui.PopID();
                break;
            }
            ImGui.PopID();
        }
    }

    private void DrawModels(ConnectionConfig c)
    {
        Section(Loc.Localize("ModelsSection", "Models"));
        if (c.Mode != ConnectionMode.Direct)
        {
            if (fetchingModels) ImGui.BeginDisabled();
            if (ImGui.SmallButton(c.Mode == ConnectionMode.LiteLlm
                    ? Loc.Localize("FetchModelsLiteLlm", "Fetch list from LiteLLM")
                    : Loc.Localize("FetchModelsBtn", "Fetch model list")))
            {
                SaveSecrets();
                fetchingModels = true;
                fetchError = null;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        fetchedModels = await plugin.ListModelsAsync(echo: false).ConfigureAwait(false);
                        if (fetchedModels.Count == 0) fetchError = Loc.Localize("FetchModelsEmpty", "No models returned. Run the connection test for details.");
                    }
                    finally { fetchingModels = false; }
                });
            }
            if (fetchingModels) ImGui.EndDisabled();
            ImGui.SameLine();
            if (fetchedModels.Count > 0) ImGui.TextColored(Theme.TextMuted, string.Format(Loc.Localize("ModelsCount", "{0} models"), fetchedModels.Count));
            else if (fetchError is { } err) ImGui.TextColored(Theme.Warn, err);
            else ImGui.NewLine();
        }

        ModelInput(Loc.Localize("InboundModelLabel", "Inbound (fast)"), "in", c.InboundModel, v => c.InboundModel = v);
        ModelInput(Loc.Localize("OutboundModelLabel", "Outbound (quality)"), "out", c.OutboundModel, v => c.OutboundModel = v);

        var json = c.UseJsonResponseFormat;
        if (ImGui.Checkbox(Loc.Localize("JsonModeLabel", "Request JSON format (models that reject it are instructed by prompt)"), ref json))
            c.UseJsonResponseFormat = json;
    }

    private void ModelInput(string label, string id, string value, Action<string> set)
    {
        Label(label);
        var buf = value;
        var models = fetchedModels;
        ImGui.SetNextItemWidth(Math.Max(120, ImGui.GetContentRegionAvail().X - (models.Count > 0 ? 34 : 0)));
        if (ImGui.InputText($"##model{id}", ref buf, 128)) set(buf.Trim());
        if (models.Count == 0) return;

        ImGui.SameLine(0, 4);
        ImGui.SetNextItemWidth(30);
        if (ImGui.BeginCombo($"##modelpick{id}", string.Empty, ImGuiComboFlags.NoPreview | ImGuiComboFlags.HeightLarge))
        {
            foreach (var m in models)
                if (ImGui.Selectable(m, m == value)) set(m);
            ImGui.EndCombo();
        }
    }

    private void DrawConnectionTest()
    {
        ImGui.TextUnformatted(Loc.Localize("TestTitle", "Connection test"));
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - 60));
        if (testRunning) ImGui.BeginDisabled();
        if (AccentButton(testRunning ? "…" : Loc.Localize("RunTestBtn", "Run")))
        {
            SaveSecrets();
            testRunning = true;
            _ = Task.Run(async () =>
            {
                try { lastReport = await plugin.RunConnectionTestAsync(echo: false).ConfigureAwait(false); }
                finally { testRunning = false; }
            });
        }
        if (testRunning) ImGui.EndDisabled();
        ImGui.Separator();

        var steps = lastReport?.Steps;
        foreach (var kind in Enum.GetValues<DiagnosticStepKind>())
        {
            var step = steps?.FirstOrDefault(s => s.Kind == kind);
            var (mark, color) = step?.Status switch
            {
                DiagnosticStatus.Ok      => ("✓", Theme.Ok),
                DiagnosticStatus.Warning => ("!", Theme.Warn),
                DiagnosticStatus.Failed  => ("×", Theme.Error),
                DiagnosticStatus.Skipped => ("–", Theme.TextMuted),
                _                        => ("·", Theme.TextMuted),
            };
            ImGui.TextColored(color, mark);
            ImGui.SameLine();
            ImGui.TextUnformatted(Plugin.StepLabel(kind));
            if (step is null) continue;

            var detail = step.StatusCode is { } code && step.Status == DiagnosticStatus.Failed ? $"HTTP {code}" : step.Detail;
            ImGui.Indent(18);
            ImGui.PushTextWrapPos();
            ImGui.TextColored(step.Status == DiagnosticStatus.Failed ? Theme.Error : Theme.TextMuted, detail);
            if (step.Hint is not null && step.Status is DiagnosticStatus.Failed or DiagnosticStatus.Warning)
                ImGui.TextColored(Theme.Warn, step.Hint);
            ImGui.PopTextWrapPos();
            ImGui.Unindent(18);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.Localize("TestHowToRead", "Reading failures"));
        ImGui.PushTextWrapPos();
        ImGui.TextColored(Theme.TextDim, Loc.Localize("TestHint302", "302 / 403: check that the Cloudflare Access policy uses Service Auth."));
        ImGui.TextColored(Theme.TextDim, Loc.Localize("TestHint401", "401: the LiteLLM virtual key is invalid or expired."));
        ImGui.TextColored(Theme.TextDim, Loc.Localize("TestHintModel", "Model not found: check that the virtual key may use that model."));
        ImGui.Spacing();
        ImGui.TextColored(Theme.TextMuted, Loc.Localize("SecretsNotice", "Keys and secrets are stored in Windows Credential Manager, not in the configuration file."));
        ImGui.PopTextWrapPos();
    }

    // ═════ AI & translation ══════════════════════════════════════════════

    private void DrawAiTab()
    {
        var t = config.Translation;
        var c = config.Connection;

        // One-line summary of the connection
        var dot = plugin.TranslationLog.LastCallOk switch { true => Theme.Ok, false => Theme.Error, null => Theme.TextMuted };
        ImGui.TextColored(dot, "●");
        ImGui.SameLine();
        ImGui.TextUnformatted(ConnectionSummary(c));
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.Localize("ChangeInConnectionTab", "Change in Connection tab")))
            selectTab = Tab.Connection;

        Section(Loc.Localize("UnresolvedSection", "When some words cannot be translated"));
        Radio(Loc.Localize("UnresolvedPartial", "Translate what can be translated and mark the rest in the original (recommended)"), UnresolvedDisplayMode.PartialMarked);
        Radio(Loc.Localize("UnresolvedOriginal", "Show only the original"), UnresolvedDisplayMode.OriginalOnly);
        Radio(Loc.Localize("UnresolvedHide", "Show nothing"), UnresolvedDisplayMode.Hide);
        var discard = t.DiscardRefusals;
        if (ImGui.Checkbox(Loc.Localize("DiscardRefusals", "Discard refusals / apologies (\"cannot translate\") and show the original"), ref discard))
            t.DiscardRefusals = discard;

        Section(Loc.Localize("FreshTermsSection", "Keep terms up to date"));
        var game = t.UseGameData;
        if (ImGui.Checkbox(Loc.Localize("UseGameData", "Load auto-translate phrases and action names from the game data (follows patches)"), ref game))
            t.UseGameData = game;
        var auto = t.CommunityAutoUpdate;
        if (ImGui.Checkbox(Loc.Localize("CommunityAutoUpdate", "Update the community dictionary at startup"), ref auto))
            t.CommunityAutoUpdate = auto;
        var hints = t.SendContextHints;
        if (ImGui.Checkbox(Loc.Localize("SendContextHints", "Send location, combat state and party jobs as hints"), ref hints))
            t.SendContextHints = hints;
        var url = t.CommunityGlossaryUrl;
        if (LabeledInput(Loc.Localize("CommunityUrl", "Community dictionary URL"), "##communityurl", ref url)) t.CommunityGlossaryUrl = url.Trim();
    }

    private static string ConnectionSummary(ConnectionConfig c)
    {
        var mode = c.Mode switch
        {
            ConnectionMode.LiteLlm when c.CloudflareAccessEnabled => Loc.Localize("SummaryLiteLlmCf", "LiteLLM (via Cloudflare Access)"),
            ConnectionMode.LiteLlm                                => "LiteLLM",
            ConnectionMode.OpenAICompatible                       => Loc.Localize("ModeOpenAICompatible", "OpenAI-compatible"),
            _                                                     => c.DirectProvider.ToString(),
        };
        var outbound = string.IsNullOrWhiteSpace(c.OutboundModel) ? c.InboundModel : c.OutboundModel;
        return string.Format(Loc.Localize("SummaryLine", "{0} · inbound {1} / outbound {2}"), mode,
                             string.IsNullOrWhiteSpace(c.InboundModel) ? "-" : c.InboundModel,
                             string.IsNullOrWhiteSpace(outbound) ? "-" : outbound);
    }

    private void Radio(string label, UnresolvedDisplayMode mode)
    {
        if (ImGui.RadioButton(label, config.Translation.UnresolvedMode == mode))
            config.Translation.UnresolvedMode = mode;
    }

    // ═════ Channels ══════════════════════════════════════════════════════

    private void DrawChannelsTab()
    {
        ImGui.TextColored(Theme.TextDim, Loc.Localize("ChannelsHelp", "Messages in these channels are translated. Where they are shown is set in the Display tab."));
        ImGui.Spacing();
        const int columns = 4;
        if (!ImGui.BeginTable("##channels", columns)) return;
        foreach (var (type, locKey, fallback) in Channels)
        {
            ImGui.TableNextColumn();
            var enabled = config.EnabledChannels.Contains(type);
            if (ImGui.Checkbox(Loc.Localize(locKey, fallback), ref enabled))
            {
                if (enabled) config.EnabledChannels.Add(type);
                else config.EnabledChannels.Remove(type);
            }
        }
        ImGui.EndTable();
    }

    // ═════ Dictionary (full editor in phase 5) ═══════════════════════════

    private void DrawDictionaryTab()
    {
        var stats = plugin.Glossary.Service.Stats;
        Label(Loc.Localize("DictMine", "My dictionary"));
        ImGui.TextUnformatted($"{stats.UserCount:N0}");
        Label(Loc.Localize("DictGame", "Game data"));
        ImGui.TextUnformatted($"{stats.GameDataCount:N0}  ({Loc.Localize("DictAutoTranslate", "auto-translate")} {plugin.Glossary.Catalog.Entries.Count:N0})");
        Label(Loc.Localize("DictCommunity", "Community"));
        ImGui.TextUnformatted($"{stats.CommunityCount:N0}  v{stats.CommunityVersion ?? "-"}  ({plugin.Glossary.Community?.Origin})");
        Label(Loc.Localize("DictPending", "Pending"));
        ImGui.TextUnformatted($"{plugin.Glossary.Pending.Items.Count:N0}");

        ImGui.Spacing();
        if (reloadingGlossary) ImGui.BeginDisabled();
        if (ImGui.Button(Loc.Localize("DictUpdateNow", "Update now")))
        {
            reloadingGlossary = true;
            _ = Task.Run(async () =>
            {
                try { await plugin.Glossary.ReloadAsync(forceDownload: true).ConfigureAwait(false); }
                finally { reloadingGlossary = false; }
            });
        }
        if (reloadingGlossary) ImGui.EndDisabled();

        if (plugin.Glossary.Community?.Error is { } error)
            ImGui.TextColored(Theme.Warn, error);
    }

    // ═════ Display ═══════════════════════════════════════════════════════

    private void DrawDisplayTab()
    {
        var d = config.Display;

        Label(Loc.Localize("PresetLabel", "Preset"));
        PresetButton(d, DisplayPreset.Recommended, Loc.Localize("PresetRecommended", "Recommended"));
        PresetButton(d, DisplayPreset.WindowOnly, Loc.Localize("PresetWindow", "Window only"));
        PresetButton(d, DisplayPreset.GameChatOnly, Loc.Localize("PresetGameChat", "Game chat only"));
        PresetButton(d, DisplayPreset.BubbleOnly, Loc.Localize("PresetBubble", "Bubbles only"));
        if (d.Preset == DisplayPreset.Custom)
            ImGui.TextColored(Theme.Accent, Loc.Localize("PresetCustom", "Custom"));
        else
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("PresetHint", "Editing the table switches to \"Custom\"."));

        Section(Loc.Localize("RoutesSection", "Where each channel is shown"));
        DrawRouteTable(d);
        ImGui.TextColored(Theme.TextMuted, Loc.Localize("BubbleOnlyParty", "Bubbles attach to the party list, so only Party and Alliance can use them."));

        ImGui.Spacing();
        if (ImGui.BeginTable("##cards", 3, ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableNextColumn();
            DrawBubbleCard(d.Bubble);
            ImGui.TableNextColumn();
            DrawLogCard(d.Log);
            ImGui.TableNextColumn();
            DrawGameChatCard(d.GameChat);
            ImGui.EndTable();
        }

        ImGui.Spacing();
        if (ImGui.Button(Loc.Localize("TestDisplayBtn", "Test display")))
            plugin.ShowSampleMessages();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.Localize("TestDisplayTip", "Adds sample messages to the translation log (no model call)."));
    }

    private void PresetButton(DisplayConfig d, DisplayPreset preset, string label)
    {
        if (Toggle(label, d.Preset == preset)) d.ApplyPreset(preset);
        ImGui.SameLine(0, 4);
    }

    private static readonly string[] CombatModeKeys = ["CombatAll", "CombatMentions", "CombatStop"];
    private static readonly string[] CombatModeFallbacks = ["All", "To me only", "Stop"];

    private static void DrawRouteTable(DisplayConfig d)
    {
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##routes", 5, flags)) return;
        ImGui.TableSetupColumn(Loc.Localize("ColChannel", "Channel"), ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn(Loc.Localize("ColBubble", "Bubble"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(Loc.Localize("ColLog", "Translation log"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(Loc.Localize("ColGameChat", "Game chat"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(Loc.Localize("ColCombat", "In combat"), ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableHeadersRow();

        var modes = CombatModeKeys.Select((k, i) => Loc.Localize(k, CombatModeFallbacks[i])).ToArray();
        foreach (var group in Enum.GetValues<ChannelGroup>())
        {
            if (!d.Routes.TryGetValue(group, out var route))
                d.Routes[group] = route = new ChannelRoute();

            ImGui.PushID((int)group);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(ChannelInfo.Color(group), ChannelInfo.GroupLabel(group));

            ImGui.TableNextColumn();
            if (DisplayConfig.SupportsBubble(group))
            {
                var b = route.Bubble;
                if (ImGui.Checkbox("##bubble", ref b)) { route.Bubble = b; d.OnTableEdited(); }
            }
            else
            {
                ImGui.TextColored(Theme.TextMuted, "—");
            }

            ImGui.TableNextColumn();
            var l = route.Log;
            if (ImGui.Checkbox("##log", ref l)) { route.Log = l; d.OnTableEdited(); }

            ImGui.TableNextColumn();
            var g = route.GameChat;
            if (ImGui.Checkbox("##chat", ref g)) { route.GameChat = g; d.OnTableEdited(); }

            ImGui.TableNextColumn();
            var mode = (int)route.InCombat;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##combat", ref mode, modes))
            {
                route.InCombat = (CombatMode)mode;
                d.OnTableEdited();
            }
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private static void DrawBubbleCard(BubbleSettings b)
    {
        ImGui.TextUnformatted(Loc.Localize("CardBubble", "Bubbles"));
        var seconds = b.Seconds;
        ImGui.SetNextItemWidth(90);
        if (ImGui.InputFloat(Loc.Localize("BubbleSeconds", "seconds shown"), ref seconds, 0.5f, 1f, "%.1f"))
            b.Seconds = Math.Clamp(seconds, 1f, 60f);
        var max = b.MaxPerMember;
        ImGui.SetNextItemWidth(90);
        if (ImGui.InputInt(Loc.Localize("BubbleMax", "max per member"), ref max))
            b.MaxPerMember = Math.Clamp(max, 1, 5);
        var extend = b.ExtendByLength;
        if (ImGui.Checkbox(Loc.Localize("BubbleExtend", "Longer for long text"), ref extend)) b.ExtendByLength = extend;

        ImGui.Spacing();
        var history = b.ShowHistory;
        if (ImGui.Checkbox(Loc.Localize("BubbleHistory", "Recent 3 at bottom right"), ref history)) b.ShowHistory = history;
        DrawHotkey(b.HistoryHotkey);
    }

    private static readonly string[] HotkeyKeys =
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString())
                  .Concat(Enumerable.Range(1, 12).Select(i => $"F{i}"))
                  .ToArray();

    private static void DrawHotkey(HotkeySettings hk)
    {
        ImGui.TextColored(Theme.TextDim, Loc.Localize("HotkeyLabel", "Toggle key"));
        var ctrl = hk.Ctrl;
        if (ImGui.Checkbox("Ctrl##hk", ref ctrl)) hk.Ctrl = ctrl;
        ImGui.SameLine();
        var shift = hk.Shift;
        if (ImGui.Checkbox("Shift##hk", ref shift)) hk.Shift = shift;
        ImGui.SameLine();
        var alt = hk.Alt;
        if (ImGui.Checkbox("Alt##hk", ref alt)) hk.Alt = alt;
        ImGui.SameLine();
        var index = Math.Max(0, Array.IndexOf(HotkeyKeys, hk.Key.ToUpperInvariant()));
        ImGui.SetNextItemWidth(60);
        if (ImGui.Combo("##hkkey", ref index, HotkeyKeys)) hk.Key = HotkeyKeys[index];
    }

    private static void DrawLogCard(LogSettings l)
    {
        ImGui.TextUnformatted(Loc.Localize("CardLog", "Translation log"));
        var oneLine = l.OneLine;
        if (ImGui.Checkbox(Loc.Localize("LogOneLine", "One line (click for original)"), ref oneLine)) l.OneLine = oneLine;
        var pause = l.PauseWhileScrolled;
        if (ImGui.Checkbox(Loc.Localize("LogPause", "Pause while scrolled up"), ref pause)) l.PauseWhileScrolled = pause;
        var hl = l.HighlightMentions;
        if (ImGui.Checkbox(Loc.Localize("LogHighlight", "Highlight messages to me"), ref hl)) l.HighlightMentions = hl;
    }

    private static readonly string[] OutputKeys = ["OutputOriginal", "OutputEcho"];
    private static readonly string[] OutputFallbacks = ["Original channel", "Echo"];

    private static void DrawGameChatCard(GameChatSettings g)
    {
        ImGui.TextUnformatted(Loc.Localize("CardGameChat", "Game chat"));
        var output = (int)g.Output;
        ImGui.SetNextItemWidth(130);
        if (ImGui.Combo(Loc.Localize("GameChatOutput", "output"), ref output, OutputKeys.Select((k, i) => Loc.Localize(k, OutputFallbacks[i])).ToArray()))
            g.Output = (GameChatOutput)output;
        var prefix = g.Prefix;
        ImGui.SetNextItemWidth(90);
        if (ImGui.InputText(Loc.Localize("GameChatPrefix", "line prefix"), ref prefix, 32)) g.Prefix = prefix;
        var replace = g.ReplaceOriginal;
        if (ImGui.Checkbox(Loc.Localize("GameChatReplace", "Replace the original with the translation"), ref replace)) g.ReplaceOriginal = replace;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.Localize("GameChatReplaceTip", "Only possible when the translation is already cached; otherwise the translation is added as a new line."));
    }

    // ═════ Helpers ═══════════════════════════════════════════════════════

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(Theme.TextMuted, title);
        ImGui.Separator();
    }

    private static void Label(string text)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextDim, text);
        ImGui.SameLine(LabelWidth);
    }

    private static bool LabeledInput(string label, string id, ref string value, string? hint = null)
    {
        Label(label);
        ImGui.SetNextItemWidth(-1);
        return hint is null
            ? ImGui.InputText(id, ref value, 512)
            : ImGui.InputTextWithHint(id, hint, ref value, 512);
    }

    private static bool AccentButton(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentText);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(2);
        return clicked;
    }

    private static bool Toggle(string label, bool selected)
    {
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentText);
        }
        var clicked = ImGui.Button(label);
        if (selected) ImGui.PopStyleColor(2);
        return clicked;
    }

    private string ReadSecret(string name)
    {
        try { return plugin.Secrets.Get(name) ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>Masked text box bound to a secret; written to the store on Save.</summary>
    private void SecretInput(string label, string secretName)
    {
        if (!secretBuffers.TryGetValue(secretName, out var buf))
            secretBuffers[secretName] = buf = ReadSecret(secretName);

        Label(label);
        var visible = visibleSecrets.Contains(secretName);
        var flags   = visible ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
        ImGui.SetNextItemWidth(Math.Max(80, ImGui.GetContentRegionAvail().X - 60));
        if (ImGui.InputText($"##{secretName}", ref buf, 512, flags))
        {
            secretBuffers[secretName] = buf;
            dirtySecrets.Add(secretName);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton($"{(visible ? Loc.Localize("HideBtn", "Hide") : Loc.Localize("ShowBtn", "Show"))}##{secretName}"))
        {
            if (visible) visibleSecrets.Remove(secretName);
            else visibleSecrets.Add(secretName);
        }
    }

    private void SaveSecrets()
    {
        foreach (var name in dirtySecrets.ToList())
        {
            try
            {
                plugin.Secrets.Set(name, secretBuffers[name].Trim());
                dirtySecrets.Remove(name);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("[AetherGlot] Could not save a secret to Credential Manager ({Type}).", ex.GetType().Name);
            }
        }

        if (!headersDirty) return;
        try
        {
            var rows = headerRows.Where(r => !string.IsNullOrWhiteSpace(r.Name))
                                 .GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                                 .Select(g => (Name: g.Key, g.Last().Value))
                                 .ToList();
            foreach (var removed in loadedHeaderNames.Except(rows.Select(r => r.Name), StringComparer.OrdinalIgnoreCase))
                plugin.Secrets.Delete(SecretNames.ExtraHeader(removed));
            foreach (var (name, value) in rows)
                plugin.Secrets.Set(SecretNames.ExtraHeader(name), value);

            config.Connection.ExtraHeaderNames = rows.Select(r => r.Name).ToList();
            loadedHeaderNames = config.Connection.ExtraHeaderNames.ToList();
            headersDirty = false;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error("[AetherGlot] Could not save extra headers ({Type}).", ex.GetType().Name);
        }
    }

    private void SaveAll()
    {
        SaveSecrets();
        plugin.TranslationLog.Capacity = config.Display.Log.Capacity;
        plugin.Pipeline.Cache.Capacity = config.Translation.CacheSize;
        config.Save();
    }

    public void Dispose() { }
}
