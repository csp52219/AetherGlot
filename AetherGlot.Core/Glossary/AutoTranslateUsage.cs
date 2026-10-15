namespace AetherGlot.Core.Glossary;

/// <summary>How the plugin uses phrases of an auto-translate category (shown in the viewer).</summary>
public enum AutoTranslateUsage
{
    /// <summary>Received: shown as is, in the client language.</summary>
    DisplayAsIs,

    /// <summary>Jobs: fixed when received; typed job names are replaced with the official name when sending.</summary>
    JobName,

    /// <summary>Actions: also used to match typed action names (e.g. "bio").</summary>
    ActionName,

    /// <summary>Places: also used for the location context hint.</summary>
    PlaceName,
}

public static class AutoTranslateUsages
{
    private static readonly HashSet<string> ActionSheets = new(StringComparer.Ordinal)
    {
        "Action", "GeneralAction", "CraftAction", "BuddyAction", "PetAction", "ActionComboRoute",
    };

    /// <summary>Decided from the sheet the category refers to (Completion.LookupTable), not from group numbers.</summary>
    public static AutoTranslateUsage Of(AutoTranslateCategory category) => Of(category.LookupTable);

    public static AutoTranslateUsage Of(string? lookupTable)
    {
        var table = LookupTable.Parse(lookupTable);
        if (table is null || table.InCompletion) return AutoTranslateUsage.DisplayAsIs;
        if (table.Sheet == "ClassJob") return AutoTranslateUsage.JobName;
        if (table.Sheet == "PlaceName") return AutoTranslateUsage.PlaceName;
        return ActionSheets.Contains(table.Sheet) ? AutoTranslateUsage.ActionName : AutoTranslateUsage.DisplayAsIs;
    }
}
