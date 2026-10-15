namespace AetherGlot.Helpers;

/// <summary>
/// Text for dalamud.log and the "copy diagnostics" button. Contains no secrets: keys are never
/// read here, endpoints are reduced to their host, and the result is redacted once more.
/// </summary>
internal static class DiagnosticsSummary
{
    public static string StartupLine(Plugin p)
    {
        var c    = p.Configuration;
        var conn = c.Connection;
        var mode = conn.Mode == ConnectionMode.Direct ? $"Direct/{conn.DirectProvider}" : conn.Mode.ToString();
        return $"AetherGlot {Version()} loaded · client {Plugin.ClientState.ClientLanguage} · UI {Plugin.PluginInterface.UiLanguage}" +
               $" · connection {mode} ({Host(p.BuildConnectionSettings().BaseUrl)}) · CF Access {(conn.CloudflareAccessEnabled ? "on" : "off")}" +
               $" · inbound {Model(conn.InboundModel)} / outbound {Model(conn.OutboundModel)}" +
               $" · receive {(c.InboundEnabled ? "on" : "off")} · /agsend {(c.OutboundEnabled ? "on" : "off")}" +
               $" · channels {string.Join(",", c.EnabledChannels)} · display {c.Display.Preset}, game chat → {c.Display.GameChat.Output}" +
               $" · verbose log {(c.Diagnostics.VerboseLogging ? "on" : "off")}";
    }

    /// <summary>One line per test step ("OK Cloudflare Access: 200"), plus hints for failures.</summary>
    public static IEnumerable<string> TestLines(DiagnosticReport report, ConnectionSettings settings)
    {
        foreach (var step in report.Steps)
        {
            var mark = step.Status switch
            {
                DiagnosticStatus.Ok      => "OK",
                DiagnosticStatus.Warning => "WARN",
                DiagnosticStatus.Failed  => "FAIL",
                _                        => "SKIP",
            };
            yield return $"{mark} {Plugin.StepLabel(step.Kind)}: {SecretRedactor.Redact(step.Detail, settings.SecretValues())}";
            if (step.Hint is not null && step.Status == DiagnosticStatus.Failed)
                yield return "   → " + step.Hint;
        }
    }

    /// <summary>Everything useful for a bug report, ready to paste.</summary>
    public static string Build(Plugin p)
    {
        var c        = p.Configuration;
        var settings = p.BuildConnectionSettings();
        var sb       = new StringBuilder();

        sb.AppendLine("=== AetherGlot diagnostics ===");
        sb.AppendLine($"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine(StartupLine(p));
        sb.AppendLine($"Settings version {c.Version}; JSON mode {(c.Connection.UseJsonResponseFormat ? "on" : "off")}; timeout {c.Connection.RequestTimeoutSeconds}s; " +
                      $"extra headers {c.Connection.ExtraHeaderNames.Count}; user tag {(string.IsNullOrWhiteSpace(c.Connection.UserTag) ? "none" : "set")}");
        sb.AppendLine($"Translation: unresolved {c.Translation.UnresolvedMode}, discard refusals {c.Translation.DiscardRefusals}, " +
                      $"context hints {c.Translation.SendContextHints}, cache {c.Translation.CacheSize}");
        sb.AppendLine("Display routes: " + string.Join("; ", c.Display.Routes.OrderBy(r => r.Key)
            .Select(r => $"{r.Key}={r.Value.Destinations}/{r.Value.InCombat}")));

        var stats = p.Glossary.Service.Stats;
        sb.AppendLine($"Dictionaries: mine {stats.UserCount}, game {stats.GameDataCount}, community {stats.CommunityCount} " +
                      $"(v{stats.CommunityVersion ?? "-"}, {p.Glossary.Community?.Origin}); pending {p.Glossary.Pending.Items.Count}");

        var log = p.TranslationLog;
        sb.AppendLine($"Translation log: {log.Count} entries; last call {(log.LastCallOk switch { true => "OK", false => "failed", null => "none" })}" +
                      (log.AverageLatency is { } avg ? $", avg {avg.TotalSeconds:0.0}s" : string.Empty));
        foreach (var n in log.NotLogged)
            sb.AppendLine($"  not in log: {n.Group} ×{n.Count} → {n.Destinations}");
        if (log.LastError is { } lastError)
            sb.AppendLine("Last error: " + lastError);

        sb.AppendLine("--- Last connection test ---");
        if (p.LastConnectionReport is { } report)
            foreach (var line in TestLines(report, settings)) sb.AppendLine(line);
        else
            sb.AppendLine("(not run in this session)");

        sb.AppendLine("--- Recent warnings / errors ---");
        var problems = p.Diag.RecentProblems;
        if (problems.Count == 0) sb.AppendLine("(none)");
        foreach (var line in problems) sb.AppendLine(line);

        return SecretRedactor.Redact(sb.ToString(), settings.SecretValues());
    }

    private static string Version() => typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "?";

    private static string Model(string m) => string.IsNullOrWhiteSpace(m) ? "(none)" : m;

    private static string Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : string.IsNullOrWhiteSpace(url) ? "(no URL)" : "(invalid URL)";
}
