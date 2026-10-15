using AetherGlot.Core.Glossary;
using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Glossary;

public sealed class AutoTranslateTests
{
    [Fact]
    public void LookupTable_ParsesAllForms()
    {
        Assert.True(LookupTable.Parse("@")!.InCompletion);

        var race = LookupTable.Parse("Race")!;
        Assert.Equal("Race", race.Sheet);
        Assert.True(race.Contains(12345));

        var weather = LookupTable.Parse("Weather[col-1,1-20,22-23,25]")!;
        Assert.Equal("Weather", weather.Sheet);
        Assert.Equal([1], weather.Columns);
        Assert.True(weather.Contains(22));
        Assert.True(weather.Contains(25));
        Assert.False(weather.Contains(21));

        var companion = LookupTable.Parse("Companion[noun,1-67, 587-591]")!;
        Assert.Empty(companion.Columns);
        Assert.True(companion.Contains(590));

        Assert.Null(LookupTable.Parse(""));
        Assert.Null(LookupTable.Parse("Bad Sheet!"));
    }

    [Fact]
    public void Catalog_ExposesShortPhrasesAsTerms_NotSentences()
    {
        var catalog = new AutoTranslateCatalog(
        [
            new AutoTranslateEntry(26, 3303, "状態変化", "Statuses", "スタン", "stun (status)"),
            new AutoTranslateEntry(2, 203, "あいさつ", "Greetings", "こんにちは。", "Hello!"),
            new AutoTranslateEntry(8, 801, "戦術", "Tactics", "敵視に注意してください。", "Please watch your aggro, everyone."),
            new AutoTranslateEntry(56, 17864, "アクション", "Actions", "バイオ", "Bio"),
        ], []);

        var terms = catalog.ToTermEntries().ToList();
        Assert.Contains(terms, t => t.En == "stun" && t.Ja == "スタン" && t.AutoTranslateId == "26:3303");
        Assert.Contains(terms, t => t.En == "Bio" && t.Kind == TermKind.AutoTranslate);
        Assert.DoesNotContain(terms, t => t.AutoTranslateId == "8:801");
        Assert.True(catalog.TryGet(56, 17864, out var bio));
        Assert.Equal("56:17864", bio.Id);
    }

    [Fact]
    public void Placeholderizer_ReplacesAutoTranslate_AndKeepsWordsApart()
    {
        var msg = Placeholderizer.Build(
        [
            new AutoTranslatePart(2, 203, "こんにちは！"),
            new TextPart("opo uptime pls"),
            new AutoTranslatePart(50, 20, "【モンク】"),
            new TextPart("!"),
        ]);

        Assert.Equal("{{at0}} opo uptime pls {{at1}} !", msg.Text);
        Assert.Equal("【こんにちは！】", msg.AutoTranslateTexts["at0"]);
        Assert.Equal("【モンク】", msg.AutoTranslateTexts["at1"]);
        Assert.Equal("【こんにちは！】 opo uptime pls 【モンク】 !", msg.DisplayOriginal);
        Assert.DoesNotContain("{{", msg.TypedText);
        Assert.Equal(2, msg.AutoTranslateParts.Count);
    }

    [Fact]
    public void Placeholderizer_TextOnly_IsUnchanged()
    {
        var msg = Placeholderizer.Build([new TextPart("brb bio, 2 min")]);
        Assert.Equal("brb bio, 2 min", msg.Text);
        Assert.Empty(msg.AutoTranslateTexts);
    }
}
