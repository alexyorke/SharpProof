namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceRangeQueryExecutor(SymbolicSourceProgramPointExecutor programPointExecutor) {
    private readonly SymbolicSourceProgramPointExecutor _programPointExecutor =
        programPointExecutor ?? throw new ArgumentNullException(nameof(programPointExecutor));

    internal SymbolicQueryResult QueryLine(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        Validate(syntaxTree, compilation);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var nodes = SymbolicSourceTargetSelector.FindOnLine(syntaxTree, line, cancellationToken);
        var results = nodes
            .Select(node => _programPointExecutor.AnalyzeAndProjectNode(semanticModel, node, options, cancellationToken))
            .ToArray();

        return SymbolicQueryResult.From(results);
    }
    internal SymbolicProgramPointResult QueryLinePoint(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        Validate(syntaxTree, compilation);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var position = SymbolicSourceLocation.GetPosition(syntaxTree, line, column, cancellationToken);
        var nodes = SymbolicSourceTargetSelector.FindOnLine(syntaxTree, line, cancellationToken);
        if (nodes.Count == 0) throw new ArgumentException("No program points found on --line.", nameof(line));

        var node = SymbolicSourceTargetSelector.SelectNearest(nodes, position);
        return _programPointExecutor.AnalyzeAndProjectNode(semanticModel, node, options, cancellationToken);
    }
    internal SymbolicQueryResult QuerySpan(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int spanStart,
        int spanEnd,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        Validate(syntaxTree, compilation);
        var sourceSpan = SymbolicSourceLocation.GetSourceSpan(syntaxTree, spanStart, spanEnd, cancellationToken);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var nodes = SymbolicSourceTargetSelector.FindInSpan(syntaxTree, sourceSpan, cancellationToken);
        var results = nodes
            .Select(node => _programPointExecutor.AnalyzeAndProjectNode(semanticModel, node, options, cancellationToken))
            .ToArray();
        return SymbolicQueryResult.From(results);
    }
    internal SymbolicQueryResult QueryAllLines(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken) {
        Validate(syntaxTree, compilation);
        var lineCount = syntaxTree.GetText(cancellationToken).Lines.Count;
        var results = new List<SymbolicProgramPointResult>();
        for (var line = 1; line <= lineCount; line++) {
            var lineResult = QueryLine(syntaxTree, compilation, line, options, cancellationToken);
            results.AddRange(lineResult.ProgramPoints);
        }
        return SymbolicQueryResult.From(results);
    }
    private static void Validate(SyntaxTree syntaxTree, Compilation compilation) {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
    }
}
