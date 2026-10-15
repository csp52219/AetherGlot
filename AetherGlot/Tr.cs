using System.Reflection;

namespace AetherGlot;

/// <summary>
/// Localized UI text. Always passes this plugin's assembly to CheapLoc: the plain
/// <c>Loc.Localize(key, fallback)</c> looks up the *calling* assembly, which the JIT can get wrong
/// after inlining (e.g. inside a LINQ lambda it resolved to System.Linq and showed "#Key").
/// </summary>
internal static class Tr
{
    public static Assembly Assembly { get; } = typeof(Tr).Assembly;

    public static string Get(string key, string fallback) => Loc.Localize(key, fallback, Assembly);
}
