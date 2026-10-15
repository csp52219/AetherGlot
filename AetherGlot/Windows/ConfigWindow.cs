using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherGlot.Windows;

/// <summary>Tabs of the settings window (for opening it at a given tab).</summary>
public enum SettingsTab { General, Connection, Ai, Channels, Dictionary, Display }

/// <summary>⑤ Settings: General / Connection / AI & translation / Channels / Dictionary / Display.</summary>
public sealed class ConfigWindow : Window, IDisposable, IRelocalizable
{
    private static readonly TimeSpan SavedNoticeTime = TimeSpan.FromSeconds(4);

    private readonly Plugin plugin;
    private readonly Configuration config;
    private SettingsTab? selectTab;

    // Result of the last save: shown next to the Save button
    private DateTimeOffset? savedAt;
    private IReadOnlyList<string> saveFailures = [];
    private DateTimeOffset? copiedAt;

    // Secret edit buffers, keyed by secret name; loaded lazily from the secret store
    private readonly Dictionary<string, string> secretBuffers = new(StringComparer.Ordinal);
    private readonly HashSet<string> dirtySecrets = new(StringComparer.Ordinal);
    private readonly HashSet<string> visibleSecrets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> secretLabels = new(StringComparer.Ordinal);

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
        : base(Title())
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

    /// <summary>Opens the window at <paramref name="tab"/>.</summary>
    public void OpenAt(SettingsTab tab)
    {
        selectTab = tab;
        IsOpen = true;
    }

    public override void Draw()
    {
        var footer = ImGui.GetFrameHeightWithSpacing() + 6;
        if (ImGui.BeginTabBar("##settingsTabs"))
        {
            DrawTab(SettingsTab.General,    Tr.Get("TabGeneral", "General"),            DrawGeneralTab, footer);
            DrawTab(SettingsTab.Connection, Tr.Get("TabConnection", "Connection"),      DrawConnectionTab, footer);
            DrawTab(SettingsTab.Ai,         Tr.Get("TabAi", "AI & translation"),        DrawAiTab, footer);
            DrawTab(SettingsTab.Channels,   Tr.Get("TabChannels", "Channels"),          DrawChannelsTab, footer);
            DrawTab(SettingsTab.Dictionary, Tr.Get("TabDictionary", "Dictionary"),      DrawDictionaryTab, footer);
            DrawTab(SettingsTab.Display,    Tr.Get("TabDisplay", "Display"),            DrawDisplayTab, footer);
            ImGui.EndTabBar();
        }
        selectTab = null;

        ImGui.Separator();
        DrawFooter();
    }

    private void DrawTab(SettingsTab tab, string label, Action body, float footer)
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
        ImGui.TextColored(Theme.TextMuted, Tr.Get("SecretsFooter", "Keys and secrets are stored in Windows Credential Manager, not in the settings file."));

        // Save result, right before the button: failures stay until the next save
        string? status = null;
        var color = Theme.Ok;
        if (saveFailures.Count > 0)
        {
            status = string.Format(Tr.Get("SaveFailed", "! Could not save: {0} (see dalamud.log)"), string.Join(", ", saveFailures));
            color  = Theme.Warn;
        }
        else if (savedAt is { } at && DateTimeOffset.Now - at < SavedNoticeTime)
        {
            status = string.Format(Tr.Get("SaveDone", "✓ Saved ({0:HH:mm:ss})"), at.ToLocalTime());
        }

        var buttonWidth = ImGui.CalcTextSize(Tr.Get("SaveBtn", "Save")).X + ImGui.GetStyle().FramePadding.X * 2;
        var right = ImGui.GetWindowContentRegionMax().X;
        if (status is not null)
        {
            ImGui.SameLine();
            var statusWidth = ImGui.CalcTextSize(status).X;
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), right - buttonWidth - statusWidth - 16));
            ImGui.TextColored(color, status);
        }
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), right - buttonWidth));
        if (AccentButton(Tr.Get("SaveBtn", "Save")))
            SaveAll();
    }

    // ═════ General ═══════════════════════════════════════════════════════

    private void DrawGeneralTab()
    {
        var inbound = config.InboundEnabled;
        if (ImGui.Checkbox(Tr.Get("InboundLabel", "Translate received messages"), ref inbound)) config.InboundEnabled = inbound;
        var outbound = config.OutboundEnabled;
        if (ImGui.Checkbox(Tr.Get("OutboundLabel", "Enable /agsend (translate before sending)"), ref outbound)) config.OutboundEnabled = outbound;
        var skipOwn = config.SkipOwnMessages;
        if (ImGui.Checkbox(Tr.Get("SkipOwnLabel", "Skip my own messages"), ref skipOwn)) config.SkipOwnMessages = skipOwn;

        ImGui.Spacing();
        if (ImGui.Button(Tr.Get("OpenLogBtn", "Open translation log")))
            plugin.OpenLog();

        Section(Tr.Get("OutboundSection", "Send (/agsend)"));
        ImGui.TextColored(Theme.TextMuted, Tr.Get("OutboundPromptHint",
            "Extra style instructions for the model (optional), e.g. \"Use British spelling\". Short / normal / polite is chosen in the send window."));
        var prompt = config.OutboundPrompt;
        if (ImGui.InputTextMultiline("##outboundprompt", ref prompt, 2048, new Vector2(-1f, 80f)))
            config.OutboundPrompt = prompt;
        if (ImGui.Button(Tr.Get("ResetPromptBtn", "Clear")))
            config.OutboundPrompt = OutboundPromptDefaults.Default;

        var current = Channels.FirstOrDefault(c => c.Type == config.DefaultOutboundChannel);
        Label(Tr.Get("DefaultOutboundChannelLabel", "Default send channel"));
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo("##defaultchannel", current.Fallback is null ? config.DefaultOutboundChannel.ToString() : Tr.Get(current.LocKey, current.Fallback)))
        {
            foreach (var (type, locKey, fallback) in Channels.Where(c => c.Type != XivChatType.TellIncoming))
            {
                if (ImGui.Selectable(Tr.Get(locKey, fallback), type == config.DefaultOutboundChannel))
                    config.DefaultOutboundChannel = type;
            }
            ImGui.EndCombo();
        }

        Section(Tr.Get("CommandHelpSection", "Commands"));
        ImGui.TextColored(Theme.TextDim, "/aetherglot            " + Tr.Get("HelpSettings", "settings"));
        ImGui.TextColored(Theme.TextDim, "/aetherglot log        " + Tr.Get("HelpLog", "translation log"));
        ImGui.TextColored(Theme.TextDim, "/aetherglot test       " + Tr.Get("HelpTest", "connection test"));
        ImGui.TextColored(Theme.TextDim, "/aetherglot lookup …   " + Tr.Get("HelpLookup", "show dictionary hits"));
        ImGui.TextColored(Theme.TextDim, "/agsend [p|fc|l1…] …   " + Tr.Get("HelpSend", "translate and send"));

        DrawDiagnosticsSection();
    }

    private void DrawDiagnosticsSection()
    {
        var d = config.Diagnostics;
        Section(Tr.Get("DiagSection", "Troubleshooting"));
        ImGui.TextColored(Theme.TextMuted, Tr.Get("DiagWhere",
            "Start-up, sends, saves, warnings and errors are always written to dalamud.log (open it with /xllog)."));

        var verbose = d.VerboseLogging;
        if (ImGui.Checkbox(Tr.Get("DiagVerbose", "Detailed log: route and result of every message"), ref verbose))
            d.VerboseLogging = verbose;

        var disabled = !d.VerboseLogging;
        if (disabled) ImGui.BeginDisabled();
        var text = d.LogMessageText;
        if (ImGui.Checkbox(Tr.Get("DiagText", "Also write message text and names (other players' chat)"), ref text))
            d.LogMessageText = text;
        if (disabled) ImGui.EndDisabled();

        ImGui.Spacing();
        if (ImGui.Button(Tr.Get("DiagCopy", "Copy diagnostics")))
        {
            ImGui.SetClipboardText(DiagnosticsSummary.Build(plugin));
            copiedAt = DateTimeOffset.Now;
        }
        ImGui.SameLine();
        if (copiedAt is { } at && DateTimeOffset.Now - at < SavedNoticeTime)
            ImGui.TextColored(Theme.Ok, Tr.Get("DiagCopied", "✓ Copied. Paste it into your bug report (no keys or secrets included)."));
        else
            ImGui.TextColored(Theme.TextMuted, Tr.Get("DiagCopyHint", "Settings summary, last connection test and recent errors; keys and secrets are never included."));
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

        Label(Tr.Get("ModeLabel", "Connection"));
        foreach (var (mode, locKey, fallback) in Modes)
        {
            if (Toggle(Tr.Get(locKey, fallback), c.Mode == mode)) c.Mode = mode;
            ImGui.SameLine(0, 2);
        }
        ImGui.NewLine();

        switch (c.Mode)
        {
            case ConnectionMode.Direct:
                Section(Tr.Get("DirectSection", "Vendor API"));
                Label(Tr.Get("ProviderLabel", "Provider"));
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##provider", c.DirectProvider.ToString()))
                {
                    foreach (var p in Enum.GetValues<DirectProvider>())
                        if (ImGui.Selectable(p.ToString(), p == c.DirectProvider)) c.DirectProvider = p;
                    ImGui.EndCombo();
                }
                var key = c.DirectProvider.ToString();
                var endpoint = c.DirectEndpoints.TryGetValue(key, out var e) ? e : string.Empty;
                if (LabeledInput(Tr.Get("EndpointLabel", "Endpoint"), "##endpoint", ref endpoint)) c.DirectEndpoints[key] = endpoint;
                SecretInput(Tr.Get("ApiKeyLabel", "API key"), SecretNames.DirectApiKey(key));
                break;

            case ConnectionMode.LiteLlm:
                Section("LiteLLM");
                var liteUrl = c.LiteLlmBaseUrl;
                if (LabeledInput(Tr.Get("BaseUrlLabel", "Base URL"), "##liteurl", ref liteUrl, "https://llm.example.com")) c.LiteLlmBaseUrl = liteUrl;
                SecretInput(Tr.Get("VirtualKeyLabel", "Virtual key"), SecretNames.LiteLlmKey);
                var tag = c.UserTag;
                if (LabeledInput(Tr.Get("UserTagLabel", "User tag"), "##usertag", ref tag)) c.UserTag = tag;
                break;

            case ConnectionMode.OpenAICompatible:
                Section(Tr.Get("ModeOpenAICompatible", "OpenAI-compatible"));
                var compatUrl = c.OpenAICompatibleBaseUrl;
                if (LabeledInput(Tr.Get("BaseUrlLabel", "Base URL"), "##compaturl", ref compatUrl, "http://localhost:11434")) c.OpenAICompatibleBaseUrl = compatUrl;
                SecretInput(Tr.Get("ApiKeyOptionalLabel", "API key (optional)"), SecretNames.OpenAICompatibleKey);
                var tag2 = c.UserTag;
                if (LabeledInput(Tr.Get("UserTagLabel", "User tag"), "##usertag2", ref tag2)) c.UserTag = tag2;
                break;
        }

        if (c.Mode != ConnectionMode.Direct)
            DrawCloudflareAccess(c);

        DrawModels(c);
    }

    private void DrawCloudflareAccess(ConnectionConfig c)
    {
        Section(Tr.Get("CfSection", "Cloudflare Access (service token)"));
        var cf = c.CloudflareAccessEnabled;
        if (ImGui.Checkbox(Tr.Get("CfEnabledLabel", "Use") + "##cf", ref cf)) c.CloudflareAccessEnabled = cf;
        if (c.CloudflareAccessEnabled)
        {
            SecretInput("Client ID", SecretNames.CfClientId);
            SecretInput("Client Secret", SecretNames.CfClientSecret);
            Label(Tr.Get("CfHeadersLabel", "Headers sent"));
            ImGui.TextColored(Theme.TextDim, $"{RequestDecorator.CfClientIdHeader} / {RequestDecorator.CfClientSecretHeader}");
        }

        Label(Tr.Get("ExtraHeadersLabel", "Extra headers"));
        if (ImGui.SmallButton(Tr.Get("AddHeaderBtn", "+ Add header")))
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
            if (ImGui.InputTextWithHint("##hname", Tr.Get("HeaderName", "Name"), ref name, 128)) headersDirty = true;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Math.Max(80, ImGui.GetContentRegionAvail().X - 30));
            if (ImGui.InputTextWithHint("##hvalue", Tr.Get("HeaderValue", "Value"), ref value, 512, ImGuiInputTextFlags.Password)) headersDirty = true;
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
        Section(Tr.Get("ModelsSection", "Models"));
        if (c.Mode != ConnectionMode.Direct)
        {
            var disabled1 = fetchingModels;
            if (disabled1) ImGui.BeginDisabled();
            if (ImGui.SmallButton(c.Mode == ConnectionMode.LiteLlm
                    ? Tr.Get("FetchModelsLiteLlm", "Fetch list from LiteLLM")
                    : Tr.Get("FetchModelsBtn", "Fetch model list")))
            {
                SaveSecrets();
                fetchingModels = true;
                fetchError = null;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        fetchedModels = await plugin.ListModelsAsync(echo: false).ConfigureAwait(false);
                        if (fetchedModels.Count == 0) fetchError = Tr.Get("FetchModelsEmpty", "No models returned. Run the connection test for details.");
                    }
                    finally { fetchingModels = false; }
                });
            }
            if (disabled1) ImGui.EndDisabled();
            ImGui.SameLine();
            if (fetchedModels.Count > 0) ImGui.TextColored(Theme.TextMuted, string.Format(Tr.Get("ModelsCount", "{0} models"), fetchedModels.Count));
            else if (fetchError is { } err) ImGui.TextColored(Theme.Warn, err);
            else ImGui.NewLine();
        }

        ModelInput(Tr.Get("InboundModelLabel", "Inbound (fast)"), "in", c.InboundModel, v => c.InboundModel = v);
        ModelInput(Tr.Get("OutboundModelLabel", "Outbound (quality)"), "out", c.OutboundModel, v => c.OutboundModel = v);

        var json = c.UseJsonResponseFormat;
        if (ImGui.Checkbox(Tr.Get("JsonModeLabel", "Request JSON format (models that reject it are instructed by prompt)"), ref json))
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
        ImGui.TextUnformatted(Tr.Get("TestTitle", "Connection test"));
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - 60));
        var disabled2 = testRunning;
        if (disabled2) ImGui.BeginDisabled();
        if (AccentButton(testRunning ? "…" : Tr.Get("RunTestBtn", "Run")))
        {
            SaveSecrets();
            testRunning = true;
            _ = Task.Run(async () =>
            {
                try { lastReport = await plugin.RunConnectionTestAsync(echo: false).ConfigureAwait(false); }
                finally { testRunning = false; }
            });
        }
        if (disabled2) ImGui.EndDisabled();
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
        ImGui.TextUnformatted(Tr.Get("TestHowToRead", "Reading failures"));
        ImGui.PushTextWrapPos();
        ImGui.TextColored(Theme.TextDim, Tr.Get("TestHint302", "302 / 403: check that the Cloudflare Access policy uses Service Auth."));
        ImGui.TextColored(Theme.TextDim, Tr.Get("TestHint401", "401: the LiteLLM virtual key is invalid or expired."));
        ImGui.TextColored(Theme.TextDim, Tr.Get("TestHintModel", "Model not found: check that the virtual key may use that model."));
        ImGui.Spacing();
        ImGui.TextColored(Theme.TextMuted, Tr.Get("SecretsNotice", "Keys and secrets are stored in Windows Credential Manager, not in the configuration file."));
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
        if (ImGui.SmallButton(Tr.Get("ChangeInConnectionTab", "Change in Connection tab")))
            selectTab = SettingsTab.Connection;

        Section(Tr.Get("UnresolvedSection", "When some words cannot be translated"));
        Radio(Tr.Get("UnresolvedPartial", "Translate what can be translated and mark the rest in the original (recommended)"), UnresolvedDisplayMode.PartialMarked);
        Radio(Tr.Get("UnresolvedOriginal", "Show only the original"), UnresolvedDisplayMode.OriginalOnly);
        Radio(Tr.Get("UnresolvedHide", "Show nothing"), UnresolvedDisplayMode.Hide);
        var discard = t.DiscardRefusals;
        if (ImGui.Checkbox(Tr.Get("DiscardRefusals", "Discard refusals / apologies (\"cannot translate\") and show the original"), ref discard))
            t.DiscardRefusals = discard;

        Section(Tr.Get("FreshTermsSection", "Keep terms up to date"));
        var game = t.UseGameData;
        if (ImGui.Checkbox(Tr.Get("UseGameData", "Load auto-translate phrases and action names from the game data (follows patches)"), ref game))
            t.UseGameData = game;
        var auto = t.CommunityAutoUpdate;
        if (ImGui.Checkbox(Tr.Get("CommunityAutoUpdate", "Update the community dictionary at startup"), ref auto))
            t.CommunityAutoUpdate = auto;
        var hints = t.SendContextHints;
        if (ImGui.Checkbox(Tr.Get("SendContextHints", "Send location, combat state and party jobs as hints"), ref hints))
            t.SendContextHints = hints;
        var url = t.CommunityGlossaryUrl;
        if (LabeledInput(Tr.Get("CommunityUrl", "Community dictionary URL"), "##communityurl", ref url)) t.CommunityGlossaryUrl = url.Trim();
    }

    private static string ConnectionSummary(ConnectionConfig c)
    {
        var mode = c.Mode switch
        {
            ConnectionMode.LiteLlm when c.CloudflareAccessEnabled => Tr.Get("SummaryLiteLlmCf", "LiteLLM (via Cloudflare Access)"),
            ConnectionMode.LiteLlm                                => "LiteLLM",
            ConnectionMode.OpenAICompatible                       => Tr.Get("ModeOpenAICompatible", "OpenAI-compatible"),
            _                                                     => c.DirectProvider.ToString(),
        };
        var outbound = string.IsNullOrWhiteSpace(c.OutboundModel) ? c.InboundModel : c.OutboundModel;
        return string.Format(Tr.Get("SummaryLine", "{0} · inbound {1} / outbound {2}"), mode,
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
        ImGui.TextColored(Theme.TextDim, Tr.Get("ChannelsHelp", "Messages in these channels are translated. Where they are shown is set in the Display tab."));
        ImGui.Spacing();
        const int columns = 4;
        if (!ImGui.BeginTable("##channels", columns)) return;
        foreach (var (type, locKey, fallback) in Channels)
        {
            ImGui.TableNextColumn();
            var enabled = config.EnabledChannels.Contains(type);
            if (ImGui.Checkbox(Tr.Get(locKey, fallback), ref enabled))
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
        Label(Tr.Get("DictMine", "My dictionary"));
        ImGui.TextUnformatted($"{stats.UserCount:N0}");
        Label(Tr.Get("DictGame", "Game data"));
        ImGui.TextUnformatted($"{stats.GameDataCount:N0}  ({Tr.Get("DictAutoTranslate", "auto-translate")} {plugin.Glossary.Catalog.Entries.Count:N0})");
        Label(Tr.Get("DictCommunity", "Community"));
        ImGui.TextUnformatted($"{stats.CommunityCount:N0}  v{stats.CommunityVersion ?? "-"}  ({plugin.Glossary.Community?.Origin})");
        Label(Tr.Get("DictPending", "Pending"));
        ImGui.TextUnformatted($"{plugin.Glossary.Pending.Items.Count:N0}");

        ImGui.Spacing();
        var disabled3 = reloadingGlossary;
        if (disabled3) ImGui.BeginDisabled();
        if (ImGui.Button(Tr.Get("DictUpdateNow", "Update now")))
        {
            reloadingGlossary = true;
            _ = Task.Run(async () =>
            {
                try { await plugin.Glossary.ReloadAsync(forceDownload: true).ConfigureAwait(false); }
                finally { reloadingGlossary = false; }
            });
        }
        if (disabled3) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Tr.Get("DictOpenWindow", "Open dictionary")))
            plugin.OpenGlossary();
        ImGui.SameLine();
        if (ImGui.Button(Tr.Get("DictOpenAt", "Auto-translate list")))
            plugin.OpenAutoTranslate();

        if (plugin.Glossary.Community?.Error is { } error)
            ImGui.TextColored(Theme.Warn, error);
    }

    // ═════ Display ═══════════════════════════════════════════════════════

    private void DrawDisplayTab()
    {
        var d = config.Display;

        Label(Tr.Get("PresetLabel", "Preset"));
        PresetButton(d, DisplayPreset.Recommended, Tr.Get("PresetRecommended", "Recommended"));
        PresetButton(d, DisplayPreset.WindowOnly, Tr.Get("PresetWindow", "Window only"));
        PresetButton(d, DisplayPreset.GameChatOnly, Tr.Get("PresetGameChat", "Game chat only"));
        PresetButton(d, DisplayPreset.BubbleOnly, Tr.Get("PresetBubble", "Bubbles only"));
        if (d.Preset == DisplayPreset.Custom)
            ImGui.TextColored(Theme.Accent, Tr.Get("PresetCustom", "Custom"));
        else
            ImGui.TextColored(Theme.TextMuted, Tr.Get("PresetHint", "Editing the table switches to \"Custom\"."));

        Section(Tr.Get("RoutesSection", "Where each channel is shown"));
        DrawRouteTable(d);
        ImGui.TextColored(Theme.TextMuted, Tr.Get("BubbleOnlyParty", "Bubbles attach to the party list, so only Party and Alliance can use them."));

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
        if (ImGui.Button(Tr.Get("TestDisplayBtn", "Test display")))
            plugin.ShowSampleMessages();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Tr.Get("TestDisplayTip", "Adds sample messages to the translation log (no model call)."));
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
        ImGui.TableSetupColumn(Tr.Get("ColChannel", "Channel"), ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn(Tr.Get("ColBubble", "Bubble"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(Tr.Get("ColLog", "Translation log"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(Tr.Get("ColGameChat", "Game chat"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(Tr.Get("ColCombat", "In combat"), ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableHeadersRow();

        var modes = CombatModeKeys.Select((k, i) => Tr.Get(k, CombatModeFallbacks[i])).ToArray();
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
        ImGui.TextUnformatted(Tr.Get("CardBubble", "Bubbles"));
        var seconds = b.Seconds;
        ImGui.SetNextItemWidth(90);
        if (ImGui.InputFloat(Tr.Get("BubbleSeconds", "seconds shown"), ref seconds, 0.5f, 1f, "%.1f"))
            b.Seconds = Math.Clamp(seconds, 1f, 60f);
        var max = b.MaxPerMember;
        ImGui.SetNextItemWidth(90);
        if (ImGui.InputInt(Tr.Get("BubbleMax", "max per member"), ref max))
            b.MaxPerMember = Math.Clamp(max, 1, 5);
        var extend = b.ExtendByLength;
        if (ImGui.Checkbox(Tr.Get("BubbleExtend", "Longer for long text"), ref extend)) b.ExtendByLength = extend;

        ImGui.Spacing();
        var history = b.ShowHistory;
        if (ImGui.Checkbox(Tr.Get("BubbleHistory", "Recent 3 at bottom right"), ref history)) b.ShowHistory = history;
        DrawHotkey(b.HistoryHotkey);
    }

    private static readonly string[] HotkeyKeys =
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString())
                  .Concat(Enumerable.Range(1, 12).Select(i => $"F{i}"))
                  .ToArray();

    private static void DrawHotkey(HotkeySettings hk)
    {
        ImGui.TextColored(Theme.TextDim, Tr.Get("HotkeyLabel", "Toggle key"));
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
        ImGui.TextUnformatted(Tr.Get("CardLog", "Translation log"));
        var oneLine = l.OneLine;
        if (ImGui.Checkbox(Tr.Get("LogOneLine", "One line (click for original)"), ref oneLine)) l.OneLine = oneLine;
        var pause = l.PauseWhileScrolled;
        if (ImGui.Checkbox(Tr.Get("LogPause", "Pause while scrolled up"), ref pause)) l.PauseWhileScrolled = pause;
        var hl = l.HighlightMentions;
        if (ImGui.Checkbox(Tr.Get("LogHighlight", "Highlight messages to me"), ref hl)) l.HighlightMentions = hl;
    }

    private static readonly string[] OutputKeys = ["OutputOriginal", "OutputEcho"];
    private static readonly string[] OutputFallbacks = ["Original channel", "Echo"];

    private static void DrawGameChatCard(GameChatSettings g)
    {
        ImGui.TextUnformatted(Tr.Get("CardGameChat", "Game chat"));
        var output = (int)g.Output;
        ImGui.SetNextItemWidth(130);
        if (ImGui.Combo(Tr.Get("GameChatOutput", "output"), ref output, OutputKeys.Select((k, i) => Tr.Get(k, OutputFallbacks[i])).ToArray()))
            g.Output = (GameChatOutput)output;
        var prefix = g.Prefix;
        ImGui.SetNextItemWidth(90);
        if (ImGui.InputText(Tr.Get("GameChatPrefix", "line prefix"), ref prefix, 32)) g.Prefix = prefix;
        var replace = g.ReplaceOriginal;
        if (ImGui.Checkbox(Tr.Get("GameChatReplace", "Replace the original with the translation"), ref replace)) g.ReplaceOriginal = replace;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Tr.Get("GameChatReplaceTip", "Only possible when the translation is already cached; otherwise the translation is added as a new line."));
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
        secretLabels[secretName] = label;
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
        if (ImGui.SmallButton($"{(visible ? Tr.Get("HideBtn", "Hide") : Tr.Get("ShowBtn", "Show"))}##{secretName}"))
        {
            if (visible) visibleSecrets.Remove(secretName);
            else visibleSecrets.Add(secretName);
        }
    }

    /// <summary>Writes changed secrets to Credential Manager; returns the labels of those that failed.</summary>
    private List<string> SaveSecrets()
    {
        var failures = new List<string>();
        foreach (var name in dirtySecrets.ToList())
        {
            var label = secretLabels.GetValueOrDefault(name, name);
            try
            {
                plugin.Secrets.Set(name, secretBuffers[name].Trim());
                dirtySecrets.Remove(name);
            }
            catch (Exception ex)
            {
                failures.Add(label);
                plugin.Diag.Error($"Could not save \"{label}\" to Windows Credential Manager ({ex.GetType().Name}).");
            }
        }

        if (!headersDirty) return failures;
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
            failures.Add(Tr.Get("ExtraHeadersLabel", "Extra headers"));
            plugin.Diag.Error($"Could not save extra headers ({ex.GetType().Name}).");
        }
        return failures;
    }

    private void SaveAll()
    {
        var failures = SaveSecrets();
        plugin.TranslationLog.Capacity = config.Display.Log.Capacity;
        plugin.Pipeline.Cache.Capacity = config.Translation.CacheSize;
        try
        {
            config.Save();
        }
        catch (Exception ex)
        {
            failures.Add(Tr.Get("SaveSettingsFile", "settings file"));
            plugin.Diag.Error($"Could not write the settings file ({ex.GetType().Name}).");
        }

        saveFailures = failures;
        savedAt      = DateTimeOffset.Now;
        if (failures.Count == 0)
            plugin.Diag.Info("Settings saved.");
    }

    private static string Title() => Tr.Get("ConfigWindowTitle", "AetherGlot Settings") + "###AetherGlotSettings";

    /// <summary>Re-reads the localized title after the Dalamud language changes.</summary>
    public void Relocalize() => WindowName = Title();

    public void Dispose() { }
}
