using AetherGlot.Core.Glossary;

namespace AetherGlot.Tests.Glossary;

internal static class GlossaryTestData
{
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "community-glossary.json");

    public static IReadOnlyList<TermEntry> Community() =>
        GlossaryFile.Parse(File.ReadAllText(BundledPath), TermSource.Community).Entries;

    public static List<TermEntry> GameData() =>
    [
        TermEntry.Simple("job:28", TermKind.Job, TermSource.GameData, "Scholar", "学者", "SCH"),
        TermEntry.Simple("job:20", TermKind.Job, TermSource.GameData, "Monk", "モンク", "MNK"),
        TermEntry.Simple("action:17864", TermKind.Action, TermSource.GameData, "Bio", "バイオ"),
        TermEntry.Simple("action:124", TermKind.Action, TermSource.GameData, "Medica", "メディカ"),
        TermEntry.Simple("action:7", TermKind.Action, TermSource.GameData, "Return", "デジョン"), // stop word
        TermEntry.Simple("status:1", TermKind.Status, TermSource.GameData, "Opo-opo Form", "壱の型：魔猿"),
        TermEntry.Simple("content:1", TermKind.Content, TermSource.GameData, "the Unending Coil of Bahamut (Ultimate)", "絶バハムート討滅戦"),
    ];

    public static GlossaryService Service(IEnumerable<TermEntry>? user = null)
    {
        var s = new GlossaryService();
        s.Rebuild(user ?? [], GameData(), Community(), GlossaryStats.Empty);
        return s;
    }
}
