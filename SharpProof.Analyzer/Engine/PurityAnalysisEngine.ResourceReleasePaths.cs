using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static bool TryCreateMissingOwnedResourceDisposalResult(
        PurityAnalysisState state,
        IMethodSymbol containingMethodSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out PurityAnalysisResult result)
    {
        cancellationToken.ThrowIfCancellationRequested();
        result = PurityAnalysisResult.Pure;

        var ownedResources = new Dictionary<SymbolicTerm, ISymbol?>();
        var releasedResources = CollectExactReleasedResources(state.PathState);
        foreach (var fact in state.PathState.Facts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!fact.Polarity || fact.Confidence != SymbolicFactConfidence.Exact) continue;

            switch (fact.Atom)
            {
                case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime:
                    ownedResources[lifetime.Resource] = fact.Symbol;
                    break;
                case SymbolicDisposalAtom { State: SymbolicDisposalState.NotDisposed } disposal:
                    ownedResources[disposal.Resource] = fact.Symbol;
                    break;
            }
        }

        foreach (var resource in ownedResources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsResourceReleased(resource.Key, releasedResources, state, new HashSet<SymbolicTerm>())) continue;

            var syntax = containingMethodSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .FirstOrDefault(node => node.SyntaxTree == semanticModel.SyntaxTree) ??
                         containingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                             ?.GetSyntax(cancellationToken);
            if (syntax == null) return false;

            result = PurityAnalysisResult.Impure(
                syntax,
                PurityEvidence.Create(
                    "resource_missing_dispose",
                    "ResourceLifetimeAnalysis",
                    syntaxNode: syntax,
                    symbol: resource.Value,
                    catalogSource: "symbolic_resource_lifetime"));
            return true;
        }

        return false;
    }

    internal static bool IsResourceReleased(
        SymbolicTerm resource,
        HashSet<SymbolicTerm> releasedResources,
        PurityAnalysisState state,
        HashSet<SymbolicTerm> visitedTerms)
    {
        if (releasedResources.Contains(resource)) return true;
        if (!visitedTerms.Add(resource)) return false;

        foreach (var aliasTerm in EnumerateSymbolicAliasTerms(resource, state))
            if (IsResourceReleased(aliasTerm, releasedResources, state, visitedTerms))
                return true;

        return false;
    }
}
