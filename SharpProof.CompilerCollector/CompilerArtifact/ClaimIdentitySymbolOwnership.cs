// Relative symbol ownership used by semantic claim type-parameter identities.
namespace SharpProof.CompilerArtifact;

internal static class ClaimIdentitySymbolOwnership
{
    internal static bool TryGetContainingMethodDepth(
        IMethodSymbol source, IMethodSymbol candidate, out int depth)
    {
        var candidateDefinition = ContractClauseInventoryBuilder.NormalizeCallable(candidate).OriginalDefinition;
        depth = 1;
        for (var current = source.ContainingSymbol as IMethodSymbol;
             current != null;
             current = current.ContainingSymbol as IMethodSymbol, depth++)
        {
            if (SymbolEqualityComparer.Default.Equals(
                ContractClauseInventoryBuilder.NormalizeCallable(current).OriginalDefinition, candidateDefinition))
            {
                return true;
            }
        }
        return false;
    }

    internal static bool TryGetContainingTypeDepth(
        INamedTypeSymbol source, INamedTypeSymbol candidate, out int depth)
    {
        depth = 0;
        for (var current = source; current != null; current = current.ContainingType, depth++)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, candidate.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

}
