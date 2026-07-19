namespace SharpProof.Symbolic;

internal sealed class SymbolicProgramPointQueryContext(
    SemanticModel semanticModel,
    int position,
    SyntaxNode node,
    SymbolicProgramPointAnalysis analysis)
{
    internal SemanticModel SemanticModel { get; } =
        semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));

    internal int Position { get; } = position;

    internal SyntaxNode Node { get; } = node ?? throw new ArgumentNullException(nameof(node));

    internal SymbolicProgramPointAnalysis Analysis { get; } =
        analysis ?? throw new ArgumentNullException(nameof(analysis));
}

internal static class SymbolicProgramPointProjector
{
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
        bool? containsRequestedPosition = null)
    {
        var nodeSourceSpan = SymbolicSourceLocation.GetNodeSourceSpan(
            syntaxTree,
            query.Node.Span,
            cancellationToken);
        var mergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(query.Analysis.PathConditions);
        var invariant = SymbolicInvariantResult.FromFormulas(
            query.Analysis.PathConditions,
            mergedInvariantText);

        return new SymbolicProgramPointResult(
            syntaxTree.FilePath,
            line,
            column,
            query.Position,
            query.Node.SpanStart,
            query.Node.Kind().ToString(),
            query.Analysis.Facts,
            query.Analysis.Reachability,
            query.Analysis.ReachabilityReason,
            conditionProofs,
            smtDiagnostics,
            mergedInvariantText,
            invariant,
            query.Node.Span.End,
            nodeSourceSpan.StartLine,
            nodeSourceSpan.StartColumn,
            nodeSourceSpan.EndLine,
            nodeSourceSpan.EndColumn,
            SymbolicProgramPointMetadata.GetContainingMethodName(query.Node),
            SymbolicProgramPointMetadata.GetProgramPointKind(query.Node),
            requestedLine,
            requestedColumn,
            requestedPosition,
            requestedPositionDistance,
            containsRequestedPosition,
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
