using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceQueryService
{
    private readonly SymbolicConditionProofEngine _conditionProofEngine;
    private readonly SymbolicSourceProgramPointExecutor _programPointExecutor;

    public SymbolicSourceQueryService()
        : this(new SymbolicInvariantService())
    {
    }

    public SymbolicSourceQueryService(SymbolicInvariantService invariantService)
    {
        var programPointAnalyzer = new SymbolicProgramPointAnalyzer(
            invariantService ?? throw new ArgumentNullException(nameof(invariantService)));
        _conditionProofEngine = new SymbolicConditionProofEngine(programPointAnalyzer);
        _programPointExecutor = new SymbolicSourceProgramPointExecutor(
            programPointAnalyzer,
            _conditionProofEngine);
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

        var query = _programPointExecutor.AnalyzeAtLine(
            syntaxTree,
            compilation,
            line,
            column,
            smtAnalysis,
            cancellationToken);
        return _programPointExecutor.Project(
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
            .Select(node => _programPointExecutor.AnalyzeAndProjectNode(
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
        return _programPointExecutor.AnalyzeAndProjectNode(
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
            .Select(node => _programPointExecutor.AnalyzeAndProjectNode(
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

        var query = _programPointExecutor.AnalyzeAtPosition(
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
        return _programPointExecutor.Project(
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

}
