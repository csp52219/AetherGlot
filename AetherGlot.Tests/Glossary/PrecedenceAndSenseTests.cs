using AetherGlot.Core.Glossary;
using Xunit;

namespace AetherGlot.Tests.Glossary;

public sealed class PrecedenceAndSenseTests
{
    [Fact]
    public void UserDictionary_OverridesEverything()
    {
        var user = TermEntry.Simple("my-stack", TermKind.Mechanic, TermSource.User, "stack", "集合");
        var lookup = GlossaryTestData.Service([user]).Lookup("stack", "en", "ja");
        Assert.Equal("集合", Assert.Single(lookup.Matches).Resolution.Chosen!.Ja);
    }

    [Fact]
    public void GameData_OverridesSingleSenseCommunityEntry()
    {
        var game      = TermEntry.Simple("g", TermKind.Action, TermSource.GameData, "Medica", "メディカ");
        var community = TermEntry.Simple("c", TermKind.Slang, TermSource.Community, "Medica", "回復魔法");
        Assert.True(GlossaryIndex.Wins(game, community));
        Assert.False(GlossaryIndex.Wins(community, game));
    }

    [Fact]
    public void PolysemousCommunityEntry_OverridesGameData()
    {
        // Game data has "Bio" (action) only; the community entry adds the afk meaning
        var lookup = GlossaryTestData.Service().Lookup("bio", "en", "ja");
        var entry = Assert.Single(lookup.Matches).Resolution.Entry;
        Assert.Equal(TermSource.Community, entry.Source);
        Assert.True(entry.IsPolysemous);
    }

    [Fact]
    public void BrbBio_OutOfCombat_MeansBathroom()
    {
        var m = Assert.Single(GlossaryTestData.Service().Lookup("brb bio, 2 min", "en", "ja", inCombat: false).Matches, x => x.Hit.Surface == "bio");
        Assert.Equal("トイレ（離席）", m.Resolution.Chosen!.Ja);
    }

    [Fact]
    public void BioFellOff_InCombat_WithScholar_MeansTheAction()
    {
        var m = Assert.Single(
            GlossaryTestData.Service().Lookup("sch your bio fell off on boss, reapply", "en", "ja", inCombat: true, partyJobs: ["MNK", "SCH"]).Matches,
            x => x.Hit.Surface == "bio");
        Assert.Equal("バイオ（技）", m.Resolution.Chosen!.Ja);
    }

    [Fact]
    public void Bio_WithoutContext_IsAmbiguous_AndBothSensesGoToTheModel()
    {
        var lookup = GlossaryTestData.Service().Lookup("bio", "en", "ja");
        var m = Assert.Single(lookup.Matches);
        Assert.True(m.Resolution.IsAmbiguous);

        var hint = Assert.Single(lookup.ToHints());
        Assert.Equal(["バイオ（技）", "トイレ（離席）"], hint.Targets);
    }

    [Fact]
    public void Hints_ContainOnlyMatchedTerms()
    {
        var hints = GlossaryTestData.Service().Lookup("stack then spread", "en", "ja").ToHints();
        Assert.Equal(2, hints.Count);
        Assert.Equal("頭割り", hints[0].Targets.Single());
        Assert.Equal("mechanic", hints[0].Note);
    }

    [Fact]
    public void FixedTerms_AreOnlyOfficialGameNames()
    {
        var fixedTerms = GlossaryTestData.Service().FixedTerms("次のギミック頭割りだから集まって。メディカは私が入れます。学者さんよろしく", "ja", "en");
        Assert.Contains(fixedTerms, h => h.Source == "メディカ" && h.Targets.Single() == "Medica");
        Assert.Contains(fixedTerms, h => h.Source == "学者" && h.Targets.Single() == "Scholar");
        Assert.DoesNotContain(fixedTerms, h => h.Source == "頭割り"); // community term, not fixed
    }

    [Fact]
    public void SenseScore_PenalisesContradictingCombatState()
    {
        var ctx = SenseContext.Create("bio", ["bio"], inCombat: true, partyJobs: null);
        Assert.Equal(-2, SenseResolver.Score(new SenseRules { InCombat = false }, ctx));
        Assert.Equal(1, SenseResolver.Score(new SenseRules { InCombat = true }, ctx));
        Assert.Equal(0, SenseResolver.Score(new SenseRules { InCombat = true }, ctx with { InCombat = null }));
    }
}
