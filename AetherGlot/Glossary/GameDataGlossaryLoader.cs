using Lumina.Excel;
using Lumina.Excel.Sheets;
using ClientLanguage = Dalamud.Game.ClientLanguage;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace AetherGlot.Glossary;

/// <summary>Terms and the auto-translate catalog built from the game data.</summary>
internal sealed record GameDataResult(IReadOnlyList<TermEntry> Terms, AutoTranslateCatalog Catalog, DateTimeOffset LoadedAt, IReadOnlyList<string> Errors);

/// <summary>
/// Reads Japanese and English names from the game's Excel sheets so terms follow every patch:
/// ClassJob, Action (player actions), Status, Completion (auto-translate, with the sheets its
/// groups point to), ContentFinderCondition and the place names of territories.
/// Runs on a background thread.
/// </summary>
internal static class GameDataGlossaryLoader
{
    // Upper bound per auto-translate group, against malformed lookup tables
    private const int MaxRowsPerGroup = 8000;

    public static GameDataResult Load(IDataManager data, IPluginLog log)
    {
        var terms  = new List<TermEntry>();
        var errors = new List<string>();
        var catalog = AutoTranslateCatalog.Empty;

        void Section(string name, System.Action body)
        {
            try { body(); }
            catch (Exception ex)
            {
                errors.Add($"{name}: {ex.GetType().Name}");
                log.Warning("Could not load {Section} from game data ({Type}: {Message}).", name, ex.GetType().Name, ex.Message);
            }
        }

        // Order matters within game data: the first entry for a spelling wins
        Section("ClassJob", () => terms.AddRange(LoadJobs(data)));
        Section("Action", () => terms.AddRange(LoadActions(data)));
        Section("Status", () => terms.AddRange(LoadStatuses(data)));
        Section("Completion", () =>
        {
            catalog = LoadAutoTranslate(data, log);
            terms.AddRange(catalog.ToTermEntries());
        });
        Section("ContentFinderCondition", () => terms.AddRange(LoadContent(data)));
        Section("PlaceName", () => terms.AddRange(LoadPlaces(data)));

        return new GameDataResult(terms, catalog, DateTimeOffset.UtcNow, errors);
    }

    private static IEnumerable<TermEntry> LoadJobs(IDataManager data)
    {
        var en = data.GetExcelSheet<ClassJob>(ClientLanguage.English);
        var ja = data.GetExcelSheet<ClassJob>(ClientLanguage.Japanese);
        foreach (var row in en)
        {
            if (row.RowId == 0 || ja.GetRowOrDefault(row.RowId) is not { } jaRow) continue;

            var enName = Text(row.NameEnglish);
            if (enName.Length == 0) enName = Text(row.Name);
            var jaName = Text(jaRow.Name);
            if (enName.Length == 0 || jaName.Length == 0) continue;

            var entry = TermEntry.Simple($"job:{row.RowId}", TermKind.Job, TermSource.GameData, enName, jaName);
            var abbr = Text(row.Abbreviation);
            if (abbr.Length >= 2) entry.AliasesEn.Add(abbr);
            var jaAbbr = Text(jaRow.Abbreviation);
            if (jaAbbr.Length >= 2 && jaAbbr != jaName) entry.AliasesJa.Add(jaAbbr);
            yield return entry;
        }
    }

    private static IEnumerable<TermEntry> LoadActions(IDataManager data)
    {
        var en   = data.GetExcelSheet<LuminaAction>(ClientLanguage.English);
        var ja   = data.GetExcelSheet<LuminaAction>(ClientLanguage.Japanese);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in en)
        {
            if (!row.IsPlayerAction || row.IsPvP) continue;
            if (ja.GetRowOrDefault(row.RowId) is not { } jaRow) continue;
            var enName = Text(row.Name);
            var jaName = Text(jaRow.Name);
            if (enName.Length == 0 || jaName.Length == 0 || !seen.Add(TermNormalizer.Normalize(enName))) continue;
            yield return TermEntry.Simple($"action:{row.RowId}", TermKind.Action, TermSource.GameData, enName, jaName);
        }
    }

    private static IEnumerable<TermEntry> LoadStatuses(IDataManager data)
    {
        var en   = data.GetExcelSheet<Status>(ClientLanguage.English);
        var ja   = data.GetExcelSheet<Status>(ClientLanguage.Japanese);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in en)
        {
            if (ja.GetRowOrDefault(row.RowId) is not { } jaRow) continue;
            var enName = Text(row.Name);
            var jaName = Text(jaRow.Name);
            if (enName.Length == 0 || jaName.Length == 0 || !seen.Add(TermNormalizer.Normalize(enName))) continue;
            yield return TermEntry.Simple($"status:{row.RowId}", TermKind.Status, TermSource.GameData, enName, jaName);
        }
    }

    private static IEnumerable<TermEntry> LoadContent(IDataManager data)
    {
        var en   = data.GetExcelSheet<ContentFinderCondition>(ClientLanguage.English);
        var ja   = data.GetExcelSheet<ContentFinderCondition>(ClientLanguage.Japanese);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in en)
        {
            if (ja.GetRowOrDefault(row.RowId) is not { } jaRow) continue;
            var enName = Text(row.Name);
            var jaName = Text(jaRow.Name);
            if (enName.Length == 0 || jaName.Length == 0 || !seen.Add(TermNormalizer.Normalize(enName))) continue;
            yield return TermEntry.Simple($"content:{row.RowId}", TermKind.Content, TermSource.GameData, enName, jaName);
        }
    }

    private static IEnumerable<TermEntry> LoadPlaces(IDataManager data)
    {
        var placeEn = data.GetExcelSheet<PlaceName>(ClientLanguage.English);
        var placeJa = data.GetExcelSheet<PlaceName>(ClientLanguage.Japanese);
        var seen    = new HashSet<uint>();
        foreach (var territory in data.GetExcelSheet<TerritoryType>(ClientLanguage.English))
        {
            var id = territory.PlaceName.RowId;
            if (id == 0 || !seen.Add(id)) continue;
            if (placeEn.GetRowOrDefault(id) is not { } en || placeJa.GetRowOrDefault(id) is not { } ja) continue;
            var enName = Text(en.Name);
            var jaName = Text(ja.Name);
            if (enName.Length == 0 || jaName.Length == 0) continue;
            yield return TermEntry.Simple($"place:{id}", TermKind.Place, TermSource.GameData, enName, jaName);
        }
    }

    /// <summary>
    /// Builds the auto-translate catalog from the Completion sheet. Group headers have a
    /// LookupTable: "@" means the phrases are Completion rows of that group (key = RowId);
    /// "Sheet[...]" means the phrases are rows of that sheet (key = that sheet's RowId).
    /// </summary>
    private static AutoTranslateCatalog LoadAutoTranslate(IDataManager data, IPluginLog log)
    {
        var en = data.GetExcelSheet<Completion>(ClientLanguage.English);
        var ja = data.GetExcelSheet<Completion>(ClientLanguage.Japanese);

        var headers = new List<(uint Group, LookupTable Table, string Raw, string CatEn, string CatJa)>();
        foreach (var row in en)
        {
            var raw = Text(row.LookupTable);
            if (raw.Length == 0) continue;
            if (LookupTable.Parse(raw) is not { } table)
            {
                log.Debug("Unsupported auto-translate lookup table for group {Group}.", row.Group);
                continue;
            }
            var catJa = ja.GetRowOrDefault(row.RowId) is { } j ? TermNormalizer.StripDecorations(Text(j.Text)) : string.Empty;
            headers.Add((row.Group, table, raw, TermNormalizer.StripDecorations(Text(row.Text)), catJa));
        }

        var entries    = new List<AutoTranslateEntry>();
        var categories = new List<AutoTranslateCategory>();
        var inCompletion = headers.Where(h => h.Table.InCompletion)
                                  .GroupBy(h => h.Group)
                                  .ToDictionary(g => g.Key, g => g.First());

        // "@" groups: phrases are Completion rows without a lookup table
        foreach (var row in en)
        {
            if (Text(row.LookupTable).Length != 0 || !inCompletion.TryGetValue(row.Group, out var h)) continue;
            if (ja.GetRowOrDefault(row.RowId) is not { } jaRow) continue;
            var enText = Text(row.Text);
            var jaText = Text(jaRow.Text);
            if (enText.Length == 0 || jaText.Length == 0) continue;
            entries.Add(new AutoTranslateEntry(row.Group, row.RowId, h.CatJa, h.CatEn, jaText, enText));
        }

        // Sheet groups
        foreach (var h in headers.Where(h => !h.Table.InCompletion))
        {
            try
            {
                var sheetEn = data.GetExcelSheet<RawRow>(ClientLanguage.English, h.Table.Sheet);
                var sheetJa = data.GetExcelSheet<RawRow>(ClientLanguage.Japanese, h.Table.Sheet);
                var columns = h.Table.Columns.Count > 0 ? h.Table.Columns : [0];
                var count   = 0;
                foreach (var rowId in RowIds(sheetEn, h.Table))
                {
                    if (count >= MaxRowsPerGroup) break;
                    if (sheetEn.GetRowOrDefault(rowId) is not { } rEn || sheetJa.GetRowOrDefault(rowId) is not { } rJa) continue;
                    var enText = FirstString(rEn, columns);
                    var jaText = FirstString(rJa, columns);
                    if (enText.Length == 0 || jaText.Length == 0) continue;
                    entries.Add(new AutoTranslateEntry(h.Group, rowId, h.CatJa, h.CatEn, jaText, enText));
                    count++;
                }
            }
            catch (Exception ex)
            {
                log.Debug("Skipped auto-translate group {Group} ({Sheet}): {Type}.", h.Group, h.Table.Sheet, ex.GetType().Name);
            }
        }

        foreach (var h in headers.OrderBy(h => h.Group))
            categories.Add(new AutoTranslateCategory(h.Group, h.CatJa, h.CatEn, h.Raw, entries.Count(e => e.Group == h.Group)));

        return new AutoTranslateCatalog(entries, categories);
    }

    private static IEnumerable<uint> RowIds(ExcelSheet<RawRow> sheet, LookupTable table)
    {
        if (table.Ranges.Count == 0)
        {
            foreach (var row in sheet) yield return row.RowId;
            yield break;
        }
        foreach (var (from, to) in table.Ranges)
            for (var id = from; id <= to && id - from < MaxRowsPerGroup; id++)
                yield return id;
    }

    private static string FirstString(RawRow row, IReadOnlyList<int> columns)
    {
        foreach (var col in columns)
        {
            try
            {
                var s = row.ReadStringColumn(col).ExtractText().Trim();
                if (s.Length > 0) return s;
            }
            catch
            {
                // Not a string column
            }
        }
        return string.Empty;
    }

    private static string Text(Lumina.Text.ReadOnly.ReadOnlySeString s) => s.ExtractText().Trim();
}
