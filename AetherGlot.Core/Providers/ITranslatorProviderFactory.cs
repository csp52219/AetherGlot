namespace AetherGlot.Providers;

/// <summary>
/// Resolves a named provider to an <see cref="ITranslatorProvider"/> instance.
/// The concrete implementation in the plugin project injects an HttpClient;
/// tests supply a mock instead.
/// </summary>
public interface ITranslatorProviderFactory
{
    ITranslatorProvider GetProvider(string providerName);
}
