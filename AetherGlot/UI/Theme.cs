using System.Numerics;

namespace AetherGlot.UI;

/// <summary>Colors taken from the UI mock (dark theme).</summary>
internal static class Theme
{
    public static Vector4 Hex(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static readonly Vector4 Text        = Hex(0xE7E7EA);
    public static readonly Vector4 TextDim     = Hex(0xA3A7AF);
    public static readonly Vector4 TextMuted   = Hex(0x868B94);
    public static readonly Vector4 Accent      = Hex(0x6B9BFF);
    public static readonly Vector4 AccentText  = Hex(0x0E0F12);
    public static readonly Vector4 Ok          = Hex(0x5FC98A);
    public static readonly Vector4 Warn        = Hex(0xE58A5B);
    public static readonly Vector4 Error       = Hex(0xF25A5A);

    /// <summary>Auto-translate phrases (green, shown with 【】).</summary>
    public static readonly Vector4 AutoTranslate = Hex(0x8FD694);

    /// <summary>Unresolved words (orange, dashed underline).</summary>
    public static readonly Vector4 Unresolved  = Hex(0xF2B08E);

    /// <summary>Term chips.</summary>
    public static readonly Vector4 TermChip     = Hex(0xF0CF94);
    public static readonly Vector4 TermChipBg   = Hex(0xE3B05C, 0.12f);
    public static readonly Vector4 ChipBg       = Hex(0x2A2C32);
    public static readonly Vector4 Confidence   = Hex(0xC9A86E);

    public static readonly Vector4 MentionBg    = Hex(0x6B9BFF, 0.10f);
}
