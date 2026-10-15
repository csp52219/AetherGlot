using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherGlot.Windows;

/// <summary>
/// ① Translation log: one line per message (time / sender / translation); click to expand the
/// original, term chips and a reply button. Auto-scroll pauses while scrolled up and a
/// "N new" button jumps back to the latest message.
/// </summary>
public sealed class TranslationLogWindow : Window, IDisposable, IRelocalizable
{
    private readonly Plugin plugin;
    private readonly Configuration config;
    private readonly HashSet<long> expanded = [];

    // Scroll state
    private long lastSeenId;
    private bool scrollToBottom = true;
    private bool wasAtBottom = true;

    // "Add to dictionary" popup
    private string addSurface = string.Empty;
    private string addMeaning = string.Empty;
    private bool openAddPopup;

    private static readonly (LogFilter Filter, string LocKey, string Fallback)[] Filters =
    [
        (LogFilter.PartyOnly,    "LogFilterParty",    "Party only"),
        (LogFilter.All,          "LogFilterAll",      "All"),
        (LogFilter.MentionsOnly, "LogFilterMentions", "To me"),
    ];

    public TranslationLogWindow(Plugin plugin)
        : base(Title())
    {
        this.plugin = plugin;
        config      = plugin.Configuration;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 240), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        Size = new Vector2(680, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        DrawToolbar();
        ImGui.Separator();

        var footerHeight = ImGui.GetFrameHeightWithSpacing() + 4;
        var entries = plugin.TranslationLog.Snapshot(config.Display.Log.Filter);

        if (ImGui.BeginChild("##logentries", new Vector2(0, -footerHeight)))
        {
            foreach (var entry in entries)
                DrawEntry(entry);

            UpdateScroll(entries);
        }
        ImGui.EndChild();

        ImGui.Separator();
        DrawFooter(entries);
        DrawAddToDictionaryPopup();
    }

    // ── Toolbar ─────────────────────────────────────────────────────────

    private void DrawToolbar()
    {
        var log = config.Display.Log;
        foreach (var (filter, locKey, fallback) in Filters)
        {
            var selected = log.Filter == filter;
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent with { W = 0.45f });
            if (ImGui.SmallButton(Loc.Localize(locKey, fallback)))
            {
                log.Filter = filter;
                scrollToBottom = true;
                config.Save();
            }
            if (selected) ImGui.PopStyleColor();
            ImGui.SameLine();
        }

        ImGui.Dummy(new Vector2(12, 0));
        ImGui.SameLine();
        var showOriginal = log.ShowOriginal;
        if (ImGui.Checkbox(Loc.Localize("LogShowOriginal", "Original"), ref showOriginal)) { log.ShowOriginal = showOriginal; config.Save(); }
        ImGui.SameLine();
        var showChips = log.ShowTermChips;
        if (ImGui.Checkbox(Loc.Localize("LogShowChips", "Term chips"), ref showChips)) { log.ShowTermChips = showChips; config.Save(); }

        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - 70));
        if (ImGui.SmallButton(Loc.Localize("LogSettingsBtn", "Settings")))
            plugin.OpenSettings();
    }

    // ── Entries ─────────────────────────────────────────────────────────

    private void DrawEntry(LogEntry entry)
    {
        var outcome = entry.Outcome;
        var unresolvedMode = config.Translation.UnresolvedMode;

        // "Show nothing" for partial translations hides the whole entry
        if (outcome?.Kind == InboundOutcomeKind.Partial && unresolvedMode == UnresolvedDisplayMode.Hide)
            return;

        ImGui.PushID((int)entry.Id);
        var isExpanded = expanded.Contains(entry.Id) || !config.Display.Log.OneLine;
        var highlight  = entry.MentionsMe && config.Display.Log.HighlightMentions;

        var start = ImGui.GetCursorScreenPos();
        ImGui.BeginGroup();

        // Header line: time [CH] sender: translation
        ImGui.TextColored(Theme.TextMuted, entry.Time.ToString("HH:mm"));
        ImGui.SameLine();
        ImGui.TextColored(ChannelInfo.Color(entry.Group), $"[{ChannelInfo.Tag((XivChatType)entry.Channel)}]");
        ImGui.SameLine();
        ImGui.TextColored(highlight ? Theme.Accent : Theme.Text, entry.Sender);
        if (entry.World is { } world)
        {
            ImGui.SameLine(0, 2);
            ImGui.TextColored(Theme.TextMuted, $"@{world}");
        }
        ImGui.SameLine();
        DrawStatusChip(entry);

        if (!isExpanded)
        {
            ImGui.SameLine();
            RichText.Draw(MainRuns(entry, unresolvedMode), wrap: false);
        }
        else
        {
            if (config.Display.Log.ShowOriginal && outcome?.Kind is not (InboundOutcomeKind.Skipped or InboundOutcomeKind.Fallback))
                RichText.Draw(OriginalRuns(entry.Message, Theme.TextDim), wrap: true);
            RichText.Draw(MainRuns(entry, unresolvedMode), wrap: true);
        }
        ImGui.EndGroup();

        var groupClicked = ImGui.IsItemClicked();
        if (highlight)
        {
            var end = new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X, ImGui.GetItemRectMax().Y);
            ImGui.GetWindowDrawList().AddRectFilled(start - new Vector2(4, 2), end + new Vector2(0, 2), ImGui.GetColorU32(Theme.MentionBg), 3f);
        }
        if (groupClicked && config.Display.Log.OneLine)
        {
            if (!expanded.Remove(entry.Id)) expanded.Add(entry.Id);
        }

        if (isExpanded)
            DrawDetails(entry, outcome);

        ImGui.Separator();
        ImGui.PopID();
    }

    private static void DrawStatusChip(LogEntry entry)
    {
        var outcome = entry.Outcome;
        var (text, color) = entry.State == LogEntryState.Translating
            ? (Loc.Localize("LogTranslating", "translating…"), Theme.TextMuted)
            : outcome?.Kind switch
            {
                InboundOutcomeKind.Skipped  => ($"{outcome.DetectedLanguage.ToUpperInvariant()} · {Loc.Localize("LogSkipped", "skipped")}", Theme.TextMuted),
                InboundOutcomeKind.Fallback => (Loc.Localize("LogOriginal", "original"), Theme.Warn),
                InboundOutcomeKind.Partial  => (Loc.Localize("LogPartial", "partly unresolved"), Theme.Unresolved),
                InboundOutcomeKind.Translated when !string.IsNullOrEmpty(outcome.DetectedLanguage) => (outcome.DetectedLanguage.ToUpperInvariant(), Theme.TextDim),
                null => (Loc.Localize("LogNotTranslated", "not translated"), Theme.Warn),
                _ => (string.Empty, Theme.TextDim),
            };
        if (text.Length > 0)
            ImGui.TextColored(color, text);
        else
            ImGui.TextUnformatted(string.Empty);
    }

    /// <summary>Translation (or the original when there is none) as styled runs.</summary>
    private static List<TextRun> MainRuns(LogEntry entry, UnresolvedDisplayMode mode)
    {
        var outcome = entry.Outcome;
        if (outcome is null || entry.State == LogEntryState.Translating ||
            outcome.Kind is InboundOutcomeKind.Skipped or InboundOutcomeKind.Fallback ||
            (outcome.Kind == InboundOutcomeKind.Partial && mode == UnresolvedDisplayMode.OriginalOnly))
        {
            var dim = entry.State == LogEntryState.Translating || outcome?.Kind == InboundOutcomeKind.Skipped;
            return OriginalRuns(entry.Message, dim ? Theme.TextMuted : Theme.Text);
        }

        return outcome.Segments.Select(s => s.Kind switch
        {
            SegmentKind.AutoTranslate => new TextRun(s.Text, Theme.AutoTranslate),
            SegmentKind.Unresolved    => new TextRun(s.Text, Theme.Unresolved, DashedUnderline: true),
            SegmentKind.Original      => new TextRun(s.Text, Theme.TextDim),
            _                         => new TextRun(s.Text, Theme.Text),
        }).ToList();
    }

    private static List<TextRun> OriginalRuns(PlaceholderizedMessage message, Vector4 color) =>
        Rehydrator.Original(message.Text, message.AutoTranslateTexts)
                  .Select(s => new TextRun(s.Text, s.Kind == SegmentKind.AutoTranslate ? Theme.AutoTranslate : color))
                  .ToList();

    private void DrawDetails(LogEntry entry, InboundOutcome? outcome)
    {
        var any = false;

        if (config.Display.Log.ShowTermChips && outcome?.Response is { } response)
        {
            foreach (var term in response.Terms)
            {
                var conf = term.Conf is { } c ? $" {c * 100:0}%" : string.Empty;
                var text = $"{term.Src} → {term.Dst}{conf}";
                if (any) RichText.SameLineIfFits(text);
                RichText.Chip(text, Theme.TermChip, Theme.TermChipBg);
                any = true;
                foreach (var alt in term.Alts)
                {
                    var altText = $"{Loc.Localize("LogAlt", "alt:")} {alt}";
                    RichText.SameLineIfFits(altText);
                    RichText.Chip(altText, Theme.TextDim, Theme.ChipBg);
                }
            }
        }

        if (outcome is not null)
        {
            foreach (var u in outcome.Segments.Where(s => s.Kind == SegmentKind.Unresolved))
            {
                var text = $"{u.Text} → {Loc.Localize("LogNotInDictionary", "not in dictionary, kept as is")}";
                if (any) RichText.SameLineIfFits(text);
                RichText.Chip(text, Theme.Unresolved, Theme.ChipBg, dashedBorder: true);
                ImGui.SameLine(0, 4);
                if (ImGui.SmallButton(Loc.Localize("LogAddToDictionary", "Add to dictionary") + $"##{u.Text}"))
                {
                    addSurface   = u.Text;
                    addMeaning   = u.Guess ?? string.Empty;
                    openAddPopup = true;
                }
                any = true;
            }

            if (outcome.Kind == InboundOutcomeKind.Fallback && outcome.FailureReason is { } reason)
                ImGui.TextColored(Theme.TextMuted, reason);
        }

        if (ImGui.SmallButton(Loc.Localize("LogReply", "Reply")))
            plugin.OpenReply((XivChatType)entry.Channel);
        if (outcome is not null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.Localize("LogInspect", "Inspect")))
                plugin.OpenInspector(entry);
        }
    }

    // ── Scrolling ───────────────────────────────────────────────────────

    private void UpdateScroll(IReadOnlyList<LogEntry> entries)
    {
        var latestId = entries.Count > 0 ? entries[^1].Id : 0;
        var atBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4;
        var pause    = config.Display.Log.PauseWhileScrolled;

        if (scrollToBottom || ((atBottom || wasAtBottom || !pause) && latestId > lastSeenId))
        {
            ImGui.SetScrollHereY(1f);
            lastSeenId     = latestId;
            scrollToBottom = false;
            atBottom       = true;
        }
        else if (atBottom)
        {
            lastSeenId = latestId;
        }
        wasAtBottom = atBottom;
    }

    // Shown in the footer while scrolled up and new messages arrived
    private void DrawNewMessagesButton(IReadOnlyList<LogEntry> entries)
    {
        var newCount = entries.Count(e => e.Id > lastSeenId);
        if (newCount == 0) return;

        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentText);
        if (ImGui.SmallButton(string.Format(Loc.Localize("LogNewMessages", "{0} new ↓"), newCount)))
            scrollToBottom = true;
        ImGui.PopStyleColor(2);
        ImGui.SameLine();
    }

    // ── Footer ──────────────────────────────────────────────────────────

    private void DrawFooter(IReadOnlyList<LogEntry> entries)
    {
        DrawNewMessagesButton(entries);

        var log   = plugin.TranslationLog;
        var color = log.LastCallOk switch { true => Theme.Ok, false => Theme.Error, null => Theme.TextMuted };
        ImGui.TextColored(color, "●");
        if (log.LastError is { } error && ImGui.IsItemHovered()) ImGui.SetTooltip(error);

        ImGui.SameLine();
        var model = config.Connection.InboundModel;
        ImGui.TextColored(Theme.TextDim, string.IsNullOrWhiteSpace(model) ? Loc.Localize("LogNoModel", "(no model)") : model);

        ImGui.SameLine();
        var stats = plugin.Glossary.Service.Stats;
        var version = stats.CommunityVersion ?? "-";
        ImGui.TextColored(Theme.TextDim, string.Format(Loc.Localize("LogDictionary", "Dictionary {0} ({1:N0} terms)"), version, stats.Total));

        if (log.AverageLatency is { } avg)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextDim, string.Format(Loc.Localize("LogAverage", "avg {0:0.0}s"), avg.TotalSeconds));
        }

        var label = Loc.Localize("LogInspectLatest", "Inspect latest");
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(label).X - 12));
        var disabled1 = !entries.Any(e => e.Outcome is not null);
        if (disabled1) ImGui.BeginDisabled();
        if (ImGui.SmallButton(label))
            plugin.InspectLatest();
        if (disabled1) ImGui.EndDisabled();
    }

    // ── Add to dictionary ───────────────────────────────────────────────

    private void DrawAddToDictionaryPopup()
    {
        var popupId = Loc.Localize("AddDictTitle", "Add to my dictionary") + "##adddict";
        if (openAddPopup)
        {
            ImGui.OpenPopup(popupId);
            openAddPopup = false;
        }

        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.AlwaysAutoResize)) return;

        ImGui.TextUnformatted(addSurface);
        ImGui.SetNextItemWidth(320);
        ImGui.InputTextWithHint("##meaning", Loc.Localize("AddDictMeaning", "Meaning"), ref addMeaning, 200);

        var canSave = !string.IsNullOrWhiteSpace(addMeaning);
        var disabled2 = !canSave;
        if (disabled2) ImGui.BeginDisabled();
        if (ImGui.Button(Loc.Localize("AddDictSave", "Save")))
        {
            plugin.AddToMyDictionary(addSurface, addMeaning.Trim());
            ImGui.CloseCurrentPopup();
        }
        if (disabled2) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(Loc.Localize("AddDictCancel", "Cancel")))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private static string Title() => Loc.Localize("LogWindowTitle", "AetherGlot — Translation Log") + "###AetherGlotLog";

    /// <summary>Re-reads the localized title after the Dalamud language changes.</summary>
    public void Relocalize() => WindowName = Title();

    public void Dispose() { }
}
