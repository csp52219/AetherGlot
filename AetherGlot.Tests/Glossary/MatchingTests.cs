using AetherGlot.Core.Glossary;
using Xunit;

namespace AetherGlot.Tests.Glossary;

public sealed class MatchingTests
{
    [Theory]
    [InlineData("opo")]
    [InlineData("Opo-opo")]
    [InlineData("opo opo")]
    [InlineData("opoopo")]
    [InlineData("ＯＰＯ")]
    public void Normalizer_UnifiesSpellings(string input)
        => Assert.Equal("opo", TermNormalizer.Normalize(input));

    [Theory]
    [InlineData("stun (status)", "stun")]
    [InlineData("【Hello!】", "Hello")]
    [InlineData("Thank you.", "Thank you")]
    public void Normalizer_StripsDecorations(string input, string expected)
        => Assert.Equal(expected, TermNormalizer.StripDecorations(input));

    [Fact]
    public void Monk_Forms_AreFound_InAnySpelling()
    {
        var lookup = GlossaryTestData.Service().Lookup("opo > rapu > kua-ru, keep ur form uptime pls", "en", "ja");

        var hits = lookup.Matches.Select(m => (m.Hit.Surface, m.Resolution.Chosen!.Ja)).ToList();
        Assert.Contains(("opo", "壱の型：魔猿"), hits);
        Assert.Contains(("rapu", "弐の型：走竜"), hits);
        Assert.Contains(("kua-ru", "参の型：猛虎"), hits);
        Assert.Contains(("uptime", "維持率（切らさないこと）"), hits);
    }

    [Fact]
    public void LongestMatch_Wins_AndHitsDoNotOverlap()
    {
        var lookup = GlossaryTestData.Service().Lookup("tank buster then stack", "en", "ja");
        Assert.Equal(["tank buster", "stack"], lookup.Matches.Select(m => m.Hit.Surface));
    }

    [Fact]
    public void ShortAliases_OnlyMatchWholeWords()
    {
        var lookup = GlossaryTestData.Service().Lookup("tbh the mitts are ok", "en", "ja");
        Assert.DoesNotContain(lookup.Matches, m => m.Resolution.Entry.Id is "tankbuster" or "mitigation");
    }

    [Fact]
    public void Placeholders_AreNotMatched_AndPositionsAreKept()
    {
        const string text = "{{at0}} stack {{at1}}";
        var hit = Assert.Single(GlossaryTestData.Service().Lookup(text, "en", "ja").Matches).Hit;
        Assert.Equal("stack", text.Substring(hit.Start, hit.Length));
    }

    [Fact]
    public void Plural_FallsBackToSingular()
    {
        var lookup = GlossaryTestData.Service().Lookup("watch the stacks", "en", "ja");
        Assert.Equal("頭割り", Assert.Single(lookup.Matches).Resolution.Chosen!.Ja);
    }

    [Fact]
    public void GameData_CommonWords_AreNotMatched()
    {
        var lookup = GlossaryTestData.Service().Lookup("we return after this", "en", "ja");
        Assert.Empty(lookup.Matches);
    }

    [Fact]
    public void JobAbbreviation_IsMatched()
    {
        var lookup = GlossaryTestData.Service().Lookup("sch pls shield", "en", "ja");
        Assert.Equal("学者", Assert.Single(lookup.Matches).Resolution.Chosen!.Ja);
    }

    [Fact]
    public void Japanese_IsMatched_WithOriginalPositions()
    {
        const string text = "次は魔猿から、メディカは私が入れます";
        var lookup = GlossaryTestData.Service().Lookup(text, "ja", "en");

        var hits = lookup.Matches.ToDictionary(m => m.Hit.Surface, m => m.Resolution.Chosen!.En);
        Assert.Equal("Opo-opo Form", hits["魔猿"]);
        Assert.Equal("Medica", hits["メディカ"]);
        Assert.All(lookup.Matches, m => Assert.Equal(m.Hit.Surface, text.Substring(m.Hit.Start, m.Hit.Length)));
    }

    [Fact]
    public void Japanese_FullName_WithColon_IsMatched()
    {
        var lookup = GlossaryTestData.Service().Lookup("壱の型:魔猿を維持", "ja", "en");
        Assert.Equal("Opo-opo Form", Assert.Single(lookup.Matches).Resolution.Chosen!.En);
    }

    [Fact]
    public void UnsupportedLanguagePair_ReturnsNothing()
        => Assert.Empty(GlossaryTestData.Service().Lookup("stack", "de", "ja").Matches);

    [Fact]
    public void MaxHits_IsRespected()
    {
        var lookup = GlossaryTestData.Service().Lookup("stack spread tb kb lb mit", "en", "ja", maxHits: 3);
        Assert.Equal(3, lookup.Matches.Count);
    }
}
