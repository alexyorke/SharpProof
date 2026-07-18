using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceProgramPointExecutor
{
    private readonly SymbolicProgramPointAnalyzer _programPointAnalyzer;
    private readonly SymbolicConditionProofEngine _conditionProofEngine;

    internal SymbolicSourceProgramPointExecutor(
        SymbolicProgramPointAnalyzer programPointAnalyzer,
        SymbolicConditionProofEngine conditionProofEngine)
    {
        _programPointAnalyzer = programPointAnalyzer ??
                                throw new ArgumentNullException(nameof(programPointAnalyzer));
        _conditionProofEngine = conditionProofEngine ??
                                throw new ArgumentNullException(nameof(conditionProofEngine));
    }

    internal SymbolicProgramPointQueryContext AnalyzeAtLine(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken)
    {
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot(cancellationToken);
        var position = SymbolicSourceLocation.GetPosition(syntaxTree, line, column, cancellationToken);
        var node = SymbolicSourceTargetSelector.FindAtPosition(root, position);
        return _programPointAnalyzer.Analyze(semanticModel, position, node, smtAnalysis, cancellationToken);
    }

    internal SymbolicProgramPointQueryContext AnalyzeAtPosition(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int position,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken)
    {
        var text = syntaxTree.GetText(cancellationToken);
        if (position < 0 || position > text.Length)
            throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot(cancellationToken);
        var node = SymbolicSourceTargetSelector.FindAtPosition(root, position);
        return _programPointAnalyzer.Analyze(semanticModel, position, node, smtAnalysis, cancellationToken);
    }

    internal SymbolicProgramPointResult AnalyzeAndProjectNode(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        SyntaxNode node,
        IEnumerable<string>? impliedConditions,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken,
        bool includeCurrentStatementCompletionFacts,
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
            smtAnalysis,
            cancellationToken,
            includeCurrentStatementCompletionFacts);
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
            impliedConditions,
            smtAnalysis,
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
        IEnumerable<string>? impliedConditions,
        SmtAnalysisService? smtAnalysis,
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
            impliedConditions,
            smtAnalysis,
            cancellationToken);
        return SymbolicProgramPointProjector.Project(
            syntaxTree,
            query,
            line,
            column,
            conditionProofs,
            SymbolicSmtDiagnostics.FromService(smtAnalysis),
            cancellationToken,
            requestedLine,
            requestedColumn,
            requestedPosition,
            requestedPositionDistance,
            containsRequestedPosition);
    }
}
