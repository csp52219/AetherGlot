namespace AetherGlot.Core;

/// <summary>
/// Tracks the last-observed source language per channel key (e.g. "Party", "Say").
/// Used to determine the outbound translation target language from conversation context.
/// </summary>
public interface ITranslationContext
{
    /// <summary>
    /// Records the detected source language for the given channel.
    /// Called after a successful inbound translation.
    /// </summary>
    void UpdateDetectedLanguage(string channelKey, string languageCode);

    /// <summary>
    /// Returns the last detected language for <paramref name="channelKey"/>,
    /// or <paramref name="fallback"/> if no history exists.
    /// </summary>
    string GetContextLanguage(string channelKey, string fallback = "EN");
}
