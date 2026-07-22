namespace SharpProof.Symbolic;

internal sealed record SymbolicProgramPointQueryContext(
    SemanticModel SemanticModel,
    int Position,
    SyntaxNode Node,
    SymbolicProgramPointAnalysis Analysis);

internal sealed class SymbolicSourceProgramPointExecutor(SymbolicInvariantService _invariantService) {
    internal SymbolicProgramPointQueryContext AnalyzeAtPosition(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int position,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        var text = syntaxTree.GetText(cancellationToken);
        if (position < 0 || position > text.Length)
            throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot(cancellationToken);
        var node = SymbolicSourceTargetSelector.FindAtPosition(root, position);
        return _invariantService.Analyze(semanticModel, position, node, options.SmtAnalysis, cancellationToken);
    }
    internal SymbolicProgramPointResult AnalyzeAndProjectNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        var query = _invariantService.Analyze(semanticModel, node.SpanStart, node, options.SmtAnalysis, cancellationToken);
        return Project(query, cancellationToken);
    }
    internal SymbolicProgramPointResult Project(SymbolicProgramPointQueryContext query, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        return new SymbolicProgramPointResult(
            query.Analysis.PathConditions,
            query.Analysis.Reachability,
            query.Analysis.ReachabilityReason,
            SymbolicInputWitnessFactory.CreateReachability(
                query.Analysis.ReachabilityProof?.PathCheck.Witness,
                query.SemanticModel,
                query.Position,
                query.Analysis.Reachability,
                query.Analysis.ReachabilityReason),
            query.Analysis.AnalysisTruncation);
    }
}
