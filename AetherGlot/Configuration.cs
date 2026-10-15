using Dalamud.Configuration;
using Newtonsoft.Json;

namespace AetherGlot;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>
    /// v1: plaintext ApiKeys + single provider/model. v2: ConnectionConfig + secret store.
    /// v3: outbound prompt became optional style instructions (tone buttons), AlwaysTranslate removed.
    /// v4: game chat output defaults to Echo; "↳" prefix replaced; diagnostics settings.
    /// </summary>
    public const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;

    // --- Connection (no secrets; see ISecretStore) ---
    public ConnectionConfig Connection { get; set; } = new();

    // --- Translation behaviour ---
    public TranslationConfig Translation { get; set; } = new();

    // --- Where translations are shown ---
    public DisplayConfig Display { get; set; } = new();

    // --- Logging for troubleshooting ---
    public DiagnosticsSettings Diagnostics { get; set; } = new();

    public bool InboundEnabled { get; set; } = true;
    public bool OutboundEnabled { get; set; } = false;

    // Skip translation when the sender is the local player
    public bool SkipOwnMessages { get; set; } = true;


    // Use List instead of HashSet for JSON-serialisation compatibility.
    // Replace: Dalamud's loader would otherwise append the saved list to these defaults.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<XivChatType> EnabledChannels { get; set; } =
    [
        XivChatType.Say,
        XivChatType.Shout,
        XivChatType.Yell,
        XivChatType.Party,
        XivChatType.Alliance,
        XivChatType.FreeCompany,
        XivChatType.TellIncoming,
    ];

    /// <summary>Optional style instructions added to the outbound prompt (the tone is chosen in the send preview).</summary>
    public string OutboundPrompt { get; set; } = OutboundPromptDefaults.Default;

    /// <summary>Default channel used for the outbound send preview window.</summary>
    public XivChatType DefaultOutboundChannel { get; set; } = XivChatType.Party;

    /// <summary>Last tone chosen in the send preview (short / normal / polite).</summary>
    public OutboundTone OutboundTone { get; set; } = OutboundTone.Normal;

    // --- v1 settings, read only for migration (ConfigurationMigrator clears them) ---
    public string? SelectedProvider { get; set; }
    public string? SelectedModel { get; set; }
    public string? CustomEndpoint { get; set; }
    public Dictionary<string, string>? ApiKeys { get; set; }

    /// <summary>v1/v2 only; no longer used (the model answers "skip" for same-language messages).</summary>
    public bool? AlwaysTranslate { get; set; }

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
