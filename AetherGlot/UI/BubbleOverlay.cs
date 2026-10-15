using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;

namespace AetherGlot.UI;

/// <summary>
/// Draws translation bubbles next to the speaker's party list row (fade out, click to pin) and
/// a small list of the last 3 translations at the bottom right, toggled by a hotkey.
/// Nothing is drawn when the party list is hidden; the log still receives the messages.
/// </summary>
internal sealed class BubbleOverlay
{
    private const float BubbleWidth = 300f;
    private const float Gap = 8f;

    private readonly Configuration config;
    private readonly BubbleBoard board;
    private readonly IGameGui gameGui;
    private readonly IKeyState keyState;
    private readonly Action openLog;
    private bool hotkeyWasDown;

    public BubbleOverlay(Configuration config, BubbleBoard board, IGameGui gameGui, IKeyState keyState, Action openLog)
    {
        this.config   = config;
        this.board    = board;
        this.gameGui  = gameGui;
        this.keyState = keyState;
        this.openLog  = openLog;
    }

    /// <summary>Hotkey edge detection (framework update).</summary>
    public void Update()
    {
        // Never react while the user is typing into one of our text boxes
        if (ImGui.GetIO().WantTextInput) { hotkeyWasDown = true; return; }

        var hk = config.Display.Bubble.HistoryHotkey;
        if (!Enum.TryParse<VirtualKey>(hk.Key, ignoreCase: true, out var key) || !keyState.IsVirtualKeyValid(key))
            return;

        var down = keyState[key]
                   && keyState[VirtualKey.CONTROL] == hk.Ctrl
                   && keyState[VirtualKey.SHIFT] == hk.Shift
                   && keyState[VirtualKey.MENU] == hk.Alt;
        if (down && !hotkeyWasDown)
        {
            config.Display.Bubble.ShowHistory = !config.Display.Bubble.ShowHistory;
            config.Save();
        }
        hotkeyWasDown = down;
    }

    public void Draw()
    {
        if (gameGui.GameUiHidden) return;

        var now   = DateTimeOffset.Now;
        var views = board.Visible(now);
        if (views.Count > 0)
            DrawBubbles(views, now);

        if (config.Display.Bubble.ShowHistory)
            DrawHistory();
    }

    private void DrawBubbles(IReadOnlyList<BubbleView> views, DateTimeOffset now)
    {
        var layout = PartyListLocator.Read(gameGui);
        if (layout is null) return;

        var viewport = ImGui.GetMainViewport();
        var screenRight = viewport.Pos.X + viewport.Size.X;

        // Stack bubbles of the same member downwards from their row
        var nextY = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var view in views.OrderBy(v => v.Bubble.Created))
        {
            if (layout.Find(view.Bubble.Member) is not { } row) continue;

            var x = layout.Max.X + Gap;
            if (x + BubbleWidth > screenRight) x = layout.Min.X - BubbleWidth - Gap; // party list on the right
            var y = nextY.TryGetValue(view.Bubble.Member, out var stacked) ? stacked : row.Min.Y;

            var height = DrawBubble(view, new Vector2(x, y), now);
            nextY[view.Bubble.Member] = y + height + 4;
        }
    }

    private float DrawBubble(BubbleView view, Vector2 pos, DateTimeOffset now)
    {
        var bubble = view.Bubble;
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(new Vector2(BubbleWidth, 0));
        ImGui.SetNextWindowBgAlpha(0.88f * view.Alpha);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Max(0.05f, view.Alpha));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
        ImGui.PushStyleColor(ImGuiCol.Border, ChannelInfo.Color(ChannelGroup.Party) with { W = 0.6f });

        var height = 0f;
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
                                       ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
                                       ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse;
        if (ImGui.Begin($"##agbubble{bubble.Id}", flags))
        {
            var runs = bubble.Segments.Select(s => s.Kind switch
            {
                SegmentKind.AutoTranslate => new TextRun(s.Text, Theme.AutoTranslate),
                SegmentKind.Unresolved    => new TextRun(s.Text, Theme.Unresolved, DashedUnderline: true),
                _                         => new TextRun(s.Text, Theme.Text),
            }).ToList();
            RichText.Draw(runs, wrap: true);

            if (bubble.Note is { } note)
                ImGui.TextColored(Theme.TermChip, note);
            if (bubble.Pinned)
                ImGui.TextColored(Theme.TextMuted, Loc.Localize("BubblePinned", "pinned · click to release"));

            if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                board.TogglePin(bubble.Id, now);
            height = ImGui.GetWindowHeight();
        }
        ImGui.End();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);
        return height;
    }

    private void DrawHistory()
    {
        var history = board.History;
        if (history.Count == 0) return;

        var viewport = ImGui.GetMainViewport();
        var size = new Vector2(320, 0);
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size - new Vector2(16, 120), ImGuiCond.Always, new Vector2(1, 1));
        ImGui.SetNextWindowSize(size);
        ImGui.SetNextWindowBgAlpha(0.75f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
                                       ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;
        if (ImGui.Begin("##aghistory", flags))
        {
            ImGui.TextColored(Theme.TextMuted, Loc.Localize("HistoryTitle", "Recent translations"));
            ImGui.SameLine();
            ImGui.TextColored(Theme.TextMuted, $"({config.Display.Bubble.HistoryHotkey})");
            foreach (var item in history)
            {
                var first = item.Member.Split(' ')[0];
                ImGui.TextColored(ChannelInfo.Color(ChannelGroup.Party), first);
                ImGui.SameLine();
                RichText.Draw([new TextRun(item.Text, Theme.Text)], wrap: false);
            }
            if (ImGui.SmallButton(Loc.Localize("HistoryOpenLog", "Open translation log")))
                openLog();
        }
        ImGui.End();
    }
}
