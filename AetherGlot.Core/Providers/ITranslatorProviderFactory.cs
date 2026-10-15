using AetherGlot.Core.Connection;

namespace AetherGlot.Providers;

/// <summary>
/// Resolves the provider for a connection snapshot. The concrete factory shares one
/// HttpClient; tests supply a mock instead.
/// </summary>
public interface ITranslatorProviderFactory
{
    ITranslatorProvider GetProvider(ConnectionSettings settings);
}
