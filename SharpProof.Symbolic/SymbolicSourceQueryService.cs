using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceQueryService
{
    private readonly SymbolicProgramPointAnalyzer _programPointAnalyzer;
    private readonly SymbolicConditionProofEngine _conditionProofEngine;

    public SymbolicSourceQueryService()
        : this(new SymbolicInvariantService())
    {
    }

    public SymbolicSourceQueryService(SymbolicInvariantService invariantService)
    {
        _programPointAnalyzer = new SymbolicProgramPointAnalyzer(
            invariantService ?? throw new ArgumentNullException(nameof(invariantService)));
        _conditionProofEngine = new SymbolicConditionProofEngine(_programPointAnalyzer);
    }

    public SymbolicProgramPointResult QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column = 1,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var query = AnalyzeProgramPoint(
            syntaxTree,
            compilation,
            line,
            column,
            smtAnalysis,
            cancellationToken);
        return ProjectSourceQueryResult(
            syntaxTree,
            query,
            line,
            column,
            impliedConditions,
            smtAnalysis,
            cancellationToken);
    }

    public SymbolicQueryResult QuerySyntaxTreeLine(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var nodes = SymbolicSourceTargetSelector.FindOnLine(
            syntaxTree,
            line,
            cancellationToken,
            includeExpressionProgramPoints);
        var results = nodes
            .Select(node => AnalyzeAndProjectNode(
                    syntaxTree,
                    semanticModel,
                    node,
                    impliedConditions,
                    smtAnalysis,
                    cancellationToken,
                    includeCurrentStatementCompletionFacts))
            .ToArray();

        return SymbolicQueryResult.FromLine(
            syntaxTree.FilePath,
            line,
            results,
            SymbolicSmtDiagnostics.FromService(smtAnalysis));
    }

    public SymbolicProgramPointResult QuerySyntaxTreeLinePoint(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var position = SymbolicSourceLocation.GetPosition(syntaxTree, line, column, cancellationToken);
        var nodes = SymbolicSourceTargetSelector.FindOnLine(
            syntaxTree,
            line,
            cancellationToken,
            includeExpressionProgramPoints);

        if (nodes.Count == 0) throw new ArgumentException("No program points found on --line.", nameof(line));

        var node = SymbolicSourceTargetSelector.SelectNearest(nodes, position);
        var requestedPositionDistance = SymbolicSourceTargetSelector.GetDistance(node, position);
        return AnalyzeAndProjectNode(
            syntaxTree,
            semanticModel,
            node,
            impliedConditions,
            smtAnalysis,
            cancellationToken,
            includeCurrentStatementCompletionFacts,
            line,
            column,
            position,
            requestedPositionDistance,
            SymbolicSourceTargetSelector.ContainsPosition(node, position));
    }

    public SymbolicQueryResult QuerySyntaxTreeSpan(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int spanStart,
        int spanEnd,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var sourceSpan = SymbolicSourceLocation.GetSourceSpan(syntaxTree, spanStart, spanEnd, cancellationToken);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var nodes = SymbolicSourceTargetSelector.FindInSpan(
            syntaxTree,
            sourceSpan,
            includeExpressionProgramPoints,
            cancellationToken);
        var results = nodes
            .Select(node => AnalyzeAndProjectNode(
                    syntaxTree,
                    semanticModel,
                    node,
                    impliedConditions,
                    smtAnalysis,
                    cancellationToken,
                    includeCurrentStatementCompletionFacts))
            .ToArray();
        var startLineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            sourceSpan.Start,
            cancellationToken,
            true);
        var endLineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            sourceSpan.End,
            cancellationToken,
            true);

        return SymbolicQueryResult.FromSpan(
            syntaxTree.FilePath,
            sourceSpan.Start,
            sourceSpan.End,
            startLineColumn.Line,
            startLineColumn.Column,
            endLineColumn.Line,
            endLineColumn.Column,
            results,
            SymbolicSmtDiagnostics.FromService(smtAnalysis));
    }

    public SymbolicQueryResult QuerySyntaxTreeLineSpan(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));

        var spanStart = SymbolicSourceLocation.GetPosition(syntaxTree, startLine, startColumn, cancellationToken);
        var spanEnd = SymbolicSourceLocation.GetPosition(syntaxTree, endLine, endColumn, cancellationToken);
        return QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            cancellationToken,
            smtAnalysis,
            impliedConditions,
            includeExpressionProgramPoints,
            includeCurrentStatementCompletionFacts);
    }

    public SymbolicQueryResult QuerySyntaxTreeAllLines(
        SyntaxTree syntaxTree,
        Compilation compilation,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var lineCount = syntaxTree.GetText(cancellationToken).Lines.Count;
        var lineResults = new List<SymbolicQueryLineGroup>();
        for (var line = 1; line <= lineCount; line++)
        {
            var lineResult = QuerySyntaxTreeLine(
                syntaxTree,
                compilation,
                line,
                cancellationToken,
                smtAnalysis,
                impliedConditions,
                includeExpressionProgramPoints,
                includeCurrentStatementCompletionFacts);
            if (lineResult.ProgramPoints.Count != 0)
                lineResults.Add(new SymbolicQueryLineGroup(line, lineResult.ProgramPoints));
        }

        return SymbolicQueryResult.FromFile(
            syntaxTree.FilePath,
            lineCount,
            lineResults,
            SymbolicSmtDiagnostics.FromService(smtAnalysis));
    }

    public SymbolicProgramPointResult QuerySyntaxTreeAtPosition(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int position,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null)
    {
        ValidateSyntaxTreeQuery(syntaxTree, compilation);

        var query = AnalyzeProgramPointAtPosition(
            syntaxTree,
            compilation,
            position,
            smtAnalysis,
            cancellationToken);
        var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            position,
            cancellationToken,
            true);
        return ProjectSourceQueryResult(
            syntaxTree,
            query,
            lineColumn.Line,
            lineColumn.Column,
            impliedConditions,
            smtAnalysis,
            cancellationToken);
    }

    public SymbolicConditionProofResult ProveConditionAtSource(
        string sourceText,
        string filePath,
        int line,
        int column,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SymbolicSourceCompilationProfile? compilationProfile = null)
        => _conditionProofEngine.ProveAtSource(
            sourceText,
            filePath,
            line,
            column,
            conditionText,
            smtAnalysis,
            references,
            cancellationToken,
            compilationProfile);

    public SymbolicConditionProofResult ProveConditionAtSyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
        => _conditionProofEngine.ProveAtSyntaxTree(
            syntaxTree, compilation, line, column, conditionText, smtAnalysis, cancellationToken);

    internal SymbolicConditionProofResult ProveConditionAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
        => _conditionProofEngine.ProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);

    internal SymbolicConditionProofResult ProveConditionAtAnalysis(
        SemanticModel semanticModel,
        SyntaxNode node,
        SymbolicProgramPointAnalysis analysis,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
        => _conditionProofEngine.ProveAtAnalysis(
            semanticModel, node, analysis, conditionText, smtAnalysis, cancellationToken);

    internal SymbolicConditionProofResult ProveConditionAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
        => _conditionProofEngine.ProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);

    private static void ValidateSyntaxTreeQuery(SyntaxTree syntaxTree, Compilation compilation)
    {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
    }

    private SymbolicProgramPointQueryContext AnalyzeProgramPoint(
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

    private SymbolicProgramPointQueryContext AnalyzeProgramPointAtPosition(
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

    private SymbolicProgramPointResult AnalyzeAndProjectNode(
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
        return ProjectSourceQueryResult(
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

    private SymbolicProgramPointResult ProjectSourceQueryResult(
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
