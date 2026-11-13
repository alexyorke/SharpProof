using Microsoft.CodeAnalysis;

namespace SharpProof.Roslyn;

internal static class RoslynSymbolFacts
{
    internal static bool IsOrDerivesFrom(
        ITypeSymbol? type,
        ITypeSymbol? possibleBase,
        bool includeSelf = true,
        bool compareOriginalDefinitions = true)
    {
        if (possibleBase == null)
        {
            return false;
        }

        var current = type as INamedTypeSymbol;
        if (!includeSelf)
        {
            current = current?.BaseType;
        }
        var expected = compareOriginalDefinitions
            ? possibleBase.OriginalDefinition
            : possibleBase;
        for (; current != null; current = current.BaseType)
        {
            var candidate = compareOriginalDefinitions
                ? current.OriginalDefinition
                : current;
            if (SymbolEqualityComparer.Default.Equals(
                    candidate,
                    expected))
            {
                return true;
            }
        }

        return false;
    }
}
