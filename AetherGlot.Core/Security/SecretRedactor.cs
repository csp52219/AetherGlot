namespace AetherGlot.Core.Security;

/// <summary>Masks secrets in strings that go to logs, exceptions or the inspector.</summary>
public static class SecretRedactor
{
    public const string Mask = "••••";

    /// <summary>Replaces every occurrence of any secret (length ≥ 4) in <paramref name="text"/>.</summary>
    public static string Redact(string? text, IEnumerable<string?> secrets)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        var result = text;
        // Longest first so a secret that contains another is fully masked
        foreach (var s in secrets.Where(s => !string.IsNullOrEmpty(s) && s!.Length >= 4)
                                 .Distinct()
                                 .OrderByDescending(s => s!.Length))
        {
            result = result.Replace(s!, Mask, StringComparison.Ordinal);
        }
        return result;
    }

    /// <summary>
    /// Display form for a stored secret: "sk-••••a12f" style (prefix up to 3 chars + last 4),
    /// or just the mask when the secret is short.
    /// </summary>
    public static string MaskForDisplay(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        if (secret.Length < 12) return Mask;
        var prefix = secret.StartsWith("sk-", StringComparison.Ordinal) ? "sk-" : string.Empty;
        return $"{prefix}{Mask}{secret[^4..]}";
    }
}
