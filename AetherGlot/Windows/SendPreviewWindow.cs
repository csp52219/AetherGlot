using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherGlot.Windows;

/// <summary>
/// ② Send preview. Type in your language, the translation follows as you type (debounced);
/// pick a tone or an alternative, then send the translation (Enter) or the original.
/// Action / job names are fixed to the official in-game name of the target language.
/// </summary>
public sealed class SendPreviewWindow : Window, IDisposable, IRelocalizable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);
    private const int InputBufferBytes = 2000;

    private readonly Configuration            config;
    private readonly ITranslationPipeline     pipeline;
    private readonly ITranslationContext      context;
    private readonly IChatSender              chatSender;
    private readonly Func<ConnectionSettings> connection;
    private readonly IClientState             clientState;
    private readonly GlossaryService          glossary;
    private readonly Action<OutboundOutcome>  inspect;

    /// <summary>What a translation was made for; a result is only shown while it still matches.</summary>
    private sealed record Key(string Text, OutboundTone Tone, string TargetCode);

    /// <summary>Immutable snapshot swapped atomically by the background task.</summary>
    private sealed record PreviewState(Key? For, bool Translating, OutboundOutcome? Outcome, string? Error);

    private volatile PreviewState state = new(null, false, null, null);
    private string      input = string.Empty;
    private string      output = string.Empty;   // chosen / edited translation
    private OutboundOutcome? shownOutcome;       // outcome `output` was taken from
    private DateTime?   editedAt;
    private XivChatType selectedChannel;
    private bool        focusInput;
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

    private static readonly (OutboundTone Tone, string LocKey, string Fallback)[] Tones =
    [
        (OutboundTone.Short,  "SendToneShort",  "Short"),
        (OutboundTone.Normal, "SendToneNormal", "Normal"),
        (OutboundTone.Polite, "SendTonePolite", "Polite"),
    ];

    public SendPreviewWindow(
        Configuration config,
        ITranslationPipeline pipeline,
        ITranslationContext context,
        IChatSender chatSender,
        Func<ConnectionSettings> connection,
        IClientState clientState,
        GlossaryService glossary,
        Action<OutboundOutcome> inspect)
        : base(Title())
    {
        this.config      = config;
        this.pipeline    = pipeline;
        this.context     = context;
        this.chatSender  = chatSender;
        this.connection  = connection;
        this.clientState = clientState;
        this.glossary    = glossary;
        this.inspect     = inspect;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 340),
            MaximumSize = new Vector2(1000, 900),
        };
        Size = new Vector2(560, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
        IsOpen = false;
    }

    /// <summary>Opens the window with <paramref name="text"/> and translates it right away.</summary>
    public void OpenForText(string text, XivChatType? channelOverride = null)
    {
        input           = text;
        selectedChannel = channelOverride ?? config.DefaultOutboundChannel;
        IsOpen          = true;
        focusInput      = true;
        StartTranslation();
    }

    /// <summary>Opens the window with an empty input for replying on <paramref name="channel"/>.</summary>
    public void OpenForReply(XivChatType channel)
    {
        cts?.Cancel();
        input           = string.Empty;
        output          = string.Empty;
        shownOutcome    = null;
        editedAt        = null;
        selectedChannel = DalamudChatSender.Supports(channel) ? channel : config.DefaultOutboundChannel;
        state           = new PreviewState(null, false, null, null);
        IsOpen          = true;
        focusInput      = true;
    }

    public override void Draw()
    {
        DrawToolbar();
        ImGui.Separator();

        // ── Input ───────────────────────────────────────────────────────
        ImGui.TextColored(Theme.TextDim, Loc.Localize("SendPreviewOriginal", "Your message"));
        if (focusInput)
        {
            ImGui.SetKeyboardFocusHere();
            focusInput = false;
        }
        var enter = ImGui.InputTextMultiline("##source", ref input, InputBufferBytes, new Vector2(-1, 64),
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CtrlEnterForNewLine);
        if (ImGui.IsItemEdited())
            editedAt = DateTime.UtcNow;
        ImGui.TextColored(Theme.TextMuted, Loc.Localize("SendPreviewInputHint", "Enter: send the translation · Ctrl+Enter: new line"));

        // Translate once typing pauses
        var s = state;
        if (editedAt is { } at && DateTime.UtcNow - at >= Debounce)
        {
            editedAt = null;
            if (s.For is null || s.For != CurrentKey()) StartTranslation();
        }

        s = state;
        TakeResult(s);

        ImGui.Separator();
        DrawOutput(s);
        ImGui.Separator();
        DrawButtons(s, enter);
    }

    // ── Toolbar: channel + tone ────────────────────────────────────────

    private void DrawToolbar()
    {
        ImGui.TextUnformatted(Loc.Localize("SendPreviewChannel", "Channel"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        var currentLabel = SendableChannels.FirstOrDefault(c => c.Type == selectedChannel).Label ?? selectedChannel.ToString();
        if (ImGui.BeginCombo("##sendchannel", currentLabel))
        {
            foreach (var (type, label) in SendableChannels)
            {
                var isSelected = type == selectedChannel;
                if (ImGui.Selectable(label, isSelected) && type != selectedChannel)
                {
                    selectedChannel = type;
                    // The channel's language may differ
                    if (!string.IsNullOrWhiteSpace(input)) StartTranslation();
                }
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(12, 0));
        ImGui.SameLine();
        foreach (var (tone, locKey, fallback) in Tones)
        {
            var selected = config.OutboundTone == tone;
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent with { W = 0.45f });
            if (ImGui.SmallButton(Loc.Localize(locKey, fallback)) && !selected)
            {
                config.OutboundTone = tone;
                config.Save();
                if (!string.IsNullOrWhiteSpace(input)) StartTranslation();
            }
            if (selected) ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        ImGui.TextColored(Theme.TextMuted, "→ " + TargetCode().ToUpperInvariant());
    }

    // ── Output ─────────────────────────────────────────────────────────

    private void DrawOutput(PreviewState s)
    {
        var stale = s.For is not null && s.For != CurrentKey();
        ImGui.TextColored(Theme.TextDim, Loc.Localize("SendPreviewTranslation", "Translation"));
        ImGui.SameLine();
        if (s.Translating)
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("SendPreviewTranslating", "translating…"));
        else if (stale || editedAt is not null)
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("SendPreviewOutdated", "(waiting for you to stop typing)"));
        else
            ImGui.TextUnformatted(string.Empty);

        // Byte counter on the right
        var bytes = ChatText.ByteCount(output);
        var counter = $"{bytes} / {ChatText.MaxBytes}";
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(counter).X));
        ImGui.TextColored(bytes > ChatText.MaxBytes ? Theme.Error : Theme.TextMuted, counter);
        if (bytes > ChatText.MaxBytes && ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.Localize("SendPreviewTooLong", "Longer than the chat allows; the end will be cut."));

        ImGui.InputTextMultiline("##output", ref output, InputBufferBytes, new Vector2(-1, 56), ImGuiInputTextFlags.CtrlEnterForNewLine);

        if (s.Error is not null)
        {
            ImGui.TextColored(Theme.Warn, s.Error);
            return;
        }
        if (s.Outcome is not { Success: true } o) return;

        DrawTermChips(o);

        if (!string.IsNullOrWhiteSpace(o.Response?.BackTranslation))
        {
            ImGui.TextColored(Theme.TextDim, Loc.Localize("SendPreviewBack", "Back-translation:"));
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
            ImGui.TextWrapped(o.Response!.BackTranslation!);
            ImGui.PopStyleColor();
        }

        var alternatives = o.Response?.Alternatives.Where(a => !string.IsNullOrWhiteSpace(a)).Take(2).ToList() ?? [];
        if (alternatives.Count > 0)
        {
            ImGui.TextColored(Theme.TextDim, Loc.Localize("SendPreviewAlternatives", "Alternatives (click to use):"));
            var candidates = new List<string> { o.Translation };
            candidates.AddRange(alternatives);
            for (var i = 0; i < candidates.Count; i++)
            {
                // Wrapped text made clickable (buttons do not wrap long sentences)
                var text = candidates[i];
                var chosen = output == text;
                ImGui.PushID(i);
                ImGui.BeginGroup();
                ImGui.TextColored(chosen ? Theme.Accent : Theme.TextMuted, chosen ? "●" : "○");
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, chosen ? Theme.Text : Theme.TextDim);
                ImGui.TextWrapped(text);
                ImGui.PopStyleColor();
                ImGui.EndGroup();
                if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsItemClicked()) output = text;
                ImGui.PopID();
            }
        }
    }

    /// <summary>Fixed terms (from the glossary) and the terms the model reported.</summary>
    private static void DrawTermChips(OutboundOutcome o)
    {
        var chips = new List<(string Text, bool Fixed)>();
        foreach (var hint in o.Request.FixedTerms ?? [])
            if (hint.Targets.Count > 0) chips.Add(($"{hint.Source} → {hint.Targets[0]}", true));
        foreach (var t in o.Response?.Terms ?? [])
        {
            var text = $"{t.Src} → {t.Dst}";
            if (!chips.Any(c => c.Text == text)) chips.Add((text, false));
        }
        if (chips.Count == 0) return;

        var first = true;
        foreach (var (text, isFixed) in chips)
        {
            if (!first) RichText.SameLineIfFits(text);
            RichText.Chip(text, isFixed ? Theme.TermChip : Theme.TextDim, isFixed ? Theme.TermChipBg : Theme.ChipBg);
            if (isFixed && ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.Localize("SendPreviewFixedTerm", "Fixed: official in-game name"));
            first = false;
        }
    }

    // ── Buttons ────────────────────────────────────────────────────────

    private void DrawButtons(PreviewState s, bool enterPressed)
    {
        var current = s.For == CurrentKey() && !s.Translating && editedAt is null;
        var canSend = current && !string.IsNullOrWhiteSpace(output);

        if (ImGui.Button(Loc.Localize("SendPreviewCancelBtn", "Cancel")))
        {
            cts?.Cancel();
            IsOpen = false;
        }
        ImGui.SameLine();
        var canSendOriginal = !string.IsNullOrWhiteSpace(input);
        if (!canSendOriginal) ImGui.BeginDisabled();
        if (ImGui.Button(Loc.Localize("SendPreviewSendOriginalBtn", "Send original")))
            Send(input);
        if (!canSendOriginal) ImGui.EndDisabled();

        ImGui.SameLine();
        if (!canSend) ImGui.BeginDisabled();
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentText);
        var sendClicked = ImGui.Button(Loc.Localize("SendPreviewSendBtn", "Send translation (Enter)"));
        ImGui.PopStyleColor(2);
        if (!canSend) ImGui.EndDisabled();

        if (sendClicked)
        {
            Send(output);
        }
        else if (enterPressed)
        {
            // Enter sends only a translation of what is in the box now; otherwise translate first
            if (canSend) Send(output);
            else
            {
                editedAt = null;
                if (!string.IsNullOrWhiteSpace(input) && (s.For != CurrentKey() || s.Error is not null)) StartTranslation();
                focusInput = true;
            }
        }

        if (s.Outcome is { } outcome)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.Localize("SendPreviewInspect", "Inspect")))
                inspect(outcome);
        }

        ImGui.TextColored(Theme.TextMuted, Loc.Localize("SendPreviewNote", "Action and job names are fixed to the game data's official name."));
    }

    private void Send(string text)
    {
        chatSender.SendMessage(selectedChannel, text);
        cts?.Cancel();
        input  = string.Empty;
        output = string.Empty;
        shownOutcome = null;
        state  = new PreviewState(null, false, null, null);
        IsOpen = false;
    }

    // ── Translation ────────────────────────────────────────────────────

    /// <summary>Copies a new result into the editable output once (later edits are kept).</summary>
    private void TakeResult(PreviewState s)
    {
        if (s.Outcome is null || ReferenceEquals(s.Outcome, shownOutcome)) return;
        shownOutcome = s.Outcome;
        output = s.Outcome.Success ? s.Outcome.Translation : string.Empty;
    }

    private string TargetCode() => context.GetContextLanguage(selectedChannel.ToString(), "EN").ToLowerInvariant();

    private Key CurrentKey() => new(input.Trim(), config.OutboundTone, TargetCode());

    // Cancels any in-flight translation and starts a new one.
    private void StartTranslation()
    {
        cts?.Cancel();
        cts?.Dispose();
        var localCts = new CancellationTokenSource();
        cts = localCts;

        var key = CurrentKey();
        if (key.Text.Length == 0)
        {
            state = new PreviewState(null, false, null, null);
            output = string.Empty;
            return;
        }

        var settings = connection();
        if (ConnectionSettingsBuilder.MissingRequirement(settings) is { } missing)
        {
            state = new PreviewState(key, false, null, missing);
            return;
        }
        state = new PreviewState(key, true, null, null);

        var sourceCode = ChatTranslationService.LanguageCode(clientState.ClientLanguage);

        // Action / job / status names use the official in-game name of the target language
        var fixedTerms = glossary.FixedTerms(key.Text, sourceCode, key.TargetCode);
        var request = new OutboundRequest(
            key.Text,
            ChatTranslationService.LanguageName(clientState.ClientLanguage),
            LanguageName(key.TargetCode),
            key.Tone,
            config.OutboundPrompt,
            fixedTerms);
        var options = config.Translation.ToPipelineOptions();

        _ = Task.Run(async () =>
        {
            try
            {
                var outcome = await pipeline.TranslateOutboundAsync(request, settings, options, localCts.Token).ConfigureAwait(false);
                if (localCts.IsCancellationRequested) return;
                state = outcome.Success
                    ? new PreviewState(key, false, outcome, null)
                    : new PreviewState(key, false, outcome, outcome.FailureReason ?? Loc.Localize("SendPreviewFailed", "Translation failed."));
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer request
            }
            catch (Exception ex)
            {
                if (!localCts.IsCancellationRequested)
                    state = new PreviewState(key, false, null, ex.GetType().Name);
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

    private static string Title() => Loc.Localize("SendPreviewTitle", "AetherGlot — Send Preview") + "###AetherGlotSend";

    /// <summary>Re-reads the localized title after the Dalamud language changes.</summary>
    public void Relocalize() => WindowName = Title();

    public void Dispose()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
