using AetherGlot.Core.Glossary;
using Xunit;

namespace AetherGlot.Tests.Glossary;

public sealed class GlossaryFileTests
{
    [Fact]
    public void BundledCommunityDictionary_IsValid()
    {
        var parsed = GlossaryFile.Parse(File.ReadAllText(GlossaryTestData.BundledPath), TermSource.Community);
        Assert.True(parsed.Ok, string.Join("; ", parsed.Errors));
        Assert.True(parsed.Entries.Count >= 40);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Version));
        Assert.All(parsed.Entries, e => Assert.Equal(TermSource.Community, e.Source));
    }

    [Fact]
    public void SenseRules_AreRead()
    {
        var bio = GlossaryTestData.Community().Single(e => e.Id == "bio");
        Assert.Equal(2, bio.Senses.Count);
        Assert.True(bio.Senses[0].Rules.InCombat);
        Assert.Contains("fell off", bio.Senses[0].Rules.Words);
        Assert.Contains("SCH", bio.Senses[0].Rules.PartyJobs);
        Assert.Contains("bio", bio.AliasesEn); // top-level "en" of a multi-sense entry is a spelling
    }

    [Fact]
    public void InvalidEntries_AreSkipped_AndReported()
    {
        const string json = """
        { "schema": 1, "entries": [
            { "id": "ok", "en": "stack", "ja": "頭割り" },
            { "id": "bad" },
            { "id": "ok", "en": "dup", "ja": "重複" },
            { "id": "badsense", "senses": [ { "id": "x" } ] }
        ] }
        """;
        var parsed = GlossaryFile.Parse(json, TermSource.User);
        Assert.Single(parsed.Entries);
        Assert.Equal(3, parsed.Errors.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "schema": 99, "entries": [] }""")]
    public void BrokenOrNewerFile_ReportsError(string json)
    {
        var parsed = GlossaryFile.Parse(json, TermSource.Community);
        Assert.Empty(parsed.Entries);
        Assert.NotEmpty(parsed.Errors);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var original = GlossaryTestData.Community();
        var parsed   = GlossaryFile.Parse(GlossaryFile.Serialize(original), TermSource.Community);

        Assert.True(parsed.Ok);
        Assert.Equal(original.Count, parsed.Entries.Count);
        var bio = parsed.Entries.Single(e => e.Id == "bio");
        Assert.Equal(AmbiguityBehavior.ShowAll, bio.OnAmbiguous);
        Assert.Equal(["brb", "afk", "sec", "min", "mins", "back", "bbl"], bio.Senses[1].Rules.Words);
    }

    [Fact]
    public void UserStore_SavesAndReloads()
    {
        var dir  = Directory.CreateTempSubdirectory("ag-user-");
        var path = Path.Combine(dir.FullName, "mydict.json");
        var store = new UserGlossaryStore(path);
        store.Load();
        store.Upsert(TermEntry.Simple("zorp", TermKind.Slang, TermSource.Community, "zorp strat", "ゾープ作戦"));

        var reloaded = new UserGlossaryStore(path);
        reloaded.Load();
        var e = Assert.Single(reloaded.Entries);
        Assert.Equal(TermSource.User, e.Source);
        Assert.Equal("ゾープ作戦", e.Ja);

        Assert.True(reloaded.Remove("zorp"));
        Assert.Empty(reloaded.Entries);
        dir.Delete(true);
    }

    [Fact]
    public void UnresolvedCandidates_AreCounted_AndPersisted()
    {
        var dir  = Directory.CreateTempSubdirectory("ag-pending-");
        var path = Path.Combine(dir.FullName, "pending.json");
        var store = new UnresolvedCandidateStore(path, capacity: 2);
        var t0 = DateTimeOffset.UtcNow;

        store.Record([("zorp strat", null)], "next one we do the zorp strat", t0);
        store.Record([("Zorp-strat", "作戦名?")], null, t0.AddMinutes(1));
        store.Record([("x", null)], null, t0); // too short, ignored
        store.Save();

        var reloaded = new UnresolvedCandidateStore(path);
        reloaded.Load();
        var c = Assert.Single(reloaded.Items);
        Assert.Equal(2, c.Count);
        Assert.Equal("作戦名?", c.Guess);
        dir.Delete(true);
    }
}
