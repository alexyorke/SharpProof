using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine;

internal static partial class PurityResourceStateFacts
{
    internal static bool IsParameterlessDisposeInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.ReducedFrom ?? invocationOperation.TargetMethod;
        return targetMethod != null &&
               targetMethod.Parameters.Length == 0 &&
               targetMethod.Name is nameof(IDisposable.Dispose) or "DisposeAsync";
    }

    internal static bool HasDisposedResourceFact(PurityAnalysisState currentState, ISymbol resourceSymbol)
        => HasDisposedResourceFactForTerm(
            PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(resourceSymbol, currentState), currentState);

    internal static bool TryCreateUseAfterDisposeEvidence(
        IOperation useOperation,
        IOperation? resourceOperation,
        ISymbol usedMemberSymbol,
        PurityAnalysisState currentState,
        CancellationToken cancellationToken,
        string ruleName,
        out PurityEvidence evidence)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

    internal static bool TryCreateDoubleDisposeEvidence(
        IInvocationOperation invocationOperation,
        IMethodSymbol invokedMethodSymbol,
        PurityAnalysisState currentState,
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

    internal static bool HasDisposedResourceFactForTerm(
        SymbolicTerm resourceTerm,
        PurityAnalysisState currentState) =>
        SymbolicStateMerger.ExactAliasComponentFactAny(
            resourceTerm, currentState.PathState.Facts, IsDisposedResourceFactForTerm);

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
