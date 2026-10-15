using System.Text.Json;

namespace AetherGlot.Core.Translation;

/// <summary>
/// Extracts and parses the JSON object returned by the model, tolerating code fences and
/// leading/trailing prose, and checks it against the schema.
/// </summary>
public static class ResponseParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    };

    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.Ordinal) { InboundResponse.StatusOk, InboundResponse.StatusPartial, InboundResponse.StatusSkip };

    public static bool TryParseInbound(string? raw, out InboundResponse? response, out string? error)
    {
        response = null;
        if (!TryDeserialize(raw, out InboundResponse? parsed, out error))
            return false;

        var r = parsed!;
        r.Status = (r.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (!ValidStatuses.Contains(r.Status))
        {
            error = $"Schema: invalid status '{Truncate(r.Status)}'.";
            return false;
        }
        if (r.Translation is null)
        {
            error = "Schema: 'translation' is missing.";
            return false;
        }
        if (r.Status != InboundResponse.StatusSkip && string.IsNullOrWhiteSpace(r.Translation))
        {
            error = "Schema: 'translation' is empty.";
            return false;
        }
        r.Lang ??= string.Empty;
        r.Terms ??= [];
        r.Unresolved ??= [];
        r.Senses ??= [];

        if (r.Terms.Any(t => t is null || string.IsNullOrWhiteSpace(t.Src)))
        {
            error = "Schema: a term has no 'src'.";
            return false;
        }
        foreach (var t in r.Terms) t.Alts ??= [];

        if (r.Unresolved.Any(u => u is null || !IsUnresolvedId(u.Id) || u.Text is null))
        {
            error = "Schema: an unresolved entry has an invalid 'id' or no 'text'.";
            return false;
        }

        response = r;
        return true;
    }

    public static bool TryParseOutbound(string? raw, out OutboundResponse? response, out string? error)
    {
        response = null;
        if (!TryDeserialize(raw, out OutboundResponse? parsed, out error))
            return false;

        if (string.IsNullOrWhiteSpace(parsed!.Translation))
        {
            error = "Schema: 'translation' is missing or empty.";
            return false;
        }
        parsed.Alternatives = (parsed.Alternatives ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        parsed.Terms ??= [];
        response = parsed;
        return true;
    }

    /// <summary>
    /// Returns the substring from the first '{' to its matching '}' (string-aware), after
    /// removing markdown code fences. Null when no complete object is present.
    /// </summary>
    public static string? ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var start = raw.IndexOf('{');
        if (start < 0) return null;

        var depth    = 0;
        var inString = false;
        var escaped  = false;
        for (var i = start; i < raw.Length; i++)
        {
            var c = raw[i];
            if (inString)
            {
                if (escaped)        escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"')  inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return raw[start..(i + 1)];
                    break;
            }
        }
        return null;
    }

    private static bool TryDeserialize<T>(string? raw, out T? value, out string? error) where T : class
    {
        value = null;
        var json = ExtractJsonObject(raw);
        if (json is null)
        {
            error = "Parse: no JSON object found in the response.";
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException ex)
        {
            // JsonException messages contain paths/positions only, not the content
            error = $"Parse: {ex.Message}";
            return false;
        }

        if (value is null)
        {
            error = "Parse: the JSON object was null.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsUnresolvedId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length > 1 && id[0] == 'u' && id[1..].All(char.IsAsciiDigit);

    private static string Truncate(string s) => s.Length > 20 ? s[..20] + "…" : s;
}
