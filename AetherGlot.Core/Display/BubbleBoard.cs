using AetherGlot.Core.Translation;

namespace AetherGlot.Core.Display;

/// <summary>A translation shown next to a party member.</summary>
public sealed class Bubble
{
    public required long Id { get; init; }

    /// <summary>Sender name as written in chat (used to find the party list row).</summary>
    public required string Member { get; init; }
    public required IReadOnlyList<DisplaySegment> Segments { get; init; }
    public required DateTimeOffset Created { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>Optional short note shown under the text (e.g. "bio = bathroom break").</summary>
    public string? Note { get; init; }

    public bool Pinned { get; internal set; }

    public string Text => Rehydrator.ToPlainText(Segments);
    public DateTimeOffset Expires => Created + Duration;
}

/// <summary>What to draw this frame.</summary>
public sealed record BubbleView(Bubble Bubble, float Alpha);

/// <summary>An entry of the small "recent translations" list.</summary>
public sealed record BubbleHistoryItem(string Member, string Text, DateTimeOffset Time);

/// <summary>
/// Bubbles per party member: at most N per member (oldest unpinned dropped), shown for a
/// configurable time that grows with the text length, fading out over the last second.
/// Clicking pins a bubble until clicked again. Thread-safe (added from background threads,
/// read by the draw loop).
/// </summary>
public sealed class BubbleBoard
{
    public const int HistorySize = 3;
    public static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(1);

    private readonly Func<BubbleSettings> settings;
    private readonly object gate = new();
    private readonly List<Bubble> bubbles = [];
    private readonly LinkedList<BubbleHistoryItem> history = new();
    private long nextId;

    public BubbleBoard(Func<BubbleSettings> settings) => this.settings = settings;

    /// <summary>
    /// Display time: the base seconds, plus (when enabled) 0.08 s per character beyond 20,
    /// at most doubling the base time.
    /// </summary>
    public static TimeSpan DurationFor(string text, BubbleSettings s)
    {
        var seconds = Math.Max(1f, s.Seconds);
        if (s.ExtendByLength)
            seconds += Math.Min(seconds, Math.Max(0, text.Length - 20) * 0.08f);
        return TimeSpan.FromSeconds(seconds);
    }

    public Bubble Add(string member, IReadOnlyList<DisplaySegment> segments, DateTimeOffset now, string? note = null)
    {
        var s = settings();
        var text = Rehydrator.ToPlainText(segments);
        lock (gate)
        {
            var bubble = new Bubble
            {
                Id = ++nextId, Member = member, Segments = segments, Created = now,
                Duration = DurationFor(text, s), Note = note,
            };
            bubbles.Add(bubble);

            // Keep at most MaxPerMember for this member, dropping the oldest unpinned ones
            var mine = bubbles.Where(b => b.Member == member).OrderBy(b => b.Created).ToList();
            var excess = mine.Count - Math.Max(1, s.MaxPerMember);
            foreach (var old in mine.Where(b => !b.Pinned && b != bubble).Take(Math.Max(0, excess)))
                bubbles.Remove(old);

            history.AddLast(new BubbleHistoryItem(member, text, now));
            while (history.Count > HistorySize) history.RemoveFirst();
            return bubble;
        }
    }

    /// <summary>Visible bubbles with their alpha; expired unpinned bubbles are removed.</summary>
    public IReadOnlyList<BubbleView> Visible(DateTimeOffset now)
    {
        lock (gate)
        {
            bubbles.RemoveAll(b => !b.Pinned && now >= b.Expires);
            return bubbles.Select(b => new BubbleView(b, Alpha(b, now))).ToList();
        }
    }

    public static float Alpha(Bubble b, DateTimeOffset now)
    {
        if (b.Pinned) return 1f;
        var left = b.Expires - now;
        if (left <= TimeSpan.Zero) return 0f;
        return left >= FadeTime ? 1f : (float)(left / FadeTime);
    }

    /// <summary>Pins or unpins. Unpinning restarts the fade from now.</summary>
    public void TogglePin(long id, DateTimeOffset now)
    {
        lock (gate)
        {
            var i = bubbles.FindIndex(b => b.Id == id);
            if (i < 0) return;
            var b = bubbles[i];
            if (!b.Pinned)
            {
                b.Pinned = true;
                return;
            }
            // Re-create so the timer starts again from now
            bubbles[i] = new Bubble
            {
                Id = b.Id, Member = b.Member, Segments = b.Segments, Note = b.Note,
                Created = now, Duration = DurationFor(b.Text, settings()),
            };
        }
    }

    public IReadOnlyList<BubbleHistoryItem> History
    {
        get { lock (gate) return history.Reverse().ToList(); }
    }

    public void Clear()
    {
        lock (gate)
        {
            bubbles.Clear();
            history.Clear();
        }
    }
}
