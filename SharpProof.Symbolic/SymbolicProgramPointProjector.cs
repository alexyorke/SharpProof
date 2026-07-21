namespace SharpProof.Symbolic;

internal sealed record SymbolicProgramPointQueryContext(
    SemanticModel SemanticModel,
    int Position,
    SyntaxNode Node,
    SymbolicProgramPointAnalysis Analysis);

internal static class SymbolicProgramPointProjector {
    internal static SymbolicProgramPointResult Project(
        SymbolicProgramPointQueryContext query,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        return new SymbolicProgramPointResult(
            query.Analysis.PathConditions,
            query.Analysis.Reachability,
            query.Analysis.ReachabilityReason,
            SymbolicInputWitnessFactory.CreateReachability(
                query.Analysis.ReachabilityProof?.PathCheck.Witness,
                query.Analysis.PathConditions,
                query.SemanticModel,
                query.Position,
                query.Analysis.Reachability,
                query.Analysis.ReachabilityReason),
            query.Analysis.AnalysisTruncation);
    }
}
