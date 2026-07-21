namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceProgramPointExecutor(
    SymbolicInvariantService _invariantService,
    SymbolicConditionProofEngine _conditionProofEngine) {
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
        return _invariantService.Analyze(
            semanticModel, position, node, options.SmtAnalysis, cancellationToken);
    }

    internal SymbolicProgramPointResult AnalyzeAndProjectNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        var query = _invariantService.Analyze(
            semanticModel,
            node.SpanStart,
            node,
            options.SmtAnalysis,
            cancellationToken,
            options.IncludeCurrentStatementCompletionFacts);
        return Project(query, options, cancellationToken);
    }

    internal SymbolicProgramPointResult Project(
        SymbolicProgramPointQueryContext query,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        var conditionProofs = _conditionProofEngine.ProveAll(
            query.SemanticModel,
            query.Position,
            query.Node,
            query.Analysis,
            options.ImpliedConditions,
            options.SmtAnalysis,
            cancellationToken);
        return SymbolicProgramPointProjector.Project(
            query,
            conditionProofs,
            cancellationToken);
    }
}
