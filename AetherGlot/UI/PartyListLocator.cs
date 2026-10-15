using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;

namespace AetherGlot.UI;

/// <summary>One visible party list row: its text (level glyphs + name) and screen rectangle.</summary>
internal readonly record struct PartyRow(string Text, Vector2 Min, Vector2 Max);

/// <summary>Where the party list is on screen and its rows, in ImGui coordinates.</summary>
internal sealed record PartyListLayout(Vector2 Min, Vector2 Max, IReadOnlyList<PartyRow> Rows)
{
    public PartyRow? Find(string sender)
    {
        foreach (var row in Rows)
            if (PartyNameMatcher.Matches(row.Text, sender)) return row;
        return null;
    }
}

/// <summary>
/// Reads the "_PartyList" addon (FFXIVClientStructs AddonPartyList): PartyMembers[0..MemberCount),
/// each with a Name text node and a Collision node covering the row. Returns null when the
/// party list is not shown (hidden HUD, solo with "hide when solo", cutscene...).
/// Call from the draw loop (game thread).
/// </summary>
internal static unsafe class PartyListLocator
{
    public static PartyListLayout? Read(IGameGui gameGui)
    {
        if (gameGui.GameUiHidden) return null;

        var ptr = gameGui.GetAddonByName("_PartyList");
        if (ptr.IsNull || !ptr.IsVisible) return null;

        var addon  = (AddonPartyList*)ptr.Address;
        var origin = ImGuiOrigin();
        var scale  = ptr.Scale;
        var rows   = new List<PartyRow>();
        var count  = Math.Clamp(addon->MemberCount, 0, addon->PartyMembers.Length);

        for (var i = 0; i < count; i++)
        {
            ref var member = ref addon->PartyMembers[i];
            if (member.Name == null) continue;

            var area = member.Collision != null ? (AtkResNode*)member.Collision : (AtkResNode*)member.Name;
            if ((area->NodeFlags & NodeFlags.Visible) == 0) continue;

            var text = new ReadOnlySeStringSpan(member.Name->NodeText.AsSpan()).ExtractText();
            var min  = origin + new Vector2(area->ScreenX, area->ScreenY);
            var max  = min + new Vector2(area->Width, area->Height) * scale;
            rows.Add(new PartyRow(text, min, max));
        }

        var addonMin = origin + new Vector2(ptr.X, ptr.Y);
        return new PartyListLayout(addonMin, addonMin + ptr.ScaledSize, rows);
    }

    // Game screen coordinates start at the main viewport's origin
    private static Vector2 ImGuiOrigin() => Dalamud.Bindings.ImGui.ImGui.GetMainViewport().Pos;
}
