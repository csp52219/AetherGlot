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
    // Prefix of the old Echo output; still ignored so old lines are never re-translated
    private const string LegacyEchoPrefix = "[AetherGlot]";
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
    private readonly TranslationLog translationLog;
    private readonly BubbleBoard bubbles;
    private readonly IPluginLog log;

    // Lines we printed ourselves; ignored when they come back through ChatMessage
    private readonly ConcurrentDictionary<string, DateTime> printedLines = new(StringComparer.Ordinal);
    private static readonly TimeSpan PrintedLineTtl = TimeSpan.FromSeconds(30);

    // Mention detector, rebuilt when the character or job changes (framework thread only)
    private MentionDetector mentionDetector = MentionDetector.Empty;
    private (string Name, uint Job) mentionKey;

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
        TranslationLog translationLog,
        BubbleBoard bubbles,
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
        this.translationLog = translationLog;
        this.bubbles     = bubbles;
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

        // Prevent recursive translation of our own game-chat output
        if (IsOwnOutput(raw)) return;
        if (raw.StartsWith(LegacyEchoPrefix, StringComparison.Ordinal)) return;
        var prefix = config.Display.GameChat.Prefix;
        if (!string.IsNullOrWhiteSpace(prefix) && raw.StartsWith(prefix, StringComparison.Ordinal)) return;

        var localName = objectTable.LocalPlayer?.Name.TextValue;
        if (config.SkipOwnMessages && !string.IsNullOrEmpty(localName) && senderName == localName)
            return;

        if (ChannelInfo.GroupOf(channel) is not { } group) return;

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
        var mentions  = MentionDetectorForPlayer().Matches(message.DisplayOriginal);
        var destinations = config.Display.Resolve(group, inCombat, mentions);
        if (destinations == Destinations.None) return;

        var partyJobs = CapturePartyJobs();
        var hints     = config.Translation.SendContextHints ? CaptureHints(channel, inCombat, partyJobs) : null;
        RememberLine(channel, senderName, message.DisplayOriginal);

        var (plainSender, world) = SplitSender(chatMessage.Sender);
        var entry = destinations.HasFlag(Destinations.Log)
            ? translationLog.Add(DateTimeOffset.Now, group, (int)channel, plainSender, world, message, mentions)
            : null;

        // Replacing the original line has to happen now, inside the event, so it only works
        // when the translation is already cached; otherwise the translation is added as a new line.
        var replaced = destinations.HasFlag(Destinations.GameChat) && config.Display.GameChat.ReplaceOriginal &&
                       TryReplaceWithCached(chatMessage, message, langName, langCode);

        // Fire-and-forget — background thread, never blocks the game thread
        _ = TranslateInboundAsync(plainSender, message, channel, langName, langCode, hints, inCombat, partyJobs,
                                  replaced ? destinations & ~Destinations.GameChat : destinations, entry);
    }

    private bool TryReplaceWithCached(IHandleableChatMessage chatMessage, PlaceholderizedMessage message, string langName, string langCode)
    {
        try
        {
            var request = new InboundRequest(message.Text, message.AutoTranslateTexts, langName, langCode);
            if (!pipeline.TryGetCachedInbound(request, connection(), out var cached) || FormatForGameChat(cached) is not { } line)
                return false;
            chatMessage.Message = new SeString(new TextPayload(line));
            return true;
        }
        catch (Exception ex)
        {
            log.Debug("[AetherGlot] Could not replace the original line ({Type}).", ex.GetType().Name);
            return false;
        }
    }

    private bool IsOwnOutput(string text)
    {
        if (!printedLines.TryGetValue(text, out var at)) return false;
        if (DateTime.UtcNow - at <= PrintedLineTtl) return true;
        printedLines.TryRemove(text, out _);
        return false;
    }

    /// <summary>Prints a translation to the original channel or Echo (framework thread).</summary>
    private void PrintToGameChat(XivChatType channel, string sender, string line)
    {
        var settings = config.Display.GameChat;
        var prefix   = string.IsNullOrWhiteSpace(settings.Prefix) ? string.Empty : settings.Prefix.Trim() + " ";

        // Below the original line the sender is obvious; in Echo it is not
        var text = settings.Output == GameChatOutput.Echo ? $"{prefix}{sender}: {line}" : $"{prefix}{line}";
        var type = settings.Output == GameChatOutput.Echo ? XivChatType.Echo : channel;

        var now = DateTime.UtcNow;
        foreach (var (k, v) in printedLines)
            if (now - v > PrintedLineTtl) printedLines.TryRemove(k, out _);
        printedLines[text] = now;

        chatGui.Print(new XivChatEntry { Type = type, Message = new SeString(new TextPayload(text)) });
    }

    private async Task TranslateInboundAsync(
        string senderName, PlaceholderizedMessage message, XivChatType channel,
        string langName, string langCode, TranslationHints? hints, bool inCombat, IReadOnlyList<string>? partyJobs,
        Destinations destinations, LogEntry? entry)
    {
        InboundOutcome? outcome = null;
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

            outcome = await pipeline
                .TranslateInboundAsync(request, settings, config.Translation.ToPipelineOptions())
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(outcome.DetectedLanguage))
                context.UpdateDetectedLanguage(channel.ToString(), outcome.DetectedLanguage);

            if (outcome.Kind == InboundOutcomeKind.Partial)
                glossary.RecordUnresolved(outcome);

            if (outcome.Kind == InboundOutcomeKind.Fallback)
                log.Debug("[AetherGlot] Translation discarded: {Reason}", outcome.FailureReason ?? "unknown");

            if (destinations.HasFlag(Destinations.Bubble) && BubbleSegments(outcome) is { } segments)
                bubbles.Add(senderName, segments, DateTimeOffset.Now, BubbleNote(outcome));

            if (destinations.HasFlag(Destinations.GameChat) && FormatForGameChat(outcome) is { } line)
                await framework.RunOnFrameworkThread(() => PrintToGameChat(channel, senderName, line)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Pipeline errors are already turned into fallbacks; this is a last resort
            log.Error("[AetherGlot] Inbound translation failed ({Type}).", ex.GetType().Name);
        }
        finally
        {
            if (entry is not null)
                translationLog.Complete(entry, outcome);
        }
    }

    /// <summary>
    /// Text for the game chat, or null when nothing should be printed. The original message is
    /// already in the chat log, so fallbacks and skips print nothing here.
    /// </summary>
    private string? FormatForGameChat(InboundOutcome outcome)
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

    /// <summary>Segments for a bubble, or null when nothing should be shown.</summary>
    private IReadOnlyList<DisplaySegment>? BubbleSegments(InboundOutcome outcome) => outcome.Kind switch
    {
        InboundOutcomeKind.Translated => outcome.Segments,
        InboundOutcomeKind.Partial when config.Translation.UnresolvedMode == UnresolvedDisplayMode.PartialMarked => outcome.Segments,
        _ => null, // skipped / original: the original is already in the chat
    };

    /// <summary>"bio = トイレ（離席）" for the first term the model chose between meanings.</summary>
    private static string? BubbleNote(InboundOutcome outcome) =>
        outcome.Response?.Terms.FirstOrDefault(t => t.Alts.Count > 0) is { } t ? $"{t.Src} = {t.Dst}" : null;

    /// <summary>Your name, job (EN/JA name and abbreviation) and role words.</summary>
    private MentionDetector MentionDetectorForPlayer()
    {
        if (objectTable.LocalPlayer is not { } player) return MentionDetector.Empty;
        var key = (player.Name.TextValue, player.ClassJob.RowId);
        if (key == mentionKey) return mentionDetector;

        var terms = MentionDetector.NameWords(key.TextValue).ToList();
        foreach (var lang in new[] { Dalamud.Game.ClientLanguage.English, Dalamud.Game.ClientLanguage.Japanese })
        {
            if (dataManager.GetExcelSheet<ClassJob>(lang).GetRowOrDefault(key.RowId) is not { } job) continue;
            terms.Add(job.Name.ExtractText());
            terms.Add(job.Abbreviation.ExtractText());
            if (lang == Dalamud.Game.ClientLanguage.English)
            {
                terms.Add(job.NameEnglish.ExtractText());
                terms.AddRange(MentionDetector.RoleWords(job.Role));
            }
        }

        mentionKey      = key;
        mentionDetector = new MentionDetector(terms);
        return mentionDetector;
    }

    /// <summary>"Name@World" style sender text → (name, world).</summary>
    private static (string Name, string? World) SplitSender(SeString sender)
    {
        string? world = null;
        foreach (var p in sender.Payloads)
        {
            if (p is PlayerPayload player)
            {
                world = player.World.ValueNullable?.Name.ExtractText();
                return (player.PlayerName, string.IsNullOrWhiteSpace(world) ? null : world);
            }
        }
        return (sender.TextValue, world);
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
