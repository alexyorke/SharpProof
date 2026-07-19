using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceRangeQueryExecutor(SymbolicSourceProgramPointExecutor programPointExecutor)
{
    private readonly SymbolicSourceProgramPointExecutor _programPointExecutor =
        programPointExecutor ?? throw new ArgumentNullException(nameof(programPointExecutor));

    internal SymbolicQueryResult QueryLine(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        Validate(syntaxTree, compilation);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var nodes = SymbolicSourceTargetSelector.FindOnLine(
            syntaxTree,
            line,
            cancellationToken,
            options.IncludeExpressionProgramPoints);
        var results = nodes
            .Select(node => _programPointExecutor.AnalyzeAndProjectNode(
                syntaxTree,
                semanticModel,
                node,
                options,
                cancellationToken))
            .ToArray();

        return SymbolicQueryResult.FromLine(
            syntaxTree.FilePath,
            line,
            results,
            SymbolicSmtDiagnostics.FromService(options.SmtAnalysis));
    }

    internal SymbolicProgramPointResult QueryLinePoint(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        Validate(syntaxTree, compilation);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var position = SymbolicSourceLocation.GetPosition(syntaxTree, line, column, cancellationToken);
        var nodes = SymbolicSourceTargetSelector.FindOnLine(
            syntaxTree,
            line,
            cancellationToken,
            options.IncludeExpressionProgramPoints);
        if (nodes.Count == 0) throw new ArgumentException("No program points found on --line.", nameof(line));

        var node = SymbolicSourceTargetSelector.SelectNearest(nodes, position);
        return _programPointExecutor.AnalyzeAndProjectNode(
            syntaxTree,
            semanticModel,
            node,
            options,
            cancellationToken,
            line,
            column,
            position,
            SymbolicSourceTargetSelector.GetDistance(node, position),
            SymbolicSourceTargetSelector.ContainsPosition(node, position));
    }

    internal SymbolicQueryResult QuerySpan(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int spanStart,
        int spanEnd,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        Validate(syntaxTree, compilation);
        var sourceSpan = SymbolicSourceLocation.GetSourceSpan(syntaxTree, spanStart, spanEnd, cancellationToken);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var nodes = SymbolicSourceTargetSelector.FindInSpan(
            syntaxTree,
            sourceSpan,
            options.IncludeExpressionProgramPoints,
            cancellationToken);
        var results = nodes
            .Select(node => _programPointExecutor.AnalyzeAndProjectNode(
                syntaxTree,
                semanticModel,
                node,
                options,
                cancellationToken))
            .ToArray();
        var start = SymbolicSourceLocation.GetLineAndColumn(syntaxTree, sourceSpan.Start, cancellationToken, true);
        var end = SymbolicSourceLocation.GetLineAndColumn(syntaxTree, sourceSpan.End, cancellationToken, true);

        return SymbolicQueryResult.FromSpan(
            syntaxTree.FilePath,
            sourceSpan.Start,
            sourceSpan.End,
            start.Line,
            start.Column,
            end.Line,
            end.Column,
            results,
            SymbolicSmtDiagnostics.FromService(options.SmtAnalysis));
    }

    internal SymbolicQueryResult QueryLineSpan(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));
        var spanStart = SymbolicSourceLocation.GetPosition(syntaxTree, startLine, startColumn, cancellationToken);
        var spanEnd = SymbolicSourceLocation.GetPosition(syntaxTree, endLine, endColumn, cancellationToken);
        return QuerySpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            options,
            cancellationToken);
    }

    internal SymbolicQueryResult QueryAllLines(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        Validate(syntaxTree, compilation);
        var lineCount = syntaxTree.GetText(cancellationToken).Lines.Count;
        var lineResults = new List<SymbolicQueryLineGroup>();
        for (var line = 1; line <= lineCount; line++)
        {
            var lineResult = QueryLine(
                syntaxTree,
                compilation,
                line,
                options,
                cancellationToken);
            if (lineResult.ProgramPoints.Count != 0)
                lineResults.Add(new SymbolicQueryLineGroup(line, lineResult.ProgramPoints));
        }

        return SymbolicQueryResult.FromFile(
            syntaxTree.FilePath,
            lineCount,
            lineResults,
            SymbolicSmtDiagnostics.FromService(options.SmtAnalysis));
    }

    private static void Validate(SyntaxTree syntaxTree, Compilation compilation)
    {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
    }
}
