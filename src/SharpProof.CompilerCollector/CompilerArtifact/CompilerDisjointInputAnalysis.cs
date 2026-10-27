namespace SharpProof.CompilerCollector;

// Derives bounded impossible-alias certificates from compiler-owned method symbols.
// Callable body admission and artifact policy remain enforced by the caller.
internal static class CompilerDisjointInputAnalysis
{
    internal static ImmutableArray<CompilerDisjointInputPair> Collect(IMethodSymbol method, CancellationToken cancellationToken)
    {
        // Closed class types share an instance only along one inheritance chain.
        var pairs = ImmutableArray.CreateBuilder<CompilerDisjointInputPair>();
        if (method.Parameters.Length > 128)
        { return []; }
        // Each pair consumes work in both directions, keeping its count below this limit.
        var remainingWork = CompilerArtifactLimits.MaximumInstructions;
        var eligible = method.Parameters.Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (CompilerIdentityBridge.IsClosedReferenceType(parameter.Type, cancellationToken))).ToImmutableArray();
        bool Related(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            left = RepositoryParameterGuard.NotNull(left, nameof(left));
            for (var current = left; current != null; current = current.BaseType)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remainingWork < 0)
                { return true; }
                if (CompilerIdentityBridge.SameRuntimeType(current, right))
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
