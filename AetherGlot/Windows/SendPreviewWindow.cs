using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherGlot.Windows;

/// <summary>
/// Floating window that shows a translation preview for outbound chat and lets
/// the player choose a channel and confirm sending.
/// Opened by the <c>/agsend [channel] &lt;text&gt;</c> command.
/// The full preview (tone, term chips, alternatives as buttons) arrives in phase 5.
/// </summary>
public sealed class SendPreviewWindow : Window, IDisposable
{
    private readonly Configuration            config;
    private readonly ITranslationPipeline     pipeline;
    private readonly ITranslationContext      context;
    private readonly IChatSender              chatSender;
    private readonly Func<ConnectionSettings> connection;
    private readonly IClientState             clientState;

    /// <summary>Immutable snapshot swapped atomically by the background task.</summary>
    private sealed record PreviewState(bool Translating, OutboundOutcome? Outcome, string? Error);

    private volatile PreviewState state = new(false, null, null);
    private string      originalText = string.Empty;
    private XivChatType selectedChannel;
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
        ITranslationPipeline pipeline,
        ITranslationContext context,
        IChatSender chatSender,
        Func<ConnectionSettings> connection,
        IClientState clientState)
        : base(Loc.Localize("SendPreviewTitle", "AetherGlot — Send Preview"))
    {
        this.config      = config;
        this.pipeline    = pipeline;
        this.context     = context;
        this.chatSender  = chatSender;
        this.connection  = connection;
        this.clientState = clientState;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 220),
            MaximumSize = new Vector2(900, 600),
        };
        IsOpen = false;
    }

    /// <summary>
    /// Opens the window and starts an async translation for <paramref name="text"/>.
    /// Previous translations are cancelled.
    /// </summary>
    public void OpenForText(string text, XivChatType? channelOverride = null)
    {
        originalText    = text;
        selectedChannel = channelOverride ?? config.DefaultOutboundChannel;
        IsOpen          = true;
        StartTranslation();
    }

    public override void Draw()
    {
        var s = state;

        ImGui.TextUnformatted(Loc.Localize("SendPreviewOriginal", "Original:"));
        ImGui.SameLine();
        ImGui.TextWrapped(originalText);
        ImGui.Separator();

        ImGui.TextUnformatted(Loc.Localize("SendPreviewTranslation", "Translation:"));
        ImGui.SameLine();
        if (s.Translating)
        {
            ImGui.TextUnformatted(Loc.Localize("SendPreviewTranslating", "Translating..."));
        }
        else if (s.Error is not null)
        {
            ImGui.TextWrapped($"[Error] {s.Error}");
        }
        else if (s.Outcome is { Success: true } o)
        {
            ImGui.TextWrapped(o.Translation);
            if (!string.IsNullOrWhiteSpace(o.Response?.BackTranslation))
            {
                ImGui.TextUnformatted(Loc.Localize("SendPreviewBack", "Back-translation:"));
                ImGui.SameLine();
                ImGui.TextWrapped(o.Response!.BackTranslation!);
            }
        }

        ImGui.Separator();
        DrawChannelCombo();
        ImGui.Spacing();

        var translation = s.Outcome is { Success: true } ok ? ok.Translation : string.Empty;
        var canSend     = !s.Translating && s.Error is null && !string.IsNullOrWhiteSpace(translation);

        if (!canSend) ImGui.BeginDisabled();
        if (ImGui.Button(Loc.Localize("SendPreviewSendBtn", "Send")))
        {
            chatSender.SendMessage(selectedChannel, translation);
            IsOpen = false;
        }
        if (!canSend) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Loc.Localize("SendPreviewSendOriginalBtn", "Send original")))
        {
            chatSender.SendMessage(selectedChannel, originalText);
            IsOpen = false;
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.Localize("SendPreviewCancelBtn", "Cancel")))
        {
            cts?.Cancel();
            IsOpen = false;
        }
    }

    private void DrawChannelCombo()
    {
        ImGui.TextUnformatted(Loc.Localize("SendPreviewChannel", "Channel:"));
        ImGui.SameLine();

        var currentLabel = SendableChannels.FirstOrDefault(c => c.Type == selectedChannel).Label ?? selectedChannel.ToString();
        if (!ImGui.BeginCombo("##sendchannel", currentLabel)) return;

        foreach (var (type, label) in SendableChannels)
        {
            var isSelected = type == selectedChannel;
            if (ImGui.Selectable(label, isSelected) && type != selectedChannel)
            {
                selectedChannel = type;
                // Re-translate for the new channel's context language
                StartTranslation();
            }
            if (isSelected) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    // Cancels any in-flight translation and starts a new one.
    private void StartTranslation()
    {
        cts?.Cancel();
        cts?.Dispose();
        var localCts = new CancellationTokenSource();
        cts   = localCts;
        state = new PreviewState(true, null, null);

        var settings = connection();
        if (ConnectionSettingsBuilder.MissingRequirement(settings) is { } missing)
        {
            state = new PreviewState(false, null, missing);
            return;
        }

        var target  = LanguageName(context.GetContextLanguage(selectedChannel.ToString(), "EN"));
        var request = new OutboundRequest(
            originalText,
            ChatTranslationService.LanguageName(clientState.ClientLanguage),
            target,
            OutboundTone.Normal,
            config.OutboundPrompt);

        _ = Task.Run(async () =>
        {
            try
            {
                var outcome = await pipeline
                    .TranslateOutboundAsync(request, settings, config.Translation.ToPipelineOptions(), localCts.Token)
                    .ConfigureAwait(false);
                if (localCts.IsCancellationRequested) return;
                state = outcome.Success
                    ? new PreviewState(false, outcome, null)
                    : new PreviewState(false, outcome, outcome.FailureReason ?? "Translation failed.");
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer request
            }
            catch (Exception ex)
            {
                if (!localCts.IsCancellationRequested)
                    state = new PreviewState(false, null, ex.GetType().Name);
            }
        });
    }

    private static string LanguageName(string code) => code.ToUpperInvariant() switch
    {
        "EN" => "English",
        "JA" => "Japanese",
        "DE" => "German",
        "FR" => "French",
        "KO" => "Korean",
        "ZH" => "Chinese",
        _    => code,
    };

    public void Dispose()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
