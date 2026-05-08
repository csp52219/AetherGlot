using AetherGlot.Core;

namespace AetherGlot;

/// <summary>
/// Receives inbound chat events and dispatches translation requests via
/// <see cref="ITranslationOrchestrator"/>. All Dalamud services are injected
/// through the constructor to enable unit-testing of the orchestration logic.
/// </summary>
public sealed class ChatTranslationService
{
    private const string EchoPrefix = "[AetherGlot]";

    private readonly Configuration config;
    private readonly ITranslationOrchestrator orchestrator;
    private readonly ITranslationContext context;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;

    public ChatTranslationService(
        Configuration config,
        ITranslationOrchestrator orchestrator,
        ITranslationContext context,
        IChatGui chatGui,
        IClientState clientState,
        IObjectTable objectTable,
        IPluginLog log)
    {
        this.config      = config;
        this.orchestrator = orchestrator;
        this.context     = context;
        this.chatGui     = chatGui;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.log         = log;
    }

    // Called synchronously from the Dalamud ChatMessage event — must be fast.
    public void HandleInboundMessage(IHandleableChatMessage chatMessage)
    {
        if (!config.InboundEnabled) return;
        if (!config.EnabledChannels.Contains(chatMessage.LogKind)) return;

        var text = chatMessage.Message.TextValue;
        if (string.IsNullOrWhiteSpace(text)) return;

        // Prevent recursive translation of our own Echo outputs
        if (text.StartsWith(EchoPrefix, StringComparison.Ordinal)) return;

        // Skip messages sent by the local player
        if (config.SkipOwnMessages)
        {
            var localName = objectTable.LocalPlayer?.Name.TextValue;
            if (!string.IsNullOrEmpty(localName) &&
                chatMessage.Sender.TextValue == localName) return;
        }

        var senderName  = chatMessage.Sender.TextValue;
        var channel     = chatMessage.LogKind;

        // Fire-and-forget — background thread, never blocks the game thread
        _ = TranslateInboundAsync(senderName, text, channel);
    }

    private async Task TranslateInboundAsync(string senderName, string text, XivChatType channel)
    {
        var options = BuildOptions();
        if (options == null)
        {
            log.Warning("[AetherGlot] Provider or API key not configured.");
            return;
        }

        var targetLang = GetClientLanguageName(clientState.ClientLanguage);
        var result = await orchestrator
            .TranslateInboundAsync(text, targetLang, options)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            log.Error("[AetherGlot] Inbound translation failed: {Error}", result.ErrorMessage ?? "unknown");
            return;
        }

        // Track detected source language for this channel (used by outbound preview)
        context.UpdateDetectedLanguage(channel.ToString(), result.DetectedLanguage);

        // Skip echo when message is already in the client language (unless overridden)
        if (!config.AlwaysTranslate && IsSameAsClientLanguage(result.DetectedLanguage, clientState.ClientLanguage))
            return;

        chatGui.Print(
            $"{EchoPrefix} {senderName} [{result.DetectedLanguage}]: {result.TranslatedText}",
            null,
            (ushort)XivChatType.Echo);
    }

    /// <summary>
    /// Builds the <see cref="TranslationOptions"/> from current configuration,
    /// or returns <c>null</c> if the provider / API key is not set.
    /// </summary>
    internal TranslationOptions? BuildOptions()
    {
        if (string.IsNullOrWhiteSpace(config.SelectedProvider)) return null;
        if (!config.ApiKeys.TryGetValue(config.SelectedProvider, out var apiKey)
            || string.IsNullOrWhiteSpace(apiKey)) return null;

        return new TranslationOptions(
            config.SelectedProvider,
            apiKey,
            config.SelectedModel,
            string.IsNullOrWhiteSpace(config.CustomEndpoint) ? null : config.CustomEndpoint);
    }

    private static bool IsSameAsClientLanguage(string detectedLang, Dalamud.Game.ClientLanguage clientLang)
    {
        var d = detectedLang.ToUpperInvariant();
        return clientLang switch
        {
            Dalamud.Game.ClientLanguage.Japanese => d is "JA" or "JAPANESE",
            Dalamud.Game.ClientLanguage.English  => d is "EN" or "ENGLISH",
            Dalamud.Game.ClientLanguage.German   => d is "DE" or "GERMAN",
            Dalamud.Game.ClientLanguage.French   => d is "FR" or "FRENCH",
            _                                    => false,
        };
    }

    private static string GetClientLanguageName(Dalamud.Game.ClientLanguage lang) =>
        lang switch
        {
            Dalamud.Game.ClientLanguage.Japanese => "Japanese",
            Dalamud.Game.ClientLanguage.English  => "English",
            Dalamud.Game.ClientLanguage.German   => "German",
            Dalamud.Game.ClientLanguage.French   => "French",
            _                                    => "English",
        };
}
