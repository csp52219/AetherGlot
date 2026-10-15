using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.Sheets;

namespace AetherGlot;

/// <summary>
/// Receives inbound chat events, captures game context on the framework thread and runs
/// the translation pipeline in the background. Output goes to the Echo channel for now
/// (the translation log window arrives in phase 3).
/// </summary>
public sealed class ChatTranslationService
{
    private const string EchoPrefix   = "[AetherGlot]";
    private const string UnresolvedOpen  = "‹";
    private const string UnresolvedClose = "›";

    private readonly Configuration config;
    private readonly ITranslationPipeline pipeline;
    private readonly ITranslationContext context;
    private readonly Func<ConnectionSettings> connection;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly ICondition condition;
    private readonly IPartyList partyList;
    private readonly IDataManager dataManager;
    private readonly IFramework framework;
    private readonly IPluginLog log;

    // Recent lines per channel, touched only on the framework thread
    private readonly Dictionary<XivChatType, Queue<string>> recentLines = new();

    // Avoid flooding the log when the connection is not configured
    private DateTime lastConfigWarning = DateTime.MinValue;

    public ChatTranslationService(
        Configuration config,
        ITranslationPipeline pipeline,
        ITranslationContext context,
        Func<ConnectionSettings> connection,
        IChatGui chatGui,
        IClientState clientState,
        IObjectTable objectTable,
        ICondition condition,
        IPartyList partyList,
        IDataManager dataManager,
        IFramework framework,
        IPluginLog log)
    {
        this.config      = config;
        this.pipeline    = pipeline;
        this.context     = context;
        this.connection  = connection;
        this.chatGui     = chatGui;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.condition   = condition;
        this.partyList   = partyList;
        this.dataManager = dataManager;
        this.framework   = framework;
        this.log         = log;
    }

    // Called synchronously from the Dalamud ChatMessage event on the framework thread — must be fast.
    public void HandleInboundMessage(IHandleableChatMessage chatMessage)
    {
        if (!config.InboundEnabled) return;
        if (!config.EnabledChannels.Contains(chatMessage.LogKind)) return;

        // The message object is reused by Dalamud: copy everything needed now
        var text       = chatMessage.Message.TextValue;
        var senderName = chatMessage.Sender.TextValue;
        var channel    = chatMessage.LogKind;
        if (string.IsNullOrWhiteSpace(text)) return;

        // Prevent recursive translation of our own Echo outputs
        if (text.StartsWith(EchoPrefix, StringComparison.Ordinal)) return;

        var localName = objectTable.LocalPlayer?.Name.TextValue;
        if (config.SkipOwnMessages && !string.IsNullOrEmpty(localName) && senderName == localName)
            return;

        var hints = config.Translation.SendContextHints ? CaptureHints(channel) : null;
        RememberLine(channel, senderName, text);

        // Fire-and-forget — background thread, never blocks the game thread
        _ = TranslateInboundAsync(senderName, text, channel, hints);
    }

    private async Task TranslateInboundAsync(string senderName, string text, XivChatType channel, TranslationHints? hints)
    {
        try
        {
            var settings = connection();
            if (ConnectionSettingsBuilder.MissingRequirement(settings) is { } missing)
            {
                if (DateTime.UtcNow - lastConfigWarning > TimeSpan.FromMinutes(5))
                {
                    lastConfigWarning = DateTime.UtcNow;
                    log.Warning("[AetherGlot] Translation skipped: {Reason}", missing);
                }
                return;
            }

            var (langName, langCode) = ClientLanguage(clientState.ClientLanguage);
            // Phase 2 replaces auto-translate payloads with {{atN}} placeholders here
            var request = new InboundRequest(text, new Dictionary<string, string>(), langName, langCode, hints);

            var outcome = await pipeline
                .TranslateInboundAsync(request, settings, config.Translation.ToPipelineOptions())
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(outcome.DetectedLanguage))
                context.UpdateDetectedLanguage(channel.ToString(), outcome.DetectedLanguage);

            var line = FormatForEcho(outcome);
            if (line is null)
            {
                if (outcome.Kind == InboundOutcomeKind.Fallback)
                    log.Debug("[AetherGlot] Translation discarded: {Reason}", outcome.FailureReason ?? "unknown");
                return;
            }

            var lang = string.IsNullOrWhiteSpace(outcome.DetectedLanguage) ? "??" : outcome.DetectedLanguage.ToUpperInvariant();
            var message = $"{EchoPrefix} {senderName} [{lang}]: {line}";
            await framework.RunOnFrameworkThread(() => chatGui.Print(message, null, (ushort)XivChatType.Echo)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Pipeline errors are already turned into fallbacks; this is a last resort
            log.Error("[AetherGlot] Inbound translation failed ({Type}).", ex.GetType().Name);
        }
    }

    /// <summary>
    /// Text to echo, or null when nothing should be shown. The original message is already in
    /// the chat log, so fallbacks and skips print nothing here.
    /// </summary>
    private string? FormatForEcho(InboundOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case InboundOutcomeKind.Translated:
                return outcome.DisplayText;

            case InboundOutcomeKind.Partial when config.Translation.UnresolvedMode == UnresolvedDisplayMode.PartialMarked:
                return string.Concat(outcome.Segments.Select(s =>
                    s.Kind == SegmentKind.Unresolved ? UnresolvedOpen + s.Text + UnresolvedClose : s.Text));

            default:
                // Partial with OriginalOnly/Hide, Skipped, Fallback
                return null;
        }
    }

    private TranslationHints CaptureHints(XivChatType channel)
    {
        string? location = null;
        try
        {
            if (dataManager.GetExcelSheet<TerritoryType>().TryGetRow(clientState.TerritoryType, out var territory))
                location = territory.PlaceName.ValueNullable?.Name.ExtractText();
        }
        catch (Exception ex)
        {
            log.Debug("[AetherGlot] Could not resolve territory name ({Type}).", ex.GetType().Name);
        }

        var jobs = partyList
            .Select(m => m.ClassJob.ValueNullable?.Abbreviation.ExtractText())
            .Where(j => !string.IsNullOrWhiteSpace(j))
            .Select(j => j!)
            .ToList();

        var recent = recentLines.TryGetValue(channel, out var q) ? q.ToList() : [];

        return new TranslationHints(
            InCombat:    condition[ConditionFlag.InCombat],
            Location:    string.IsNullOrWhiteSpace(location) ? null : location,
            PartyJobs:   jobs,
            RecentLines: recent);
    }

    private void RememberLine(XivChatType channel, string sender, string text)
    {
        var max = Math.Max(0, config.Translation.RecentLineCount);
        if (max == 0) return;

        if (!recentLines.TryGetValue(channel, out var q))
            recentLines[channel] = q = new Queue<string>(max + 1);
        q.Enqueue($"{sender}: {text}");
        while (q.Count > max) q.Dequeue();
    }

    private static (string Name, string Code) ClientLanguage(Dalamud.Game.ClientLanguage lang) =>
        lang switch
        {
            Dalamud.Game.ClientLanguage.Japanese => ("Japanese", "ja"),
            Dalamud.Game.ClientLanguage.English  => ("English", "en"),
            Dalamud.Game.ClientLanguage.German   => ("German", "de"),
            Dalamud.Game.ClientLanguage.French   => ("French", "fr"),
            _                                    => ("English", "en"),
        };

    internal static string LanguageName(Dalamud.Game.ClientLanguage lang) => ClientLanguage(lang).Name;
}
