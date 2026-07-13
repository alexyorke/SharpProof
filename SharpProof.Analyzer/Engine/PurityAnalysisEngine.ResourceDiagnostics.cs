using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    internal static bool IsParameterlessDisposeInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.ReducedFrom ?? invocationOperation.TargetMethod;
        return targetMethod != null &&
               targetMethod.Parameters.Length == 0 &&
               targetMethod.Name is nameof(IDisposable.Dispose) or "DisposeAsync";
    }

    internal static bool HasDisposedResourceFact(PurityAnalysisState currentState, ISymbol resourceSymbol)
    {
        return HasDisposedResourceFactForTerm(
            PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(resourceSymbol, currentState),
            currentState,
            new HashSet<SymbolicTerm>());
    }

    internal static bool TryCreateUseAfterDisposeEvidence(
        IOperation useOperation,
        IOperation? resourceOperation,
        ISymbol usedMemberSymbol,
        PurityAnalysisState currentState,
        string ruleName,
        out PurityEvidence evidence)
    {
        evidence = PurityEvidence.None;
        if (TryResolveTrackedSymbol(resourceOperation, currentState) is not { } resourceSymbol ||
            !HasDisposedResourceFact(currentState, resourceSymbol))
            return false;

        evidence = PurityEvidence.Create(
            "resource_use_after_dispose",
            ruleName,
            useOperation,
            useOperation.Syntax,
            usedMemberSymbol,
            "symbolic_resource_lifetime");
        return true;
    }

    internal static bool TryCreateUseAfterDisposeEvidence(
        IOperation useOperation,
        IOperation? resourceOperation,
        ISymbol usedMemberSymbol,
        PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string ruleName,
        out PurityEvidence evidence)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return TryCreateUseAfterDisposeEvidence(
            useOperation,
            resourceOperation,
            usedMemberSymbol,
            currentState,
            ruleName,
            out evidence);
    }

    internal static bool TryCreateDoubleDisposeEvidence(
        IInvocationOperation invocationOperation,
        IMethodSymbol invokedMethodSymbol,
        PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string ruleName,
        out PurityEvidence evidence)
    {
        cancellationToken.ThrowIfCancellationRequested();
        evidence = PurityEvidence.None;
        if (!IsParameterlessDisposeInvocation(invocationOperation) ||
            invocationOperation.Instance == null ||
            TryResolveTrackedSymbol(invocationOperation.Instance, currentState) is not { } resourceSymbol ||
            !HasDisposedResourceFact(currentState, resourceSymbol))
            return false;

        evidence = PurityEvidence.Create(
            "resource_double_dispose",
            ruleName,
            invocationOperation,
            symbol: invokedMethodSymbol,
            catalogSource: "symbolic_resource_lifetime");
        return true;
    }

    private static bool HasDisposedResourceFactForTerm(
        SymbolicTerm resourceTerm,
        PurityAnalysisState currentState,
        HashSet<SymbolicTerm> visitedTerms)
    {
        if (!visitedTerms.Add(resourceTerm)) return false;

        foreach (var fact in currentState.PathState.Facts)
            if (fact.Polarity &&
                fact.Confidence == SymbolicFactConfidence.Exact &&
                IsDisposedResourceFactForTerm(fact, resourceTerm))
                return true;

        foreach (var aliasTerm in PuritySymbolicStateFacts.EnumerateSymbolicAliasTerms(resourceTerm, currentState))
            if (HasDisposedResourceFactForTerm(aliasTerm, currentState, visitedTerms))
                return true;

        return false;
    }

    private static bool IsDisposedResourceFactForTerm(
        SymbolicFact fact,
        SymbolicTerm resourceTerm)
    {
        return fact.Atom switch
        {
            SymbolicDisposalAtom { State: SymbolicDisposalState.Disposed } disposal =>
                Equals(disposal.Resource, resourceTerm),
            SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Released } lifetime =>
                Equals(lifetime.Resource, resourceTerm) && IsMergedAllPathReleaseFact(fact),
            _ => false
        };
    }

    private static bool IsMergedAllPathReleaseFact(SymbolicFact fact)
    {
        return string.Equals(
            fact.Provenance,
            "analyzer.resource.merge.all-path-release",
            StringComparison.Ordinal);
    }
}
