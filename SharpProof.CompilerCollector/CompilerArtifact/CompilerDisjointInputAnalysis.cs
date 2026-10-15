namespace SharpProof.CompilerArtifact;

// Derives bounded impossible-alias certificates from compiler-owned method symbols.
// Callable body admission and artifact policy remain enforced by the caller.
internal static class CompilerDisjointInputAnalysis
{
    internal static ImmutableArray<CompilerDisjointInputPair> Collect(IMethodSymbol method, CancellationToken cancellationToken)
    {
        // Only Roslyn's closed sealed class symbols certify impossible aliases.
        var pairs = ImmutableArray.CreateBuilder<CompilerDisjointInputPair>();
        if (method.Parameters.Length > 128)
        { return []; }
        var remainingWork = 4096;
        var eligible = method.Parameters.Select(parameter => CompilerIdentityBridge.IsClosedSealedReferenceType(parameter.Type, cancellationToken)).ToArray();
        bool Related(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            for (var current = left; current != null; current = current.BaseType)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remainingWork < 0)
                { return true; }
                if (SymbolEqualityComparer.Default.Equals(current, right))
                { return true; }
            }
            return false;
        }
        for (var left = 0; left < method.Parameters.Length; left++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!eligible[left])
            { continue; }
            for (var right = left + 1; right < method.Parameters.Length; right++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (eligible[right] &&
                    !Related((INamedTypeSymbol)method.Parameters[left].Type, (INamedTypeSymbol)method.Parameters[right].Type) &&
                    !Related((INamedTypeSymbol)method.Parameters[right].Type, (INamedTypeSymbol)method.Parameters[left].Type))
                {
                    pairs.Add(new(left, right));
                }
            }
        }
        return pairs.ToImmutable();
    }
}
