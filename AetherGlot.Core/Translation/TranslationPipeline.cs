using AetherGlot.Core.Connection;
using AetherGlot.Core.Security;
using AetherGlot.Providers;

namespace AetherGlot.Core.Translation;

/// <inheritdoc cref="ITranslationPipeline"/>
public sealed class TranslationPipeline : ITranslationPipeline
{
    /// <summary>How many times a response that fails to parse is requested in total.</summary>
    public const int MaxParseAttempts = 2;

    private readonly ITranslatorProviderFactory providerFactory;
    private readonly IPromptBuilder promptBuilder;
    private readonly IModelCapabilityStore capabilities;
    private readonly TranslationCache<InboundOutcome> cache;
    private readonly StringPool systemPrompts = new();

    public TranslationPipeline(
        ITranslatorProviderFactory providerFactory,
        IPromptBuilder promptBuilder,
        IModelCapabilityStore capabilities,
        TranslationCache<InboundOutcome> cache)
    {
        this.providerFactory = providerFactory;
        this.promptBuilder   = promptBuilder;
        this.capabilities    = capabilities;
        this.cache           = cache;
    }

    public TranslationCache<InboundOutcome> Cache => cache;

    public async Task<InboundOutcome> TranslateInboundAsync(
        InboundRequest request, ConnectionSettings connection, PipelineOptions options,
        CancellationToken ct = default, bool bypassCache = false)
    {
        var model    = connection.InboundModel;
        var cacheKey = InboundCacheKey(request, connection, model);
        if (!bypassCache && cache.TryGet(cacheKey, out var cached))
            return cached.AsCached();

        var system   = systemPrompts.Share(promptBuilder.BuildInboundSystemPrompt(request));
        var user     = promptBuilder.BuildInboundUserPrompt(request);
        var attempts = new List<AttemptRecord>();

        InboundOutcome Fallback(string reason, ValidationReport? validation = null,
                                InboundResponse? response = null, HttpFailureKind? http = null) => new()
        {
            Request       = request,
            Kind          = InboundOutcomeKind.Fallback,
            Segments      = Rehydrator.Original(request.SourceText, request.AutoTranslateTexts),
            Response      = response,
            Validation    = validation,
            FailureReason = SecretRedactor.Redact(reason, connection.SecretValues()),
            HttpFailure   = http,
            Attempts      = attempts,
            SystemPrompt  = system,
            UserPrompt    = user,
            Model         = model,
        };

        InboundResponse? parsed = null;
        string? parseError      = null;
        try
        {
            var provider = providerFactory.GetProvider(connection);
            for (var i = 0; i < MaxParseAttempts && parsed is null; i++)
            {
                var llm = await CallAsync(provider, model, system, user, connection, options, attempts, ct).ConfigureAwait(false);
                if (ResponseParser.TryParseInbound(llm.Content, out parsed, out parseError))
                    attempts.Add(Record(llm, null));
                else
                    attempts.Add(Record(llm, parseError));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (LlmHttpException ex)
        {
            return Fallback(ex.Message, http: ex.Kind);
        }
        catch (Exception ex)
        {
            return Fallback(DescribeException(ex));
        }

        if (parsed is null)
            return Fallback($"Invalid response after {MaxParseAttempts} attempts. {parseError}",
                            ValidationReport.SchemaFailure(parseError ?? "unknown"));

        var validation = ResponseValidator.ValidateInbound(parsed, request.SourceText, options.MaxLengthRatio, options.LengthSlack);
        if (!validation.IsAcceptable(options.DiscardRefusals))
            return Fallback(DescribeRejection(validation), validation, parsed);

        InboundOutcome outcome;
        if (parsed.Status == InboundResponse.StatusSkip)
        {
            outcome = new InboundOutcome
            {
                Request = request, Kind = InboundOutcomeKind.Skipped,
                Segments = Rehydrator.Original(request.SourceText, request.AutoTranslateTexts),
                Response = parsed, Validation = validation, Attempts = attempts,
                SystemPrompt = system, UserPrompt = user, Model = model,
            };
        }
        else
        {
            var segments = Rehydrator.Rehydrate(parsed.Translation!, request.AutoTranslateTexts, parsed.Unresolved);
            var partial  = parsed.Status == InboundResponse.StatusPartial || segments.Any(s => s.Kind == SegmentKind.Unresolved);
            outcome = new InboundOutcome
            {
                Request = request, Kind = partial ? InboundOutcomeKind.Partial : InboundOutcomeKind.Translated,
                Segments = segments, Response = parsed, Validation = validation, Attempts = attempts,
                SystemPrompt = system, UserPrompt = user, Model = model,
            };
        }

        cache.Set(cacheKey, outcome);
        return outcome;
    }

    public bool TryGetCachedInbound(InboundRequest request, ConnectionSettings connection, out InboundOutcome outcome)
    {
        if (cache.TryGet(InboundCacheKey(request, connection, connection.InboundModel), out var cached))
        {
            outcome = cached.AsCached();
            return true;
        }
        outcome = null!;
        return false;
    }

    public async Task<OutboundOutcome> TranslateOutboundAsync(
        OutboundRequest request, ConnectionSettings connection, PipelineOptions options,
        CancellationToken ct = default)
    {
        var model    = connection.OutboundModel;
        var system   = systemPrompts.Share(promptBuilder.BuildOutboundSystemPrompt(request, options.OutboundMaxChars));
        var user     = promptBuilder.BuildOutboundUserPrompt(request);
        var attempts = new List<AttemptRecord>();

        OutboundOutcome Failure(string reason, ValidationReport? validation = null,
                                OutboundResponse? response = null, HttpFailureKind? http = null) => new()
        {
            Request = request, Success = false, Response = response, Validation = validation,
            FailureReason = SecretRedactor.Redact(reason, connection.SecretValues()), HttpFailure = http,
            Attempts = attempts, SystemPrompt = system, UserPrompt = user, Model = model,
        };

        OutboundResponse? parsed = null;
        string? parseError       = null;
        try
        {
            var provider = providerFactory.GetProvider(connection);
            for (var i = 0; i < MaxParseAttempts && parsed is null; i++)
            {
                var llm = await CallAsync(provider, model, system, user, connection, options, attempts, ct).ConfigureAwait(false);
                if (ResponseParser.TryParseOutbound(llm.Content, out parsed, out parseError))
                    attempts.Add(Record(llm, null));
                else
                    attempts.Add(Record(llm, parseError));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (LlmHttpException ex)
        {
            return Failure(ex.Message, http: ex.Kind);
        }
        catch (Exception ex)
        {
            return Failure(DescribeException(ex));
        }

        if (parsed is null)
            return Failure($"Invalid response after {MaxParseAttempts} attempts. {parseError}",
                           ValidationReport.SchemaFailure(parseError ?? "unknown"));

        var validation = ResponseValidator.ValidateOutbound(parsed, request.SourceText);
        if (!validation.IsAcceptable(options.DiscardRefusals))
            return Failure(DescribeRejection(validation), validation, parsed);

        return new OutboundOutcome
        {
            Request = request, Success = true, Response = parsed, Validation = validation,
            Attempts = attempts, SystemPrompt = system, UserPrompt = user, Model = model,
        };
    }

    /// <summary>
    /// Calls the model. Uses response_format unless disabled or known to be rejected for this
    /// model; on rejection remembers that and retries once with prompt-only JSON.
    /// </summary>
    private async Task<LlmResponse> CallAsync(
        ITranslatorProvider provider, string model, string system, string user,
        ConnectionSettings connection, PipelineOptions options, List<AttemptRecord> attempts, CancellationToken ct)
    {
        var useJson = connection.UseJsonResponseFormat && capabilities.GetJsonModeSupport(model) != false;
        try
        {
            var response = await provider.CompleteAsync(new LlmRequest(model, system, user, useJson, options.MaxTokens), ct).ConfigureAwait(false);
            if (useJson) capabilities.SetJsonModeSupport(model, true);
            return response;
        }
        catch (LlmHttpException ex) when (useJson && ex.Kind == HttpFailureKind.JsonModeNotSupported)
        {
            capabilities.SetJsonModeSupport(model, false);
            attempts.Add(new AttemptRecord(string.Empty, model, null, null, TimeSpan.Zero, true, ex.Message));
            return await provider.CompleteAsync(new LlmRequest(model, system, user, false, options.MaxTokens), ct).ConfigureAwait(false);
        }
    }

    private static AttemptRecord Record(LlmResponse r, string? error) =>
        new(r.Content, r.Model, r.PromptTokens, r.CompletionTokens, r.Elapsed, r.JsonModeUsed, error);

    private static string DescribeRejection(ValidationReport v)
    {
        var reasons = new List<string>();
        if (v.RefusalDetected)          reasons.Add("refusal/apology detected");
        if (v.TooLong)                  reasons.Add("translation far longer than the source");
        if (!v.PlaceholdersPreserved)   reasons.Add($"placeholders missing: {string.Join(", ", v.MissingPlaceholders)}");
        if (v.UnknownPlaceholders.Count > 0) reasons.Add($"unknown placeholders: {string.Join(", ", v.UnknownPlaceholders)}");
        return "Response rejected: " + string.Join("; ", reasons) + ".";
    }

    private static string DescribeException(Exception ex) => ex switch
    {
        TaskCanceledException      => "The request timed out.",
        HttpRequestException hre   => $"Network error: {hre.HttpRequestError}.",
        InvalidOperationException  => ex.Message, // our own configuration messages
        NotSupportedException      => ex.Message,
        UriFormatException         => "The configured URL is invalid.",
        _                          => $"Unexpected error ({ex.GetType().Name}).",
    };

    private static string InboundCacheKey(InboundRequest r, ConnectionSettings c, string model)
    {
        var at = string.Join("\u001f", r.AutoTranslateTexts.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                                            .Select(kv => kv.Key + "=" + kv.Value));
        return string.Join("\u001e", c.Mode, model, r.TargetLanguageCode, r.SourceText.Trim(), at);
    }
}
