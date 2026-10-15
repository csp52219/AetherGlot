using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace AetherGlot.Tests.Localization;

/// <summary>Keeps loc/{en,ja,de,fr}.json complete and in sync with the code.</summary>
public sealed partial class LocFilesTests
{
    private static readonly string[] Languages = ["en", "ja", "de", "fr"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AetherGlot.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static Dictionary<string, string> Load(string lang)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "AetherGlot", "loc", lang + ".json")));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetProperty("Message").GetString() ?? string.Empty);
    }

    private static List<string> Placeholders(string s) =>
        PlaceholderRegex().Matches(s).Select(m => m.Value).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void AllLanguages_HaveTheSameKeys_AndPlaceholders()
    {
        var en = Load("en");
        foreach (var lang in Languages.Skip(1))
        {
            var t = Load(lang);
            Assert.Empty(en.Keys.Except(t.Keys));
            Assert.Empty(t.Keys.Except(en.Keys));
            foreach (var (key, text) in en)
            {
                Assert.False(string.IsNullOrWhiteSpace(t[key]), $"{lang}:{key} is empty");
                Assert.True(Placeholders(text).SequenceEqual(Placeholders(t[key])), $"{lang}:{key} placeholders differ");
            }
        }
    }

    [Fact]
    public void EveryLiteralKeyInCode_IsInEnJson_WithTheSameFallback()
    {
        var en = Load("en");
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "AetherGlot"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var code = File.ReadAllText(file);
            foreach (Match m in LocalizeRegex().Matches(code))
            {
                var key = m.Groups["key"].Value;
                var fallback = string.Concat(StringRegex().Matches(m.Groups["text"].Value).Select(s => JsonSerializer.Deserialize<string>(s.Value)));
                if (!en.TryGetValue(key, out var text)) problems.Add($"missing {key}");
                else if (text != fallback) problems.Add($"different fallback for {key}");
            }
        }
        Assert.Empty(problems);
    }

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"Loc\.Localize\(\s*""(?<key>\w+)""\s*,\s*(?<text>(?:""(?:[^""\\]|\\.)*""\s*\+?\s*)+)\)")]
    private static partial Regex LocalizeRegex();

    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""")]
    private static partial Regex StringRegex();
}
