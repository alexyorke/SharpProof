using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceProgramPointExecutor(
    SymbolicProgramPointAnalyzer programPointAnalyzer,
    SymbolicConditionProofEngine conditionProofEngine)
{
    private readonly SymbolicProgramPointAnalyzer _programPointAnalyzer =
        programPointAnalyzer ?? throw new ArgumentNullException(nameof(programPointAnalyzer));
    private readonly SymbolicConditionProofEngine _conditionProofEngine =
        conditionProofEngine ?? throw new ArgumentNullException(nameof(conditionProofEngine));

    internal SymbolicProgramPointQueryContext AnalyzeAtPosition(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int position,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        var text = syntaxTree.GetText(cancellationToken);
        if (position < 0 || position > text.Length)
            throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot(cancellationToken);
        var node = SymbolicSourceTargetSelector.FindAtPosition(root, position);
        return _programPointAnalyzer.Analyze(
            semanticModel, position, node, options.SmtAnalysis, cancellationToken);
    }

    internal SymbolicProgramPointResult AnalyzeAndProjectNode(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        SyntaxNode node,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken,
        int? requestedLine = null,
        int? requestedColumn = null,
        int? requestedPosition = null,
        int? requestedPositionDistance = null,
        bool? containsRequestedPosition = null)
    {
        var query = _programPointAnalyzer.Analyze(
            semanticModel,
            node.SpanStart,
            node,
            options.SmtAnalysis,
            cancellationToken,
            options.IncludeCurrentStatementCompletionFacts);
        var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            query.Position,
            cancellationToken,
            true);
        return Project(
            syntaxTree,
            query,
            lineColumn.Line,
            lineColumn.Column,
            options,
            cancellationToken,
            requestedLine,
            requestedColumn,
            requestedPosition,
            requestedPositionDistance,
            containsRequestedPosition);
    }

    internal SymbolicProgramPointResult Project(
        SyntaxTree syntaxTree,
        SymbolicProgramPointQueryContext query,
        int line,
        int column,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken,
        int? requestedLine = null,
        int? requestedColumn = null,
        int? requestedPosition = null,
        int? requestedPositionDistance = null,
        bool? containsRequestedPosition = null)
    {
        var conditionProofs = _conditionProofEngine.ProveAll(
            query.SemanticModel,
            query.Position,
            query.Node,
            query.Analysis,
            options.ImpliedConditions,
            options.SmtAnalysis,
            cancellationToken);
        return SymbolicProgramPointProjector.Project(
            syntaxTree,
            query,
            line,
            column,
            conditionProofs,
            SymbolicSmtDiagnostics.FromService(options.SmtAnalysis),
            cancellationToken,
            requestedLine,
            requestedColumn,
            requestedPosition,
            requestedPositionDistance,
            containsRequestedPosition);
    }
}
