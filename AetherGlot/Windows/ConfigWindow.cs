using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherGlot.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration config;

    // Transient UI state
    private string apiKeyBuffer = string.Empty;
    private bool showApiKey = false;
    private string lastLoadedProviderForKey = string.Empty;

    public ConfigWindow(Plugin plugin)
        : base(Loc.Localize("ConfigWindowTitle", "AetherGlot Settings"),
               ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        this.config = plugin.Configuration;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 460),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        DrawProviderSection();
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

    // ─── Provider Settings ────────────────────────────────────────────────────

    private void DrawProviderSection()
    {
        ImGui.TextUnformatted(Loc.Localize("ProviderSection", "Provider Settings"));
        ImGui.Spacing();

        // Provider combo
        if (ImGui.BeginCombo(
            Loc.Localize("ProviderLabel", "Provider"),
            config.SelectedProvider))
        {
            foreach (var provider in ProviderFactory.AvailableProviders)
            {
                var selected = provider == config.SelectedProvider;
                if (ImGui.Selectable(provider, selected))
                {
                    config.SelectedProvider = provider;
                    // Reset key buffer when switching providers
                    apiKeyBuffer = config.ApiKeys.TryGetValue(provider, out var k) ? k : string.Empty;
                    lastLoadedProviderForKey = provider;
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // Sync API-key buffer when the selected provider changes externally
        if (lastLoadedProviderForKey != config.SelectedProvider)
        {
            apiKeyBuffer = config.ApiKeys.TryGetValue(config.SelectedProvider, out var existing)
                ? existing
                : string.Empty;
            lastLoadedProviderForKey = config.SelectedProvider;
        }

        // Model — combo with well-known suggestions, or free-text for Custom
        var models = ProviderFactory.DefaultModels.TryGetValue(config.SelectedProvider, out var m) ? m : [];
        if (models.Length > 0)
        {
            var displayModel = string.IsNullOrWhiteSpace(config.SelectedModel) ? models[0] : config.SelectedModel;
            if (ImGui.BeginCombo(Loc.Localize("ModelLabel", "Model"), displayModel))
            {
                foreach (var mdl in models)
                {
                    var selected = mdl == config.SelectedModel;
                    if (ImGui.Selectable(mdl, selected))
                        config.SelectedModel = mdl;
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }
        else
        {
            var modelBuf = config.SelectedModel;
            if (ImGui.InputText(Loc.Localize("ModelLabel", "Model"), ref modelBuf, 128))
                config.SelectedModel = modelBuf;
        }

        // Custom endpoint
        var endpointBuf = config.CustomEndpoint;
        if (ImGui.InputText(
            Loc.Localize("EndpointLabel", "Custom Endpoint (optional)"),
            ref endpointBuf, 512))
        {
            config.CustomEndpoint = endpointBuf;
        }

        // API key (masked by default)
        ImGui.Spacing();
        var flags = showApiKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
        if (ImGui.InputText(
            Loc.Localize("ApiKeyLabel", "API Key"),
            ref apiKeyBuffer, 256, flags))
        {
            config.ApiKeys[config.SelectedProvider] = apiKeyBuffer;
        }
        ImGui.SameLine();
        var toggleLabel = showApiKey
            ? Loc.Localize("HideBtn", "Hide")
            : Loc.Localize("ShowBtn", "Show");
        if (ImGui.Button(toggleLabel))
            showApiKey = !showApiKey;
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
            config.Save();
    }

    public void Dispose() { }
}
