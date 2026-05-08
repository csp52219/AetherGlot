using System.Text.Json;
using AetherGlot.Providers;

namespace AetherGlot.Core;

/// <inheritdoc cref="ITranslationOrchestrator"/>
public sealed class TranslationOrchestrator : ITranslationOrchestrator
{
    private readonly ITranslatorProviderFactory providerFactory;
    private readonly IPromptBuilder promptBuilder;

    public TranslationOrchestrator(ITranslatorProviderFactory providerFactory, IPromptBuilder promptBuilder)
    {
        this.providerFactory = providerFactory;
        this.promptBuilder   = promptBuilder;
    }

    public async Task<TranslationResult> TranslateInboundAsync(
        string text,
        string targetLanguage,
        TranslationOptions options,
        CancellationToken ct = default)
    {
        try
        {
            var provider     = providerFactory.GetProvider(options.ProviderName);
            var systemPrompt = promptBuilder.BuildInboundSystemPrompt(targetLanguage);
            var raw          = await provider
                .TranslateAsync(text, systemPrompt, text, options.ApiKey, options.Model, options.CustomEndpoint, ct)
                .ConfigureAwait(false);
            return ParseInboundResult(text, raw);
        }
        catch (OperationCanceledException)
        {
            return TranslationResult.Failure(text, "Cancelled");
        }
        catch (Exception ex)
        {
            return TranslationResult.Failure(text, ex.Message);
        }
    }

    public async Task<TranslationResult> TranslateOutboundAsync(
        string text,
        string targetLanguage,
        string customPrompt,
        TranslationOptions options,
        CancellationToken ct = default)
    {
        try
        {
            var provider     = providerFactory.GetProvider(options.ProviderName);
            var systemPrompt = promptBuilder.BuildOutboundSystemPrompt(targetLanguage, customPrompt);
            var raw          = await provider
                .TranslateAsync(text, systemPrompt, text, options.ApiKey, options.Model, options.CustomEndpoint, ct)
                .ConfigureAwait(false);

            // Outbound: provider returns plain text, not JSON
            var translated = raw.Trim();
            return TranslationResult.Success(text, translated, targetLanguage);
        }
        catch (OperationCanceledException)
        {
            return TranslationResult.Failure(text, "Cancelled");
        }
        catch (Exception ex)
        {
            return TranslationResult.Failure(text, ex.Message);
        }
    }

    // Strips markdown code fences (```json ... ```) that some models wrap around JSON.
    private static string StripCodeFence(string raw)
    {
        var s = raw.Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
        var firstNewline = s.IndexOf('\n');
        if (firstNewline < 0) return s;
        s = s[(firstNewline + 1)..];
        if (s.EndsWith("```", StringComparison.Ordinal))
            s = s[..^3];
        return s.Trim();
    }

    // Parses {"lang":"XX","text":"..."} — falls back to raw string on malformed JSON.
    private static TranslationResult ParseInboundResult(string originalText, string raw)
    {
        try
        {
            var json        = StripCodeFence(raw);
            using var doc   = JsonDocument.Parse(json);
            var root        = doc.RootElement;
            var lang        = root.GetProperty("lang").GetString() ?? "??";
            var translated  = root.GetProperty("text").GetString() ?? string.Empty;
            return TranslationResult.Success(originalText, translated, lang);
        }
        catch
        {
            return TranslationResult.Success(originalText, raw.Trim(), "??");
        }
    }
}
