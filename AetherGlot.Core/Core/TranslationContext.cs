using System.Collections.Concurrent;

namespace AetherGlot.Core;

/// <inheritdoc cref="ITranslationContext"/>
public sealed class TranslationContext : ITranslationContext
{
    // ConcurrentDictionary because inbound translations complete on thread-pool threads
    private readonly ConcurrentDictionary<string, string> lastLanguage = new(StringComparer.OrdinalIgnoreCase);

    public void UpdateDetectedLanguage(string channelKey, string languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
            lastLanguage[channelKey] = languageCode.ToUpperInvariant();
    }

    public string GetContextLanguage(string channelKey, string fallback = "EN")
        => lastLanguage.TryGetValue(channelKey, out var lang) ? lang : fallback;
}
