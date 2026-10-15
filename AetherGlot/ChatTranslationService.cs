using Dalamud.Game.ClientState.Conditions;
using Lumina.Excel.Sheets;

namespace AetherGlot;

/// <summary>
/// Receives inbound chat events, captures game context on the framework thread and runs
/// the translation pipeline in the background. Output goes to the Echo channel for now
/// (the translation log window arrives in phase 3).
/// </summary>
internal sealed class ChatTranslationService
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
    private readonly ISeStringEvaluator evaluator;
    private readonly GlossaryManager glossary;
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
        ISeStringEvaluator evaluator,
        GlossaryManager glossary,
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
        this.evaluator   = evaluator;
        this.glossary    = glossary;
        this.log         = log;
    }

    // Called synchronously from the Dalamud ChatMessage event on the framework thread — must be fast.
    public void HandleInboundMessage(IHandleableChatMessage chatMessage)
    {
        if (!config.InboundEnabled) return;
        if (!config.EnabledChannels.Contains(chatMessage.LogKind)) return;

        // The message object is reused by Dalamud: copy everything needed now
        var senderName = chatMessage.Sender.TextValue;
        var channel    = chatMessage.LogKind;
        var raw        = chatMessage.Message.TextValue;
        if (string.IsNullOrWhiteSpace(raw)) return;

        // Prevent recursive translation of our own Echo outputs
        if (raw.StartsWith(EchoPrefix, StringComparison.Ordinal)) return;

        var localName = objectTable.LocalPlayer?.Name.TextValue;
        if (config.SkipOwnMessages && !string.IsNullOrEmpty(localName) && senderName == localName)
            return;

        // Auto-translate phrases become {{atN}}; their text is already in the client language
        var (langName, langCode) = ClientLanguage(clientState.ClientLanguage);
        PlaceholderizedMessage message;
        try
        {
            var parts = SeStringReader.ToParts(chatMessage.Message, evaluator, clientState.ClientLanguage, langCode, glossary.Catalog);
            message = Placeholderizer.Build(parts);
            var now = DateTimeOffset.UtcNow;
            foreach (var at in message.AutoTranslateParts)
                glossary.Catalog.MarkSeen(at.Group, at.Key, now);
        }
        catch (Exception ex)
        {
            log.Debug("[AetherGlot] Could not split the message into parts ({Type}); using plain text.", ex.GetType().Name);
            message = Placeholderizer.Build([new TextPart(raw)]);
        }

        // Nothing typed (only auto-translate phrases): the game already shows it in the client language
        if (!message.TypedText.Any(char.IsLetter)) return;

        // Combat state and party jobs are always used locally for sense resolution;
        // they are sent to the model only when context hints are enabled
        var inCombat  = condition[ConditionFlag.InCombat];
        var partyJobs = CapturePartyJobs();
        var hints     = config.Translation.SendContextHints ? CaptureHints(channel, inCombat, partyJobs) : null;
        RememberLine(channel, senderName, message.DisplayOriginal);

        // Fire-and-forget — background thread, never blocks the game thread
        _ = TranslateInboundAsync(senderName, message, channel, langName, langCode, hints, inCombat, partyJobs);
    }

    private async Task TranslateInboundAsync(
        string senderName, PlaceholderizedMessage message, XivChatType channel,
        string langName, string langCode, TranslationHints? hints, bool inCombat, IReadOnlyList<string>? partyJobs)
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

            // Only the glossary pairs that occur in this message go into the prompt
            var sourceCode = langCode == "ja" ? "en" : "ja";
            var lookup = glossary.Service.Lookup(message.Text, sourceCode, langCode, inCombat, partyJobs, config.Translation.MaxGlossaryHits);
            var request = new InboundRequest(message.Text, message.AutoTranslateTexts, langName, langCode, hints, lookup.ToHints());

            var outcome = await pipeline
                .TranslateInboundAsync(request, settings, config.Translation.ToPipelineOptions())
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(outcome.DetectedLanguage))
                context.UpdateDetectedLanguage(channel.ToString(), outcome.DetectedLanguage);

            if (outcome.Kind == InboundOutcomeKind.Partial)
                glossary.RecordUnresolved(outcome);

            var line = FormatForEcho(outcome);
            if (line is null)
            {
                if (outcome.Kind == InboundOutcomeKind.Fallback)
                    log.Debug("[AetherGlot] Translation discarded: {Reason}", outcome.FailureReason ?? "unknown");
                return;
            }

            var lang = string.IsNullOrWhiteSpace(outcome.DetectedLanguage) ? "??" : outcome.DetectedLanguage.ToUpperInvariant();
            var echo = $"{EchoPrefix} {senderName} [{lang}]: {line}";
            await framework.RunOnFrameworkThread(() => chatGui.Print(echo, null, (ushort)XivChatType.Echo)).ConfigureAwait(false);
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

    private TranslationHints CaptureHints(XivChatType channel, bool inCombat, IReadOnlyList<string> jobs)
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

        var recent = recentLines.TryGetValue(channel, out var q) ? q.ToList() : [];

        return new TranslationHints(
            InCombat:    inCombat,
            Location:    string.IsNullOrWhiteSpace(location) ? null : location,
            PartyJobs:   jobs,
            RecentLines: recent);
    }

    // English job abbreviations of the party (e.g. "SCH") whatever the client language; framework thread only
    private List<string> CapturePartyJobs()
    {
        var jobs = new List<string>();
        if (partyList.Length == 0) return jobs;

        var sheet = dataManager.GetExcelSheet<ClassJob>(Dalamud.Game.ClientLanguage.English);
        foreach (var member in partyList)
        {
            if (sheet.GetRowOrDefault(member.ClassJob.RowId) is { } job)
            {
                var abbr = job.Abbreviation.ExtractText();
                if (!string.IsNullOrWhiteSpace(abbr)) jobs.Add(abbr.ToUpperInvariant());
            }
        }
        return jobs;
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

    internal static string LanguageCode(Dalamud.Game.ClientLanguage lang) => ClientLanguage(lang).Code;
}
