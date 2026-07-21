namespace SharpProof.Symbolic;

internal sealed record SymbolicProgramPointQueryContext(
    SemanticModel SemanticModel,
    int Position,
    SyntaxNode Node,
    SymbolicProgramPointAnalysis Analysis);

internal static class SymbolicProgramPointProjector {
    internal static SymbolicProgramPointResult Project(
        SymbolicProgramPointQueryContext query,
        IReadOnlyList<SymbolicConditionProofResult> conditionProofs,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var mergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(query.Analysis.PathConditions);
        var invariant = SymbolicInvariantResult.FromFormulas(
            query.Analysis.PathConditions,
            mergedInvariantText);
        return new SymbolicProgramPointResult(
            invariant,
            query.Analysis.Reachability,
            query.Analysis.ReachabilityReason,
            conditionProofs,
            SymbolicInputWitnessFactory.CreateReachability(
                query.Analysis.ReachabilityProof?.PathCheck.Witness,
                query.Analysis.PathConditions,
                query.SemanticModel,
                query.Position,
                query.Analysis.Reachability,
                query.Analysis.ReachabilityReason),
            query.Analysis.Truncation);
    }
}
