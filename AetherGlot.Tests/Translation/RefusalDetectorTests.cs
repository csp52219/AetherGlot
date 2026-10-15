using AetherGlot.Core.Translation;
using Xunit;

namespace AetherGlot.Tests.Translation;

public sealed class RefusalDetectorTests
{
    [Theory]
    [InlineData("申し訳ありませんが、この文章は翻訳できません。", "zorp blorp")]
    [InlineData("この入力は認識できません。", "asdfgh")]
    [InlineData("I'm sorry, but I can't translate this message.", "ちょっとまって")]
    [InlineData("As an AI language model, I cannot help with that.", "test")]
    [InlineData("Je ne peux pas traduire ce message.", "hello")]
    [InlineData("申し訳ございません。", "brb")]
    [InlineData("意味がわかりません", "lol")]
    public void Refusals_AreDetected(string translation, string source)
        => Assert.True(RefusalDetector.IsRefusal(translation, source));

    [Theory]
    [InlineData("ごめん、遅れた", "sorry im late")]
    [InlineData("すみません、もう一回お願いします", "sry can u repeat")]
    [InlineData("I'm sorry, my bad", "ごめん、私のミス")]
    [InlineData("ギミックが理解できません", "i don't understand this mechanic")]
    [InlineData("意味がわかりません、何それ？", "what does that mean?")]
    [InlineData("ちょっとトイレ行ってきます、2分で戻ります", "brb bio, 2 min")]
    [InlineData("魔猿 → 走竜 → 猛虎の順で回して", "opo > rapu > kua-ru")]
    public void NormalTranslations_AreNotRefusals(string translation, string source)
        => Assert.False(RefusalDetector.IsRefusal(translation, source));

    [Fact]
    public void ShortMarker_MustBeWholeWord()
    {
        // "mb" inside "member" is not an apology, so the apology in the translation is a refusal
        Assert.True(RefusalDetector.IsRefusal("I'm sorry, I cannot do that", "member list"));
    }

    [Fact]
    public void LengthRatio_FlagsCommentary()
    {
        var src = "gg";
        var tr  = "お疲れさまでした。なお、「gg」は「good game」の略で、ゲーム終了時に相手を称える挨拶として広く使われている表現です。";
        Assert.True(RefusalDetector.IsSuspiciouslyLong(tr, src));
        Assert.False(RefusalDetector.IsSuspiciouslyLong("お疲れさま", src));
    }

    [Fact]
    public void LengthRatio_IgnoresPlaceholders()
    {
        Assert.False(RefusalDetector.IsSuspiciouslyLong("{{at0}}{{at1}}{{at2}}{{at3}}{{at4}}{{at5}} よろしく", "{{at0}}{{at1}}{{at2}}{{at3}}{{at4}}{{at5}} ty"));
    }
}
