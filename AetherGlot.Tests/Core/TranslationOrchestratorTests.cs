using AetherGlot.Core;
using AetherGlot.Providers;
using Moq;
using Xunit;

namespace AetherGlot.Tests.Core;

public sealed class TranslationOrchestratorTests
{
    private readonly Mock<ITranslatorProviderFactory> factoryMock   = new();
    private readonly Mock<ITranslatorProvider>        providerMock  = new();
    private readonly Mock<IPromptBuilder>             promptBuilderMock = new();
    private readonly ITranslationOrchestrator         sut;

    private static readonly TranslationOptions DefaultOptions =
        new("OpenAI", "test-key", "gpt-4o-mini");

    public TranslationOrchestratorTests()
    {
        factoryMock.Setup(f => f.GetProvider(It.IsAny<string>())).Returns(providerMock.Object);
        promptBuilderMock.Setup(p => p.BuildInboundSystemPrompt(It.IsAny<string>())).Returns("sys-in");
        promptBuilderMock.Setup(p => p.BuildOutboundSystemPrompt(It.IsAny<string>(), It.IsAny<string>())).Returns("sys-out");
        sut = new TranslationOrchestrator(factoryMock.Object, promptBuilderMock.Object);
    }

    // ── Inbound ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TranslateInboundAsync_ValidJsonResponse_ReturnsSuccess()
    {
        const string json = """{"lang":"JA","text":"こんにちは"}""";
        providerMock
            .Setup(p => p.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var result = await sut.TranslateInboundAsync("hello", "Japanese", DefaultOptions);

        Assert.True(result.IsSuccess);
        Assert.Equal("こんにちは", result.TranslatedText);
        Assert.Equal("JA", result.DetectedLanguage);
        Assert.Equal("hello", result.OriginalText);
    }

    [Fact]
    public async Task TranslateInboundAsync_NonJsonResponse_FallsBackToRawText()
    {
        providerMock
            .Setup(p => p.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("plain translated text");

        var result = await sut.TranslateInboundAsync("text", "English", DefaultOptions);

        Assert.True(result.IsSuccess);
        Assert.Equal("plain translated text", result.TranslatedText);
        Assert.Equal("??", result.DetectedLanguage);
    }

    [Fact]
    public async Task TranslateInboundAsync_ProviderThrowsHttpException_ReturnsFailure()
    {
        providerMock
            .Setup(p => p.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var result = await sut.TranslateInboundAsync("text", "English", DefaultOptions);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Network error", result.ErrorMessage);
    }

    [Fact]
    public async Task TranslateInboundAsync_Cancelled_ReturnsFailureWithCancelledMessage()
    {
        var cts = new CancellationTokenSource();
        providerMock
            .Setup(p => p.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await sut.TranslateInboundAsync("text", "English", DefaultOptions, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("Cancelled", result.ErrorMessage);
    }

    // ── Outbound ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TranslateOutboundAsync_PlainTextResponse_ReturnsSuccess()
    {
        providerMock
            .Setup(p => p.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("  translated output  ");

        var result = await sut.TranslateOutboundAsync(
            "original", "Japanese", "custom prompt", DefaultOptions);

        Assert.True(result.IsSuccess);
        Assert.Equal("translated output", result.TranslatedText);
        // Outbound uses target language as detected language
        Assert.Equal("Japanese", result.DetectedLanguage);
    }

    [Fact]
    public async Task TranslateOutboundAsync_ProviderThrows_ReturnsFailure()
    {
        providerMock
            .Setup(p => p.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API timeout"));

        var result = await sut.TranslateOutboundAsync(
            "text", "English", string.Empty, DefaultOptions);

        Assert.False(result.IsSuccess);
        Assert.Contains("API timeout", result.ErrorMessage);
    }
}
