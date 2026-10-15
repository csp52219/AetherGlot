using Dalamud.Configuration;

namespace AetherGlot;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>v1: plaintext ApiKeys + single provider/model. v2: ConnectionConfig + secret store.</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    // --- Connection (no secrets; see ISecretStore) ---
    public ConnectionConfig Connection { get; set; } = new();

    // --- Translation behaviour ---
    public TranslationConfig Translation { get; set; } = new();

    public bool InboundEnabled { get; set; } = true;
    public bool OutboundEnabled { get; set; } = false;

    // Skip translation when the sender is the local player
    public bool SkipOwnMessages { get; set; } = true;

    // When false (default), messages already in the client language are skipped.
    // When true, they are still sent to the model (it answers "skip" and nothing is shown).
    public bool AlwaysTranslate { get; set; } = false;

    // Use List instead of HashSet for JSON-serialisation compatibility
    public List<XivChatType> EnabledChannels { get; set; } =
    [
        XivChatType.Say,
        XivChatType.Shout,
        XivChatType.Yell,
        XivChatType.Party,
        XivChatType.Alliance,
        XivChatType.FreeCompany,
    ];

    // --- Outbound prompt ---
    public string OutboundPrompt { get; set; } = DefaultOutboundPrompt;

    /// <summary>Default channel used for the outbound send preview window.</summary>
    public XivChatType DefaultOutboundChannel { get; set; } = XivChatType.Party;

    public const string DefaultOutboundPrompt =
        "You are a player in an MMORPG. Translate the user's input into natural English.\n" +
        "- Keep it very casual. Avoid formal grammar or structures.\n" +
        "- Actively use gaming slangs: 'i' instead of 'I', 'btw', 'ty', 'gg', 'brb', 'thx', 'omw', etc.\n" +
        "- Avoid capitalizing words unless necessary. Do not end sentences with periods.";

    // --- v1 settings, read only for migration (ConfigurationMigrator clears them) ---
    public string? SelectedProvider { get; set; }
    public string? SelectedModel { get; set; }
    public string? CustomEndpoint { get; set; }
    public Dictionary<string, string>? ApiKeys { get; set; }

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
