using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherGlot.Windows;

/// <summary>
/// ④-b Auto-translate viewer (read-only). Shows how a phrase travels through the pipeline
/// (ID → {{atN}} → displayed text), the categories of the game data and every phrase in
/// Japanese and English, with what the plugin uses it for.
/// </summary>
public sealed class AutoTranslateWindow : Window, IDisposable, IRelocalizable
{
    private const float LeftWidth = 220;

    private readonly Plugin plugin;

    private uint? category;          // null = all
    private string query = string.Empty;
    private bool recentOnly;

    private (AutoTranslateCatalog Catalog, uint? Category, string Query, bool Recent, int RecentCount)? cacheKey;
    private IReadOnlyList<AutoTranslateEntry> results = [];
    private Dictionary<uint, AutoTranslateUsage> usageByGroup = new();

    public AutoTranslateWindow(Plugin plugin)
        : base(Title())
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(720, 400), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        Size = new Vector2(940, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var catalog = plugin.Glossary.Catalog;
        if (catalog.Entries.Count == 0)
        {
            ImGui.TextColored(Theme.TextMuted, plugin.Glossary.IsLoading
                ? Loc.Localize("AtLoading", "Reading the game data…")
                : Loc.Localize("AtEmpty", "No auto-translate data. Enable \"Use game data\" in the Dictionary settings."));
            return;
        }

        var recent = catalog.RecentlySeen;
        Refresh(catalog, recent);

        DrawFlow();
        ImGui.Separator();

        if (ImGui.BeginChild("##categories", new Vector2(LeftWidth, 0), true))
            DrawCategories(catalog);
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##phrases", new Vector2(0, 0)))
            DrawPhrases(recent);
        ImGui.EndChild();
    }

    // ── Flow example ───────────────────────────────────────────────────

    private void DrawFlow()
    {
        ImGui.TextColored(Theme.Accent, Loc.Localize("AtFlow", "How a phrase is handled"));

        // The latest message that contained auto-translate phrases, or a fixed example
        var entry = plugin.TranslationLog.Snapshot().LastOrDefault(e => e.Message.AutoTranslateParts.Count > 0);
        string ids, placeholderized, displayed;
        if (entry is not null)
        {
            ids             = string.Join(", ", entry.Message.AutoTranslateParts.Select(p => $"{p.Group}:{p.Key}"));
            placeholderized = entry.Message.Text;
            displayed       = entry.Outcome is { Kind: InboundOutcomeKind.Translated or InboundOutcomeKind.Partial } o ? o.DisplayText : entry.Message.DisplayOriginal;
        }
        else
        {
            ids             = "2:203";
            placeholderized = "{{at0}} opo > rapu > kua-ru pls";
            displayed       = "【こんにちは！】 魔猿 → 走竜 → 猛虎の順でお願い";
        }

        Step("1", Loc.Localize("AtFlowReceived", "Received (payload IDs)"), ids, Theme.AutoTranslate);
        Step("2", Loc.Localize("AtFlowSent", "Sent to the model"), placeholderized, Theme.TextDim);
        Step("3", Loc.Localize("AtFlowShown", "Shown (phrase filled back in)"), displayed, Theme.Text);
        if (entry is null)
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("AtFlowExample", "(example — the latest received phrase appears here)"));
    }

    private static void Step(string n, string label, string value, Vector4 color)
    {
        ImGui.TextColored(Theme.TextMuted, n);
        ImGui.SameLine();
        ImGui.TextColored(Theme.TextDim, label);
        ImGui.SameLine(240);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(value);
        ImGui.PopStyleColor();
    }

    // ── Categories ─────────────────────────────────────────────────────

    private void DrawCategories(AutoTranslateCatalog catalog)
    {
        var japanese = Plugin.ClientState.ClientLanguage == Dalamud.Game.ClientLanguage.Japanese;
        if (ImGui.Selectable($"{Loc.Localize("AtAll", "All")}  ({catalog.Entries.Count:N0})", category is null))
            category = null;
        foreach (var c in catalog.Categories)
        {
            var name = TermNormalizer.StripDecorations(japanese ? c.Ja : c.En);
            if (ImGui.Selectable($"{name}  ({c.Count:N0})##cat{c.Group}", category == c.Group))
                category = c.Group;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{c.Group} · {UsageLabel(AutoTranslateUsages.Of(c))}");
        }
    }

    // ── Phrases ────────────────────────────────────────────────────────

    private void Refresh(AutoTranslateCatalog catalog, IReadOnlyDictionary<string, DateTimeOffset> recent)
    {
        var key = (catalog, category, query, recentOnly, recent.Count);
        if (cacheKey == key) return;

        if (!ReferenceEquals(cacheKey?.Catalog, catalog))
            usageByGroup = catalog.Categories.ToDictionary(c => c.Group, AutoTranslateUsages.Of);

        var q = TermNormalizer.Normalize(query, collapseRepeat: false);
        IEnumerable<AutoTranslateEntry> list = catalog.Entries;
        if (category is { } g) list = list.Where(e => e.Group == g);
        if (recentOnly) list = list.Where(e => recent.ContainsKey(e.Id)).OrderByDescending(e => recent[e.Id]);
        if (q.Length > 0)
            list = list.Where(e => TermNormalizer.Normalize(e.Ja, collapseRepeat: false).Contains(q, StringComparison.Ordinal) ||
                                   TermNormalizer.Normalize(e.En, collapseRepeat: false).Contains(q, StringComparison.Ordinal) ||
                                   e.Id == query.Trim());
        results  = list.ToList();
        cacheKey = key;
    }

    private void DrawPhrases(IReadOnlyDictionary<string, DateTimeOffset> recent)
    {
        ImGui.SetNextItemWidth(Math.Max(160, ImGui.GetContentRegionAvail().X - 260));
        ImGui.InputTextWithHint("##atquery", Loc.Localize("AtSearch", "Search Japanese, English or an ID (group:key)"), ref query, 200);
        ImGui.SameLine();
        ImGui.Checkbox(string.Format(Loc.Localize("AtRecentOnly", "Only seen in chat ({0})"), recent.Count), ref recentOnly);
        ImGui.TextColored(Theme.TextMuted, string.Format(Loc.Localize("AtResults", "{0:N0} phrases · read-only"), results.Count));

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY |
                                      ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##at", 4, flags)) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn(Loc.Localize("GlossaryColJa", "Japanese"), ImGuiTableColumnFlags.WidthStretch, 2.5f);
        ImGui.TableSetupColumn("English", ImGuiTableColumnFlags.WidthStretch, 2.5f);
        ImGui.TableSetupColumn(Loc.Localize("AtUsage", "Used for"), ImGuiTableColumnFlags.WidthStretch, 2.4f);
        ImGui.TableHeadersRow();

        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(results.Count, ImGui.GetTextLineHeightWithSpacing());
        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                if (i < 0 || i >= results.Count) continue;
                var e = results[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(recent.ContainsKey(e.Id) ? Theme.Accent : Theme.TextMuted, e.Id);
                ImGui.TableNextColumn();
                ImGui.TextColored(Theme.AutoTranslate, e.Ja);
                ImGui.TableNextColumn();
                ImGui.TextColored(Theme.AutoTranslate, e.En);
                ImGui.TableNextColumn();
                var usage = usageByGroup.TryGetValue(e.Group, out var u) ? u : AutoTranslateUsage.DisplayAsIs;
                ImGui.TextColored(usage == AutoTranslateUsage.DisplayAsIs ? Theme.TextMuted : Theme.TextDim, UsageLabel(usage));
            }
        }
        clipper.End();
        clipper.Destroy();
        ImGui.EndTable();
    }

    private static string UsageLabel(AutoTranslateUsage usage) => usage switch
    {
        AutoTranslateUsage.JobName    => Loc.Localize("AtUsageJob", "Received: fixed · Sent: replacement candidate"),
        AutoTranslateUsage.ActionName => Loc.Localize("AtUsageAction", "Also matches typed action names"),
        AutoTranslateUsage.PlaceName  => Loc.Localize("AtUsagePlace", "Also used as a context hint"),
        _                             => Loc.Localize("AtUsageDisplay", "Received: shown as is"),
    };

    private static string Title() => Loc.Localize("AtTitle", "AetherGlot — Auto-translate") + "###AetherGlotAutoTranslate";

    /// <summary>Re-reads the localized title after the Dalamud language changes.</summary>
    public void Relocalize() => WindowName = Title();

    public void Dispose() { }
}
