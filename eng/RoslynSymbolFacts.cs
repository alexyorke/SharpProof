using Microsoft.CodeAnalysis;

namespace SharpProof.Roslyn;

internal static class RoslynSymbolFacts
{
    internal static bool IsOrDerivesFrom(
        ITypeSymbol? type,
        ITypeSymbol? possibleBase,
        bool includeSelf = true)
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
        for (; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    possibleBase.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }
}
