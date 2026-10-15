using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherGlot.Windows;

/// <summary>
/// ④ Dictionary. Left: sources with counts, pending words and update times. Center: search
/// over every term (virtualised table). Right: the sense editor for My dictionary entries, or
/// a read-only view with "copy to My dictionary and edit" for the others.
/// </summary>
public sealed class GlossaryWindow : Window, IDisposable
{
    private const float LeftWidth  = 190;
    private const float RightWidth = 340;

    private readonly Plugin plugin;

    // Left pane: a bucket, or the pending list
    private GlossaryBucket bucket = GlossaryBucket.All;
    private bool showPending;

    // Search (results cached until the glossary or the query changes)
    private string query = string.Empty;
    private TermKind? kindFilter;
    private (int Version, string Query, GlossaryBucket Bucket, TermKind? Kind)? cacheKey;
    private IReadOnlyList<TermEntry> results = [];
    private IReadOnlyDictionary<GlossaryBucket, int> counts = new Dictionary<GlossaryBucket, int>();

    // Right pane
    private TermEntry? selected;
    private Editor? editor;
    private string? editorError;
    private volatile bool saving;

    // Pending → register popup
    private string addSurface = string.Empty;
    private string addMeaning = string.Empty;
    private bool openAddPopup;

    /// <summary>Text-box state for one sense.</summary>
    private sealed class SenseEdit
    {
        public string En = string.Empty;
        public string Ja = string.Empty;
        public int Priority;
        public int InCombat;   // 0 = either, 1 = in combat, 2 = out of combat
        public string Words = string.Empty;
        public string PartyJobs = string.Empty;
        public string Note = string.Empty;
    }

    /// <summary>Editable copy of a My dictionary entry.</summary>
    private sealed class Editor
    {
        public string Id = string.Empty;
        public bool IsNew;
        public TermKind Kind;
        public string AliasesEn = string.Empty;
        public string AliasesJa = string.Empty;
        public AmbiguityBehavior OnAmbiguous;
        public List<SenseEdit> Senses = [];

        public static Editor From(TermEntry e, bool isNew) => new()
        {
            Id = e.Id, IsNew = isNew, Kind = e.Kind, OnAmbiguous = e.OnAmbiguous,
            AliasesEn = GlossaryBrowser.FormatList(e.AliasesEn),
            AliasesJa = GlossaryBrowser.FormatList(e.AliasesJa),
            Senses = e.Senses.Select(s => new SenseEdit
            {
                En = s.En, Ja = s.Ja, Priority = s.Priority, Note = s.Note ?? string.Empty,
                InCombat = s.Rules.InCombat switch { true => 1, false => 2, null => 0 },
                Words = GlossaryBrowser.FormatList(s.Rules.Words),
                PartyJobs = GlossaryBrowser.FormatList(s.Rules.PartyJobs),
            }).ToList(),
        };

        public TermEntry ToEntry() => new()
        {
            Id = Id, Kind = Kind, Source = TermSource.User, OnAmbiguous = OnAmbiguous,
            AliasesEn = GlossaryBrowser.ParseList(AliasesEn),
            AliasesJa = GlossaryBrowser.ParseList(AliasesJa),
            Senses = Senses.Select(s => new TermSense
            {
                En = s.En, Ja = s.Ja, Priority = s.Priority, Note = s.Note,
                Rules = new SenseRules
                {
                    InCombat  = s.InCombat switch { 1 => true, 2 => false, _ => null },
                    Words     = GlossaryBrowser.ParseList(s.Words),
                    PartyJobs = GlossaryBrowser.ParseList(s.PartyJobs),
                },
            }).ToList(),
        };
    }

    public GlossaryWindow(Plugin plugin)
        : base(Loc.Localize("GlossaryTitle", "AetherGlot — Dictionary") + "###AetherGlotGlossary")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(820, 420), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        Size = new Vector2(1060, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>Opens the window with <paramref name="text"/> in the search box.</summary>
    public void OpenWithQuery(string text)
    {
        query = text;
        showPending = false;
        IsOpen = true;
    }

    public override void Draw()
    {
        RefreshResults();

        if (ImGui.BeginChild("##left", new Vector2(LeftWidth, 0), true))
            DrawLeft();
        ImGui.EndChild();

        ImGui.SameLine();
        var centerWidth = Math.Max(240, ImGui.GetContentRegionAvail().X - RightWidth - ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.BeginChild("##center", new Vector2(centerWidth, 0)))
        {
            if (showPending) DrawPending();
            else DrawSearch();
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##right", new Vector2(0, 0), true))
            DrawRight();
        ImGui.EndChild();

        DrawRegisterPopup();
    }

    // ── Left ───────────────────────────────────────────────────────────

    private static readonly (GlossaryBucket Bucket, string LocKey, string Fallback)[] Buckets =
    [
        (GlossaryBucket.All,           "GlossaryAll",       "All"),
        (GlossaryBucket.AutoTranslate, "GlossaryAt",        "Auto-translate"),
        (GlossaryBucket.GameNames,     "GlossaryGameNames", "Game names"),
        (GlossaryBucket.Community,     "GlossaryCommunity", "Community"),
        (GlossaryBucket.Mine,          "GlossaryMine",      "My dictionary"),
    ];

    private void DrawLeft()
    {
        foreach (var (b, locKey, fallback) in Buckets)
        {
            var count = counts.TryGetValue(b, out var c) ? c : 0;
            if (ImGui.Selectable($"{Loc.Localize(locKey, fallback)}  ({count:N0})##bucket{b}", !showPending && bucket == b))
            {
                bucket = b;
                showPending = false;
            }
        }

        ImGui.Separator();
        var pending = plugin.Glossary.Pending.Items.Count;
        if (ImGui.Selectable($"{Loc.Localize("GlossaryPending", "Pending approval")}  ({pending:N0})##pending", showPending))
            showPending = true;

        ImGui.Separator();
        var stats = plugin.Glossary.Service.Stats;
        ImGui.TextColored(Theme.TextMuted, Loc.Localize("GlossaryUpdated", "Last updated"));
        ImGui.TextColored(Theme.TextDim, $"{Loc.Localize("GlossaryCommunity", "Community")} v{stats.CommunityVersion ?? "-"}");
        ImGui.TextColored(Theme.TextMuted, Time(stats.CommunityUpdated));
        ImGui.TextColored(Theme.TextDim, Loc.Localize("GlossaryGameData", "Game data"));
        ImGui.TextColored(Theme.TextMuted, Time(stats.GameDataLoaded));

        ImGui.Spacing();
        var loading = plugin.Glossary.IsLoading;
        if (loading) ImGui.BeginDisabled();
        if (ImGui.Button(loading ? Loc.Localize("GlossaryUpdating", "Updating…") : Loc.Localize("DictUpdateNow", "Update now"), new Vector2(-1, 0)))
            _ = plugin.Glossary.ReloadAsync(forceDownload: true);
        if (loading) ImGui.EndDisabled();
        if (plugin.Glossary.Community?.Error is { } error)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Warn);
            ImGui.TextWrapped(error);
            ImGui.PopStyleColor();
        }
    }

    private static string Time(DateTimeOffset? t) => t is { } v ? v.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";

    // ── Center: search ─────────────────────────────────────────────────

    private void RefreshResults()
    {
        var service = plugin.Glossary.Service;
        var key = (service.Version, query, bucket, kindFilter);
        if (cacheKey == key) return;
        var all = service.AllEntries;
        counts   = GlossaryBrowser.Counts(all);
        results  = GlossaryBrowser.Search(all, query, bucket, kindFilter);
        cacheKey = key;

        // Keep the selection pointing at the rebuilt entry
        if (selected is not null)
            selected = all.FirstOrDefault(e => e.Id == selected.Id && e.Source == selected.Source);
    }

    private void DrawSearch()
    {
        ImGui.SetNextItemWidth(Math.Max(120, ImGui.GetContentRegionAvail().X - 250));
        ImGui.InputTextWithHint("##query", Loc.Localize("GlossarySearch", "Search English, Japanese or abbreviations"), ref query, 200);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(130);
        if (ImGui.BeginCombo("##kind", kindFilter is { } k ? KindLabel(k) : Loc.Localize("GlossaryAllKinds", "All kinds")))
        {
            if (ImGui.Selectable(Loc.Localize("GlossaryAllKinds", "All kinds"), kindFilter is null)) kindFilter = null;
            foreach (var kind in Enum.GetValues<TermKind>())
                if (ImGui.Selectable(KindLabel(kind), kindFilter == kind)) kindFilter = kind;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.Localize("GlossaryAdd", "+ Add")))
            StartNew(string.Empty);

        ImGui.TextColored(Theme.TextMuted, string.Format(Loc.Localize("GlossaryResults", "{0:N0} terms"), results.Count));

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY |
                                      ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##terms", 5, flags)) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(Loc.Localize("GlossaryColEn", "English / abbr."), ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn(Loc.Localize("GlossaryColJa", "Japanese"), ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn(Loc.Localize("GlossaryColKind", "Kind"), ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn(Loc.Localize("GlossaryColRules", "Context"), ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn(Loc.Localize("GlossaryColSource", "Source"), ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableHeadersRow();

        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(results.Count, ImGui.GetTextLineHeightWithSpacing());
        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i < 0 || i >= results.Count) continue;
                DrawRow(results[i], i);
            }
        }
        clipper.End();
        clipper.Destroy();
        ImGui.EndTable();
    }

    private void DrawRow(TermEntry e, int index)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var isSelected = ReferenceEquals(selected, e);
        var en = e.AliasesEn.Count > 0 ? $"{e.En}  ({string.Join(", ", e.AliasesEn.Take(3))})" : e.En;
        if (ImGui.Selectable($"{en}##row{index}", isSelected, ImGuiSelectableFlags.SpanAllColumns))
            Select(e);

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(e.IsPolysemous ? string.Join(" / ", e.Senses.Select(s => s.Ja)) : e.Ja);
        ImGui.TableNextColumn();
        ImGui.TextColored(Theme.TextDim, KindLabel(e.Kind));
        ImGui.TableNextColumn();
        var rules = GlossaryBrowser.RuleCount(e);
        if (e.IsPolysemous || rules > 0)
            ImGui.TextColored(Theme.TermChip, string.Format(Loc.Localize("GlossaryRules", "{0} senses · {1} rules"), e.Senses.Count, rules));
        else
            ImGui.TextColored(Theme.TextMuted, "—");
        ImGui.TableNextColumn();
        ImGui.TextColored(SourceColor(e.Source), SourceLabel(e));
    }

    // ── Center: pending ────────────────────────────────────────────────

    private void DrawPending()
    {
        var items = plugin.Glossary.Pending.Items;
        ImGui.TextColored(Theme.TextDim, Loc.Localize("GlossaryPendingHelp", "Words the model could not translate. Register them so they are translated next time."));
        if (items.Count == 0)
        {
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("GlossaryPendingEmpty", "Nothing pending."));
            return;
        }

        if (!ImGui.BeginTable("##pending", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(Loc.Localize("GlossaryPendingWord", "Word"), ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn(Loc.Localize("GlossaryPendingGuess", "Model's guess"), ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn(Loc.Localize("GlossaryPendingSeen", "Seen"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, 130);
        ImGui.TableHeadersRow();

        foreach (var item in items)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.Unresolved, item.Text);
            if (item.Example is { } example && ImGui.IsItemHovered()) ImGui.SetTooltip(example);
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.TextDim, item.Guess ?? "—");
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.TextMuted, $"×{item.Count}");
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"{Loc.Localize("GlossaryRegister", "Register")}##reg{item.Text}"))
            {
                addSurface   = item.Text;
                addMeaning   = item.Guess ?? string.Empty;
                openAddPopup = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton($"{Loc.Localize("GlossaryIgnore", "Ignore")}##ign{item.Text}"))
            {
                var text = item.Text;
                _ = Task.Run(() =>
                {
                    if (plugin.Glossary.Pending.Remove(text)) plugin.Glossary.Pending.Save();
                });
            }
        }
        ImGui.EndTable();
    }

    private void DrawRegisterPopup()
    {
        var popupId = Loc.Localize("AddDictTitle", "Add to my dictionary") + "##glossaryadd";
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
        if (!canSave) ImGui.BeginDisabled();
        if (ImGui.Button(Loc.Localize("AddDictSave", "Save")))
        {
            plugin.AddToMyDictionary(addSurface, addMeaning.Trim());
            ImGui.CloseCurrentPopup();
        }
        if (!canSave) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(Loc.Localize("GlossaryEditFull", "Edit with senses…")))
        {
            StartNew(addSurface);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.Localize("AddDictCancel", "Cancel")))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    // ── Right: details / editor ────────────────────────────────────────

    private void Select(TermEntry e)
    {
        selected    = e;
        editorError = null;
        editor      = e.Source == TermSource.User ? Editor.From(GlossaryBrowser.CopyAsUser(e), isNew: false) : null;
    }

    private void StartNew(string surface)
    {
        selected    = null;
        editorError = null;
        var clientJapanese = Plugin.ClientState.ClientLanguage == Dalamud.Game.ClientLanguage.Japanese;
        var sense = new SenseEdit();
        if (TermNormalizer.IsJapanese(surface)) sense.Ja = surface;
        else if (!clientJapanese || surface.Length > 0) sense.En = surface;
        editor = new Editor { IsNew = true, Kind = TermKind.Slang, Senses = [sense] };
    }

    private void DrawRight()
    {
        if (editor is not null)
        {
            DrawEditor(editor);
            return;
        }
        if (selected is null)
        {
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("GlossarySelectHint", "Select a term to see its meanings, or add your own."));
            return;
        }
        DrawReadOnly(selected);
    }

    private void DrawReadOnly(TermEntry e)
    {
        ImGui.TextColored(SourceColor(e.Source), SourceLabel(e));
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, KindLabel(e.Kind));
        if (e.AutoTranslateId is { } at)
            ImGui.TextColored(Theme.AutoTranslate, $"{Loc.Localize("GlossaryAtId", "Auto-translate")} {at}");
        if (e.AliasesEn.Count > 0) Labeled("EN", GlossaryBrowser.FormatList(e.AliasesEn));
        if (e.AliasesJa.Count > 0) Labeled("JA", GlossaryBrowser.FormatList(e.AliasesJa));
        ImGui.Separator();

        foreach (var (s, i) in e.Senses.Select((s, i) => (s, i)))
        {
            ImGui.TextColored(Theme.Accent, $"{i + 1}.");
            ImGui.SameLine();
            ImGui.TextUnformatted($"{s.En}  ⇔  {s.Ja}");
            if (s.Priority != 0) Labeled(Loc.Localize("GlossaryPriority", "Priority"), s.Priority.ToString());
            if (s.Rules.InCombat is { } c) Labeled(Loc.Localize("GlossaryInCombat", "Combat"), c ? Loc.Localize("GlossaryInCombatYes", "in combat") : Loc.Localize("GlossaryInCombatNo", "out of combat"));
            if (s.Rules.Words.Count > 0) Labeled(Loc.Localize("GlossaryWords", "Words"), GlossaryBrowser.FormatList(s.Rules.Words));
            if (s.Rules.PartyJobs.Count > 0) Labeled(Loc.Localize("GlossaryJobs", "Party jobs"), GlossaryBrowser.FormatList(s.Rules.PartyJobs));
            if (s.Note is { Length: > 0 } note) Labeled(Loc.Localize("GlossaryNote", "Note"), note);
            ImGui.Spacing();
        }
        if (e.IsPolysemous)
            Labeled(Loc.Localize("GlossaryOnAmbiguous", "When unsure"), AmbiguityLabel(e.OnAmbiguous));

        ImGui.Separator();
        if (ImGui.Button(Loc.Localize("GlossaryCopyToMine", "Copy to My dictionary and edit")))
        {
            editor = Editor.From(GlossaryBrowser.CopyAsUser(e), isNew: true);
            editorError = null;
        }
        ImGui.TextColored(Theme.TextMuted, Loc.Localize("GlossaryPrecedence", "My dictionary > game data > community."));
    }

    private void DrawEditor(Editor ed)
    {
        ImGui.TextColored(Theme.Accent, ed.IsNew ? Loc.Localize("GlossaryNewEntry", "New entry (My dictionary)") : Loc.Localize("GlossaryEditEntry", "Edit (My dictionary)"));
        if (!ed.IsNew) ImGui.TextColored(Theme.TextMuted, ed.Id);

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##editkind", KindLabel(ed.Kind)))
        {
            foreach (var kind in Enum.GetValues<TermKind>())
                if (ImGui.Selectable(KindLabel(kind), ed.Kind == kind)) ed.Kind = kind;
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##aen", Loc.Localize("GlossaryAliasesEn", "English spellings / abbreviations (comma separated)"), ref ed.AliasesEn, 400);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##aja", Loc.Localize("GlossaryAliasesJa", "Japanese spellings (comma separated)"), ref ed.AliasesJa, 400);

        ImGui.Separator();
        var removeAt = -1;
        for (var i = 0; i < ed.Senses.Count; i++)
        {
            var s = ed.Senses[i];
            ImGui.PushID($"sense{i}");
            ImGui.TextColored(Theme.Accent, $"{Loc.Localize("GlossarySense", "Meaning")} {i + 1}");
            if (ed.Senses.Count > 1)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(Loc.Localize("GlossaryRemoveSense", "Remove"))) removeAt = i;
            }
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##en", "English", ref s.En, 200);
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##ja", "日本語", ref s.Ja, 200);

            ImGui.SetNextItemWidth(90);
            ImGui.InputInt(Loc.Localize("GlossaryPriority", "Priority") + "##prio", ref s.Priority);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.Localize("GlossaryPriorityTip", "Lower wins when nothing else decides."));

            ImGui.TextUnformatted(Loc.Localize("GlossaryInCombat", "Combat"));
            ImGui.SameLine();
            if (ImGui.RadioButton(Loc.Localize("GlossaryEither", "either") + "##c0", s.InCombat == 0)) s.InCombat = 0;
            ImGui.SameLine();
            if (ImGui.RadioButton(Loc.Localize("GlossaryInCombatYes", "in combat") + "##c1", s.InCombat == 1)) s.InCombat = 1;
            ImGui.SameLine();
            if (ImGui.RadioButton(Loc.Localize("GlossaryInCombatNo", "out of combat") + "##c2", s.InCombat == 2)) s.InCombat = 2;

            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##words", Loc.Localize("GlossaryWordsHint", "Words seen with this meaning (brb, dot, …)"), ref s.Words, 400);
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##jobs", Loc.Localize("GlossaryJobsHint", "Party jobs that suggest it (SCH, WHM, …)"), ref s.PartyJobs, 200);
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##note", Loc.Localize("GlossaryNote", "Note"), ref s.Note, 200);
            ImGui.Separator();
            ImGui.PopID();
        }
        if (removeAt >= 0) ed.Senses.RemoveAt(removeAt);

        if (ImGui.Button(Loc.Localize("GlossaryAddSense", "+ Add meaning")))
            ed.Senses.Add(new SenseEdit { Priority = ed.Senses.Count });

        if (ed.Senses.Count > 1)
        {
            ImGui.TextUnformatted(Loc.Localize("GlossaryOnAmbiguous", "When unsure"));
            ImGui.SameLine();
            if (ImGui.RadioButton(AmbiguityLabel(AmbiguityBehavior.ShowAll), ed.OnAmbiguous == AmbiguityBehavior.ShowAll)) ed.OnAmbiguous = AmbiguityBehavior.ShowAll;
            ImGui.SameLine();
            if (ImGui.RadioButton(AmbiguityLabel(AmbiguityBehavior.PreferFirst), ed.OnAmbiguous == AmbiguityBehavior.PreferFirst)) ed.OnAmbiguous = AmbiguityBehavior.PreferFirst;
        }

        ImGui.Spacing();
        if (saving) ImGui.BeginDisabled();
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.AccentText);
        var save = ImGui.Button(Loc.Localize("GlossarySave", "Save"));
        ImGui.PopStyleColor(2);
        ImGui.SameLine();
        var cancel = ImGui.Button(Loc.Localize("AddDictCancel", "Cancel"));
        var delete = false;
        if (!ed.IsNew)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Error);
            delete = ImGui.Button(Loc.Localize("GlossaryDelete", "Delete")) && ImGui.GetIO().KeyShift;
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.Localize("GlossaryDeleteTip", "Hold Shift and click to delete."));
        }
        if (saving) ImGui.EndDisabled();

        if (editorError is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Warn);
            ImGui.TextWrapped(editorError);
            ImGui.PopStyleColor();
        }

        if (save) Save(ed);
        else if (cancel)
        {
            // Back to the saved version (or to the read-only view)
            editorError = null;
            editor = selected is { Source: TermSource.User } current ? Editor.From(GlossaryBrowser.CopyAsUser(current), false) : null;
        }
        else if (delete) Delete(ed);
    }

    private void Save(Editor ed)
    {
        var entry = ed.ToEntry();
        if (ed.IsNew) entry.Id = string.Empty;
        if (GlossaryBrowser.PrepareUserEntry(entry) is { } error)
        {
            editorError = error;
            return;
        }
        if (ed.IsNew && plugin.Glossary.User.Entries.Any(e => e.Id == entry.Id))
        {
            editorError = string.Format(Loc.Localize("GlossaryDuplicate", "\"{0}\" is already in My dictionary; edit that entry instead."), entry.Id);
            return;
        }

        editorError = null;
        saving = true;
        _ = Task.Run(() =>
        {
            try
            {
                plugin.Glossary.User.Upsert(entry);
                foreach (var surface in entry.AliasesEn.Concat(entry.AliasesJa).Concat(entry.Senses.SelectMany(s => new[] { s.En, s.Ja })))
                    if (plugin.Glossary.Pending.Remove(surface)) plugin.Glossary.Pending.Save();
                plugin.Glossary.Rebuild();
                editor = null;
                selected = plugin.Glossary.Service.AllEntries.FirstOrDefault(e => e.Id == entry.Id && e.Source == TermSource.User);
                if (selected is not null) editor = Editor.From(GlossaryBrowser.CopyAsUser(selected), false);
            }
            catch (Exception ex)
            {
                editorError = string.Format(Loc.Localize("GlossarySaveFailed", "Could not save ({0})."), ex.GetType().Name);
                Plugin.Log.Error("[AetherGlot] Could not save {File} ({Type}).", GlossaryManager.UserFileName, ex.GetType().Name);
            }
            finally
            {
                saving = false;
            }
        });
    }

    private void Delete(Editor ed)
    {
        var id = ed.Id;
        saving = true;
        _ = Task.Run(() =>
        {
            try
            {
                if (plugin.Glossary.User.Remove(id)) plugin.Glossary.Rebuild();
                editor = null;
                selected = null;
            }
            catch (Exception ex)
            {
                editorError = string.Format(Loc.Localize("GlossarySaveFailed", "Could not save ({0})."), ex.GetType().Name);
            }
            finally
            {
                saving = false;
            }
        });
    }

    // ── Labels ─────────────────────────────────────────────────────────

    private static void Labeled(string label, string value)
    {
        ImGui.TextColored(Theme.TextMuted, label);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
        ImGui.TextWrapped(value);
        ImGui.PopStyleColor();
    }

    internal static string KindLabel(TermKind kind) => kind switch
    {
        TermKind.AutoTranslate => Loc.Localize("KindAutoTranslate", "Auto-translate"),
        TermKind.Action        => Loc.Localize("KindAction", "Action"),
        TermKind.Status        => Loc.Localize("KindStatus", "Status"),
        TermKind.Job           => Loc.Localize("KindJob", "Job"),
        TermKind.Content       => Loc.Localize("KindContent", "Duty"),
        TermKind.Place         => Loc.Localize("KindPlace", "Place"),
        TermKind.Mechanic      => Loc.Localize("KindMechanic", "Mechanic"),
        TermKind.Slang         => Loc.Localize("KindSlang", "Slang"),
        TermKind.Abbreviation  => Loc.Localize("KindAbbreviation", "Abbreviation"),
        _                      => Loc.Localize("KindOther", "Other"),
    };

    private static string SourceLabel(TermEntry e) => GlossaryBrowser.BucketOf(e) switch
    {
        GlossaryBucket.Mine          => Loc.Localize("GlossaryMine", "My dictionary"),
        GlossaryBucket.Community     => Loc.Localize("GlossaryCommunity", "Community"),
        GlossaryBucket.AutoTranslate => Loc.Localize("GlossaryAt", "Auto-translate"),
        _                            => Loc.Localize("GlossaryGameData", "Game data"),
    };

    private static Vector4 SourceColor(TermSource source) => source switch
    {
        TermSource.User      => Theme.Accent,
        TermSource.Community => Theme.TermChip,
        _                    => Theme.AutoTranslate,
    };

    private static string AmbiguityLabel(AmbiguityBehavior b) => b == AmbiguityBehavior.ShowAll
        ? Loc.Localize("GlossaryShowAll", "show every meaning")
        : Loc.Localize("GlossaryPreferFirst", "use the top priority");

    public void Dispose() { }
}
