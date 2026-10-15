using AetherGlot.Core.Display;
using AetherGlot.Core.Translation;

namespace AetherGlot.Core.Log;

public enum LogEntryState
{
    Translating,
    Done,
}

/// <summary>One received message in the translation log. Outcome is set from a background thread.</summary>
public sealed class LogEntry
{
    private volatile InboundOutcome? outcome;
    private volatile bool done;

    public required long Id { get; init; }
    public required DateTimeOffset Time { get; init; }
    public required ChannelGroup Group { get; init; }

    /// <summary>Raw channel value (XivChatType) for the plugin's labels and colors.</summary>
    public required int Channel { get; init; }
    public required string Sender { get; init; }
    public string? World { get; init; }
    public required PlaceholderizedMessage Message { get; init; }
    public bool MentionsMe { get; init; }

    public InboundOutcome? Outcome => outcome;
    public LogEntryState State => done ? LogEntryState.Done : LogEntryState.Translating;

    /// <summary>Set when translation finished (or failed without an outcome).</summary>
    public void Complete(InboundOutcome? result)
    {
        outcome = result;
        done    = true;
    }
}

/// <summary>
/// Bounded, thread-safe list of recent messages for the log window, plus a rolling average of
/// response times and the last connection state for the footer.
/// </summary>
public sealed class TranslationLog
{
    private const int LatencyWindow = 20;

    private readonly object gate = new();
    private readonly LinkedList<LogEntry> entries = new();
    private readonly Queue<double> latencies = new();
    private long nextId;
    private int capacity;

    public TranslationLog(int capacity) => this.capacity = Math.Max(10, capacity);

    /// <summary>Incremented on every add / completion; the UI compares it to detect changes.</summary>
    public int Version { get; private set; }

    public int Capacity
    {
        get { lock (gate) return capacity; }
        set { lock (gate) { capacity = Math.Max(10, value); Trim(); } }
    }

    /// <summary>Average response time of recent model calls (cache hits excluded).</summary>
    public TimeSpan? AverageLatency
    {
        get
        {
            lock (gate) return latencies.Count == 0 ? null : TimeSpan.FromMilliseconds(latencies.Average());
        }
    }

    /// <summary>true = last call worked, false = it failed at HTTP level, null = no call yet.</summary>
    public bool? LastCallOk { get; private set; }
    public string? LastError { get; private set; }

    public LogEntry Add(DateTimeOffset time, ChannelGroup group, int channel, string sender, string? world,
                        PlaceholderizedMessage message, bool mentionsMe)
    {
        lock (gate)
        {
            var entry = new LogEntry
            {
                Id = ++nextId, Time = time, Group = group, Channel = channel, Sender = sender, World = world,
                Message = message, MentionsMe = mentionsMe,
            };
            entries.AddLast(entry);
            Trim();
            Version++;
            return entry;
        }
    }

    public void Complete(LogEntry entry, InboundOutcome? outcome)
    {
        entry.Complete(outcome);
        lock (gate)
        {
            if (outcome is { FromCache: false, Attempts.Count: > 0 })
            {
                latencies.Enqueue(outcome.TotalElapsed.TotalMilliseconds);
                while (latencies.Count > LatencyWindow) latencies.Dequeue();
            }
            if (outcome is not null)
            {
                LastCallOk = outcome.HttpFailure is null;
                LastError  = outcome.HttpFailure is null ? null : outcome.FailureReason;
            }
            Version++;
        }
    }

    /// <summary>Entries matching <paramref name="filter"/>, oldest first.</summary>
    public IReadOnlyList<LogEntry> Snapshot(LogFilter filter = LogFilter.All)
    {
        lock (gate)
        {
            return entries.Where(e => filter switch
            {
                LogFilter.PartyOnly    => e.Group is ChannelGroup.Party or ChannelGroup.Alliance,
                LogFilter.MentionsOnly => e.MentionsMe,
                _                      => true,
            }).ToList();
        }
    }

    public LogEntry? Find(long id)
    {
        lock (gate) return entries.FirstOrDefault(e => e.Id == id);
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
            Version++;
        }
    }

    private void Trim()
    {
        while (entries.Count > capacity) entries.RemoveFirst();
    }
}
