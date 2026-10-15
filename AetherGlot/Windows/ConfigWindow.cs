using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherGlot.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration config;

    // Secret edit buffers, keyed by secret name; loaded lazily from the secret store
    private readonly Dictionary<string, string> secretBuffers = new(StringComparer.Ordinal);
    private readonly HashSet<string> dirtySecrets = new(StringComparer.Ordinal);
    private readonly HashSet<string> visibleSecrets = new(StringComparer.Ordinal);

    // Results of background operations (swapped atomically)
    private volatile DiagnosticReport? lastReport;
    private volatile bool testRunning;
    private volatile IReadOnlyList<string> fetchedModels = [];
    private volatile bool fetchingModels;

    private static readonly (ConnectionMode Mode, string LocKey, string Fallback)[] Modes =
    [
        (ConnectionMode.Direct,           "ModeDirect",           "Direct (vendor API)"),
        (ConnectionMode.LiteLlm,          "ModeLiteLlm",          "LiteLLM proxy"),
        (ConnectionMode.OpenAICompatible, "ModeOpenAICompatible", "OpenAI-compatible"),
    ];

    public ConfigWindow(Plugin plugin)
        : base(Loc.Localize("ConfigWindowTitle", "AetherGlot Settings"))
    {
        this.plugin = plugin;
        this.config = plugin.Configuration;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void OnOpen()
    {
        // Re-read secrets every time the window opens
        secretBuffers.Clear();
        dirtySecrets.Clear();
        visibleSecrets.Clear();
    }

    public override void Draw()
    {
        DrawConnectionSection();
        ImGui.Separator();
        DrawTranslationSection();
        ImGui.Separator();
        DrawChannelSection();
        ImGui.Separator();
        DrawOutboundSection();
        ImGui.Separator();
        DrawCommandHelpSection();
        ImGui.Separator();
        DrawSaveButton();
    }

    // ─── Connection (interim UI; the full connection tab arrives in phase 3) ──

    private void DrawConnectionSection()
    {
        var c = config.Connection;
        ImGui.TextUnformatted(Loc.Localize("ConnectionSection", "Connection"));
        ImGui.Spacing();

        var modeLabel = Modes.First(m => m.Mode == c.Mode);
        if (ImGui.BeginCombo(Loc.Localize("ModeLabel", "Connection type"), Loc.Localize(modeLabel.LocKey, modeLabel.Fallback)))
        {
            foreach (var (mode, locKey, fallback) in Modes)
            {
                if (ImGui.Selectable(Loc.Localize(locKey, fallback), mode == c.Mode))
                    c.Mode = mode;
            }
            ImGui.EndCombo();
        }

        switch (c.Mode)
        {
            case ConnectionMode.Direct:
                if (ImGui.BeginCombo(Loc.Localize("ProviderLabel", "Provider"), c.DirectProvider.ToString()))
                {
                    foreach (var p in Enum.GetValues<DirectProvider>())
                    {
                        if (ImGui.Selectable(p.ToString(), p == c.DirectProvider))
                            c.DirectProvider = p;
                    }
                    ImGui.EndCombo();
                }
                var key      = c.DirectProvider.ToString();
                var endpoint = c.DirectEndpoints.TryGetValue(key, out var e) ? e : string.Empty;
                if (ImGui.InputText(Loc.Localize("EndpointLabel", "Endpoint"), ref endpoint, 512))
                    c.DirectEndpoints[key] = endpoint;
                SecretInput(Loc.Localize("ApiKeyLabel", "API Key"), SecretNames.DirectApiKey(key));
                break;

            case ConnectionMode.LiteLlm:
                var liteUrl = c.LiteLlmBaseUrl;
                if (ImGui.InputText(Loc.Localize("BaseUrlLabel", "Base URL"), ref liteUrl, 512))
                    c.LiteLlmBaseUrl = liteUrl;
                SecretInput(Loc.Localize("VirtualKeyLabel", "Virtual key"), SecretNames.LiteLlmKey);
                DrawUserTag(c);
                break;

            case ConnectionMode.OpenAICompatible:
                var compatUrl = c.OpenAICompatibleBaseUrl;
                if (ImGui.InputText(Loc.Localize("BaseUrlLabel", "Base URL"), ref compatUrl, 512))
                    c.OpenAICompatibleBaseUrl = compatUrl;
                SecretInput(Loc.Localize("ApiKeyOptionalLabel", "API Key (optional)"), SecretNames.OpenAICompatibleKey);
                DrawUserTag(c);
                break;
        }

        if (c.Mode != ConnectionMode.Direct)
        {
            ImGui.Spacing();
            var cf = c.CloudflareAccessEnabled;
            if (ImGui.Checkbox(Loc.Localize("CfEnabledLabel", "Use Cloudflare Access (service token)"), ref cf))
                c.CloudflareAccessEnabled = cf;
            if (c.CloudflareAccessEnabled)
            {
                SecretInput("Client ID", SecretNames.CfClientId);
                SecretInput("Client Secret", SecretNames.CfClientSecret);
                ImGui.TextDisabled(Loc.Localize("CfHeadersNote", "Sends CF-Access-Client-Id / CF-Access-Client-Secret"));
            }
        }

        ImGui.Spacing();
        DrawModelInput(Loc.Localize("InboundModelLabel", "Model (inbound)"), "##inmodels", c.InboundModel, v => c.InboundModel = v);
        DrawModelInput(Loc.Localize("OutboundModelLabel", "Model (outbound)"), "##outmodels", c.OutboundModel, v => c.OutboundModel = v);

        if (c.Mode != ConnectionMode.Direct)
        {
            if (fetchingModels) ImGui.BeginDisabled();
            if (ImGui.Button(Loc.Localize("FetchModelsBtn", "Fetch model list")))
            {
                SaveSecrets();
                fetchingModels = true;
                _ = Task.Run(async () =>
                {
                    try { fetchedModels = await plugin.ListModelsAsync(echo: false).ConfigureAwait(false); }
                    finally { fetchingModels = false; }
                });
            }
            if (fetchingModels) ImGui.EndDisabled();
            if (fetchedModels.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"{fetchedModels.Count}");
            }
        }

        var json = c.UseJsonResponseFormat;
        if (ImGui.Checkbox(Loc.Localize("JsonModeLabel", "Request JSON format (models that reject it are instructed by prompt)"), ref json))
            c.UseJsonResponseFormat = json;

        DrawConnectionTest();
    }

    private static void DrawUserTag(ConnectionConfig c)
    {
        var tag = c.UserTag;
        if (ImGui.InputText(Loc.Localize("UserTagLabel", "User tag"), ref tag, 128))
            c.UserTag = tag;
    }

    private void DrawModelInput(string label, string comboId, string value, Action<string> set)
    {
        var buf = value;
        if (ImGui.InputText(label, ref buf, 128))
            set(buf);

        var models = fetchedModels;
        if (models.Count == 0) return;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        if (ImGui.BeginCombo(comboId, string.Empty, ImGuiComboFlags.NoPreview))
        {
            foreach (var m in models)
            {
                if (ImGui.Selectable(m, m == value))
                    set(m);
            }
            ImGui.EndCombo();
        }
    }

    private void DrawConnectionTest()
    {
        ImGui.Spacing();
        if (testRunning) ImGui.BeginDisabled();
        if (ImGui.Button(Loc.Localize("RunTestBtn", "Run connection test")))
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

        if (lastReport is not { } report) return;

        foreach (var step in report.Steps)
        {
            var color = step.Status switch
            {
                DiagnosticStatus.Ok      => new Vector4(0.37f, 0.79f, 0.54f, 1f),
                DiagnosticStatus.Warning => new Vector4(0.90f, 0.54f, 0.36f, 1f),
                DiagnosticStatus.Failed  => new Vector4(0.95f, 0.35f, 0.35f, 1f),
                _                        => new Vector4(0.53f, 0.55f, 0.58f, 1f),
            };
            ImGui.TextColored(color, $"{step.Status,-7}");
            ImGui.SameLine();
            ImGui.TextWrapped($"{Plugin.StepLabel(step.Kind)}: {step.Detail}");
            if (step.Hint is not null && step.Status is DiagnosticStatus.Failed or DiagnosticStatus.Warning)
                ImGui.TextWrapped($"    {step.Hint}");
        }
        ImGui.TextDisabled(Loc.Localize("SecretsNotice", "Keys and secrets are stored in Windows Credential Manager, not in the configuration file."));
    }

    /// <summary>Masked text box bound to a secret; written to the store on Save.</summary>
    private void SecretInput(string label, string secretName)
    {
        if (!secretBuffers.TryGetValue(secretName, out var buf))
        {
            try { buf = plugin.Secrets.Get(secretName) ?? string.Empty; }
            catch { buf = string.Empty; }
            secretBuffers[secretName] = buf;
        }

        var visible = visibleSecrets.Contains(secretName);
        var flags   = visible ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
        if (ImGui.InputText($"{label}##{secretName}", ref buf, 512, flags))
        {
            secretBuffers[secretName] = buf;
            dirtySecrets.Add(secretName);
        }
        ImGui.SameLine();
        if (ImGui.Button($"{(visible ? Loc.Localize("HideBtn", "Hide") : Loc.Localize("ShowBtn", "Show"))}##{secretName}"))
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
    }

    // ─── Translation On/Off ───────────────────────────────────────────────────

    private void DrawTranslationSection()
    {
        ImGui.TextUnformatted(Loc.Localize("TranslationSection", "Translation"));
        ImGui.Spacing();

        var inbound = config.InboundEnabled;
        if (ImGui.Checkbox(Loc.Localize("InboundLabel", "Enable inbound translation (receive)"), ref inbound))
            config.InboundEnabled = inbound;

        var outbound = config.OutboundEnabled;
        if (ImGui.Checkbox(Loc.Localize("OutboundLabel", "Enable outbound translation (send)"), ref outbound))
            config.OutboundEnabled = outbound;

        var skipOwn = config.SkipOwnMessages;
        if (ImGui.Checkbox(Loc.Localize("SkipOwnLabel", "Skip translation for own messages"), ref skipOwn))
            config.SkipOwnMessages = skipOwn;

        var alwaysTranslate = config.AlwaysTranslate;
        if (ImGui.Checkbox(Loc.Localize("AlwaysTranslateLabel", "Translate even if message is in client language"), ref alwaysTranslate))
            config.AlwaysTranslate = alwaysTranslate;
    }

    // ─── Channel Checkboxes ───────────────────────────────────────────────────

    private static readonly (XivChatType type, string locKey, string fallback)[] Channels =
    [
        (XivChatType.Say,             "ChannelSay",          "Say"),
        (XivChatType.Shout,           "ChannelShout",        "Shout"),
        (XivChatType.Yell,            "ChannelYell",         "Yell"),
        (XivChatType.Party,           "ChannelParty",        "Party"),
        (XivChatType.Alliance,        "ChannelAlly",         "Alliance"),
        (XivChatType.FreeCompany,     "ChannelFc",           "Free Company"),
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

    private const int ChannelColumns = 3;

    private void DrawChannelSection()
    {
        ImGui.TextUnformatted(Loc.Localize("ChannelSection", "Enabled Chat Channels"));
        ImGui.Spacing();

        for (var i = 0; i < Channels.Length; i++)
        {
            var (type, locKey, fallback) = Channels[i];
            var enabled = config.EnabledChannels.Contains(type);
            if (ImGui.Checkbox(Loc.Localize(locKey, fallback), ref enabled))
            {
                if (enabled)
                    config.EnabledChannels.Add(type);
                else
                    config.EnabledChannels.Remove(type);
            }

            // Layout: ChannelColumns items per row
            if ((i + 1) % ChannelColumns != 0 && i < Channels.Length - 1)
                ImGui.SameLine();
        }
    }

    // ─── Outbound Prompt & Default Channel ───────────────────────────────────

    private void DrawOutboundSection()
    {
        ImGui.TextUnformatted(
            Loc.Localize("OutboundSection", "Outbound Prompt (for send translation)"));
        ImGui.Spacing();

        var prompt = config.OutboundPrompt;
        if (ImGui.InputTextMultiline(
            "##outboundprompt",
            ref prompt, 2048,
            new Vector2(-1f, 100f)))
        {
            config.OutboundPrompt = prompt;
        }

        if (ImGui.Button(Loc.Localize("ResetPromptBtn", "Reset to Default")))
            config.OutboundPrompt = Configuration.DefaultOutboundPrompt;

        ImGui.Spacing();

        // Default channel for the send-preview window
        var currentChannelLabel = Channels
            .FirstOrDefault(c => c.type == config.DefaultOutboundChannel)
            .fallback ?? config.DefaultOutboundChannel.ToString();

        if (ImGui.BeginCombo(
            Loc.Localize("DefaultOutboundChannelLabel", "Default Send Channel"),
            currentChannelLabel))
        {
            foreach (var (type, locKey, fallback) in Channels)
            {
                var selected = type == config.DefaultOutboundChannel;
                if (ImGui.Selectable(Loc.Localize(locKey, fallback), selected))
                    config.DefaultOutboundChannel = type;
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    // ─── Command Help ─────────────────────────────────────────────────────────

    private void DrawCommandHelpSection()
    {
        ImGui.TextUnformatted(Loc.Localize("CommandHelpSection", "Send Command"));
        ImGui.Spacing();
        ImGui.TextUnformatted(Loc.Localize("CommandHelpLine1", "/agsend <text>             — translate and send using the default channel"));
        ImGui.TextUnformatted(Loc.Localize("CommandHelpLine2", "/agsend <prefix> <text>    — translate and send to the specified channel"));
        ImGui.Spacing();
        ImGui.TextUnformatted(Loc.Localize("CommandHelpPrefixes", "Prefixes:  s  sh  y  p  a  fc  nn  l1-l8  cw1-cw8"));
    }

    // ─── Save ─────────────────────────────────────────────────────────────────

    private void DrawSaveButton()
    {
        if (ImGui.Button(Loc.Localize("SaveBtn", "Save")))
        {
            SaveSecrets();
            config.Save();
        }
    }

    public void Dispose() { }
}
