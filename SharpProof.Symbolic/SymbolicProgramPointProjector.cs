namespace SharpProof.Symbolic;

internal sealed record SymbolicProgramPointQueryContext(
    SemanticModel SemanticModel,
    int Position,
    SyntaxNode Node,
    SymbolicProgramPointAnalysis Analysis);

internal static class SymbolicProgramPointProjector {
    internal static SymbolicProgramPointResult Project(
        SyntaxTree syntaxTree,
        SymbolicProgramPointQueryContext query,
        int line,
        int column,
        IReadOnlyList<SymbolicConditionProofResult> conditionProofs,
        SymbolicSmtDiagnostics smtDiagnostics,
        CancellationToken cancellationToken,
        int? requestedLine = null,
        int? requestedColumn = null,
        int? requestedPosition = null,
        int? requestedPositionDistance = null,
        bool? containsRequestedPosition = null) {
        var nodeSourceSpan = SymbolicSourceLocation.GetNodeSourceSpan(
            syntaxTree,
            query.Node.Span,
            cancellationToken);
        var mergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(query.Analysis.PathConditions);
        var invariant = SymbolicInvariantResult.FromFormulas(
            query.Analysis.PathConditions,
            mergedInvariantText);

        var metadata = new SymbolicProgramPointMetadata(
            syntaxTree.FilePath,
            line,
            column,
            query.Position,
            requestedLine,
            requestedColumn,
            requestedPosition,
            requestedPositionDistance,
            containsRequestedPosition,
            query.Node.SpanStart,
            query.Node.Span.End,
            nodeSourceSpan.StartLine,
            nodeSourceSpan.StartColumn,
            nodeSourceSpan.EndLine,
            nodeSourceSpan.EndColumn,
            query.Node.Kind().ToString(),
            SymbolicProgramPointClassifier.GetContainingMethodName(query.Node),
            SymbolicProgramPointClassifier.GetProgramPointKind(query.Node));
        return new SymbolicProgramPointResult(
            metadata,
            query.Analysis.Facts,
            query.Analysis.Reachability,
            query.Analysis.ReachabilityReason,
            conditionProofs,
            smtDiagnostics,
            mergedInvariantText,
            invariant,
            SymbolicFactInfo.FromState(query.Analysis.PathState),
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
