namespace AetherGlot.Core.Translation;

/// <summary>
/// The outbound "style instructions" used to hold a full prompt (v1). The tone is now chosen in
/// the send preview, so the default is empty; the old default is recognised for migration.
/// </summary>
public static class OutboundPromptDefaults
{
    /// <summary>Default style instructions (none: the tone buttons decide).</summary>
    public const string Default = "";

    /// <summary>The v1 default prompt, which forced casual style and conflicts with the tone buttons.</summary>
    public const string LegacyDefault =
        "You are a player in an MMORPG. Translate the user's input into natural English.\n" +
        "- Keep it very casual. Avoid formal grammar or structures.\n" +
        "- Actively use gaming slangs: 'i' instead of 'I', 'btw', 'ty', 'gg', 'brb', 'thx', 'omw', etc.\n" +
        "- Avoid capitalizing words unless necessary. Do not end sentences with periods.";

    /// <summary>
    /// Value to keep after migration: the old default (unchanged by the user, whatever its line
    /// endings or surrounding whitespace) becomes empty; a prompt the user edited is kept as is.
    /// </summary>
    public static string Migrate(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return Default;
        return Normalize(prompt) == Normalize(LegacyDefault) ? Default : prompt;
    }

    private static string Normalize(string s) =>
        string.Join("\n", s.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim())).Trim();
}
