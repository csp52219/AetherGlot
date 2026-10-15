namespace AetherGlot.Helpers;

/// <summary>Diagnostics switches (Configuration.json).</summary>
[Serializable]
public sealed class DiagnosticsSettings
{
    /// <summary>Write the per-message trace at Information level (visible in dalamud.log by default).</summary>
    public bool VerboseLogging { get; set; }

    /// <summary>Also write message text and sender names (only with verbose logging). Off by default: it is other players' chat.</summary>
    public bool LogMessageText { get; set; }
}

/// <summary>
/// Logging for AetherGlot. Startup, sends, saves, warnings and errors always reach dalamud.log;
/// the per-message trace is Debug unless verbose logging is on. Chat text and names are written
/// only when the user opts in. Never pass secrets: callers pass already redacted text.
/// The last warnings / errors are kept for "copy diagnostics".
/// </summary>
internal sealed class DiagnosticLog
{
    private const int RecentCapacity = 30;

    private readonly IPluginLog log;
    private readonly Func<DiagnosticsSettings> settings;
    private readonly Queue<string> recent = new();
    private readonly object gate = new();

    public DiagnosticLog(IPluginLog log, Func<DiagnosticsSettings> settings)
    {
        this.log      = log;
        this.settings = settings;
    }

    public IPluginLog Raw => log;

    public void Info(string message) => log.Information("{Message}", message);

    /// <summary>Per-message detail: Information when verbose logging is on, Debug otherwise.</summary>
    public void Trace(string message)
    {
        if (settings().VerboseLogging) log.Information("{Message}", message);
        else log.Debug("{Message}", message);
    }

    public void Warning(string message)
    {
        Remember("WARN", message);
        log.Warning("{Message}", message);
    }

    public void Error(string message)
    {
        Remember("ERROR", message);
        log.Error("{Message}", message);
    }

    /// <summary>Chat text as it may appear in the log: quoted when allowed, otherwise only its length.</summary>
    public string Text(string? text)
    {
        var s = settings();
        if (s.VerboseLogging && s.LogMessageText) return "\"" + (text ?? string.Empty) + "\"";
        return $"({(text ?? string.Empty).Length} chars)";
    }

    /// <summary>A player name as it may appear in the log: in full when allowed, otherwise initials.</summary>
    public string Who(string? name)
    {
        var s = settings();
        if (string.IsNullOrWhiteSpace(name)) return "?";
        if (s.VerboseLogging && s.LogMessageText) return name;
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p[0] + "."));
    }

    public IReadOnlyList<string> RecentProblems
    {
        get { lock (gate) return recent.ToList(); }
    }

    private void Remember(string level, string message)
    {
        lock (gate)
        {
            recent.Enqueue($"{DateTimeOffset.Now:HH:mm:ss} {level} {message}");
            while (recent.Count > RecentCapacity) recent.Dequeue();
        }
    }
}
