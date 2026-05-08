using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using AetherGlot.Core;
using AetherGlot.Helpers;

namespace AetherGlot.Windows;

/// <summary>
/// Floating window that shows a translation preview for outbound chat and lets
/// the player choose a channel and confirm sending.
/// Opened by the <c>/agsend [channel] &lt;text&gt;</c> command.
/// </summary>
public sealed class SendPreviewWindow : Window, IDisposable
{
    private readonly Configuration          config;
    private readonly ITranslationOrchestrator orchestrator;
    private readonly ITranslationContext    context;
    private readonly IPromptBuilder         promptBuilder;
    private readonly IChatSender            chatSender;

    // ── per-invocation UI state ──────────────────────────────────────────────
    private string       originalText    = string.Empty;
    private string       translatedText  = string.Empty;
    private bool         isTranslating   = false;
    private string?      errorMessage    = null;
    private XivChatType  selectedChannel;

    private CancellationTokenSource? cts;

    // Channels available in the send-channel combo
    private static readonly (XivChatType Type, string Label)[] SendableChannels =
    [
        (XivChatType.Say,             "Say"),
        (XivChatType.Shout,           "Shout"),
        (XivChatType.Yell,            "Yell"),
        (XivChatType.Party,           "Party"),
        (XivChatType.Alliance,        "Alliance"),
        (XivChatType.FreeCompany,     "Free Company"),
        (XivChatType.NoviceNetwork,   "Novice Network"),
        (XivChatType.Ls1,             "LS 1"),
        (XivChatType.Ls2,             "LS 2"),
        (XivChatType.Ls3,             "LS 3"),
        (XivChatType.Ls4,             "LS 4"),
        (XivChatType.Ls5,             "LS 5"),
        (XivChatType.Ls6,             "LS 6"),
        (XivChatType.Ls7,             "LS 7"),
        (XivChatType.Ls8,             "LS 8"),
        (XivChatType.CrossLinkShell1, "CWLS 1"),
        (XivChatType.CrossLinkShell2, "CWLS 2"),
        (XivChatType.CrossLinkShell3, "CWLS 3"),
        (XivChatType.CrossLinkShell4, "CWLS 4"),
        (XivChatType.CrossLinkShell5, "CWLS 5"),
        (XivChatType.CrossLinkShell6, "CWLS 6"),
        (XivChatType.CrossLinkShell7, "CWLS 7"),
        (XivChatType.CrossLinkShell8, "CWLS 8"),
    ];

    public SendPreviewWindow(
        Configuration config,
        ITranslationOrchestrator orchestrator,
        ITranslationContext context,
        IPromptBuilder promptBuilder,
        IChatSender chatSender)
        : base(Loc.Localize("SendPreviewTitle", "AetherGlot — Send Preview"))
    {
        this.config       = config;
        this.orchestrator = orchestrator;
        this.context      = context;
        this.promptBuilder = promptBuilder;
        this.chatSender   = chatSender;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 200),
            MaximumSize = new Vector2(800, 400),
        };
        IsOpen = false;
    }

    /// <summary>
    /// Opens the window and starts an async translation for <paramref name="text"/>.
    /// Can be called multiple times; previous translations are cancelled.
    /// An optional <paramref name="channelOverride"/> sets the initial channel,
    /// falling back to <see cref="Configuration.DefaultOutboundChannel"/>.
    /// </summary>
    public void OpenForText(string text, XivChatType? channelOverride = null)
    {
        originalText   = text;
        translatedText = string.Empty;
        errorMessage   = null;
        isTranslating  = true;
        selectedChannel = channelOverride ?? config.DefaultOutboundChannel;
        IsOpen = true;

        _ = StartTranslationAsync(text, selectedChannel, CancellationToken.None);
    }

    public override void Draw()
    {
        // ── Source text ──────────────────────────────────────────────────────
        ImGui.TextUnformatted(Loc.Localize("SendPreviewOriginal", "Original:"));
        ImGui.SameLine();
        ImGui.TextWrapped(originalText);

        ImGui.Separator();

        // ── Translation result ───────────────────────────────────────────────
        ImGui.TextUnformatted(Loc.Localize("SendPreviewTranslation", "Translation:"));
        ImGui.SameLine();

        if (isTranslating)
        {
            ImGui.TextUnformatted(Loc.Localize("SendPreviewTranslating", "Translating..."));
        }
        else if (errorMessage is not null)
        {
            ImGui.TextUnformatted($"[Error] {errorMessage}");
        }
        else
        {
            ImGui.TextWrapped(translatedText);
        }

        ImGui.Separator();

        // ── Channel selector ─────────────────────────────────────────────────
        ImGui.TextUnformatted(Loc.Localize("SendPreviewChannel", "Channel:"));
        ImGui.SameLine();

        var currentLabel = SendableChannels
            .FirstOrDefault(c => c.Type == selectedChannel)
            .Label ?? selectedChannel.ToString();

        if (ImGui.BeginCombo("##sendchannel", currentLabel))
        {
            foreach (var (type, label) in SendableChannels)
            {
                var isSelected = type == selectedChannel;
                if (ImGui.Selectable(label, isSelected))
                {
                    if (type != selectedChannel)
                    {
                        selectedChannel = type;
                        // Re-translate for the new channel's context language
                        translatedText  = string.Empty;
                        errorMessage    = null;
                        isTranslating   = true;
                        _ = StartTranslationAsync(originalText, selectedChannel, CancellationToken.None);
                    }
                }
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();

        // ── Action buttons ───────────────────────────────────────────────────
        var canSend = !isTranslating && errorMessage is null && !string.IsNullOrWhiteSpace(translatedText);

        if (!canSend) ImGui.BeginDisabled();
        if (ImGui.Button(Loc.Localize("SendPreviewSendBtn", "Send")))
        {
            chatSender.SendMessage(selectedChannel, translatedText);
            IsOpen = false;
        }
        if (!canSend) ImGui.EndDisabled();

        ImGui.SameLine();

        if (ImGui.Button(Loc.Localize("SendPreviewCancelBtn", "Cancel")))
        {
            cts?.Cancel();
            IsOpen = false;
        }
    }

    // Cancels any in-flight translation and starts a new one.
    private async Task StartTranslationAsync(string text, XivChatType channel, CancellationToken _)
    {
        // Cancel previous request
        cts?.Cancel();
        cts?.Dispose();
        var localCts = new CancellationTokenSource();
        cts = localCts;

        try
        {
            var options = BuildOptions();
            if (options is null)
            {
                errorMessage  = Loc.Localize("SendPreviewNoApiKey", "Provider or API key not configured.");
                isTranslating = false;
                return;
            }

            var targetLang = context.GetContextLanguage(channel.ToString(), "EN");
            var result = await orchestrator
                .TranslateOutboundAsync(text, targetLang, config.OutboundPrompt, options, localCts.Token)
                .ConfigureAwait(false);

            if (localCts.IsCancellationRequested) return;

            isTranslating = false;
            if (result.IsSuccess)
                translatedText = result.TranslatedText;
            else
                errorMessage = result.ErrorMessage;
        }
        catch (OperationCanceledException)
        {
            // Silently discard cancelled requests
        }
        catch (Exception ex)
        {
            isTranslating = false;
            errorMessage  = ex.Message;
        }
    }

    private TranslationOptions? BuildOptions()
    {
        if (string.IsNullOrWhiteSpace(config.SelectedProvider)) return null;
        if (!config.ApiKeys.TryGetValue(config.SelectedProvider, out var apiKey)
            || string.IsNullOrWhiteSpace(apiKey)) return null;

        return new TranslationOptions(
            config.SelectedProvider,
            apiKey,
            config.SelectedModel,
            string.IsNullOrWhiteSpace(config.CustomEndpoint) ? null : config.CustomEndpoint);
    }

    public void Dispose()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
