using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine;

internal static partial class PurityResourceStateFacts
{
    internal static bool TryCreateMissingOwnedResourceDisposalResult(
        PurityAnalysisState state,
        IMethodSymbol containingMethodSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out PurityAnalysisResult result)
    {
        cancellationToken.ThrowIfCancellationRequested();
        result = PurityAnalysisResult.Pure;

        var ownedResources = new Dictionary<SymbolicTerm, ISymbol?>();
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
            if (SymbolicStateMerger.HasExactResourceRelease(state.PathState, resource.Key)) continue;

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
}
