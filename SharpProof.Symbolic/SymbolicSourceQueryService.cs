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
    private readonly SymbolicSourceRangeQueryExecutor _rangeQueryExecutor;

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
        _rangeQueryExecutor = new SymbolicSourceRangeQueryExecutor(_programPointExecutor);
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
        => _rangeQueryExecutor.QueryLine(
            syntaxTree, compilation, line, cancellationToken, smtAnalysis, impliedConditions,
            includeExpressionProgramPoints, includeCurrentStatementCompletionFacts);

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
        => _rangeQueryExecutor.QueryLinePoint(
            syntaxTree, compilation, line, column, cancellationToken, smtAnalysis, impliedConditions,
            includeExpressionProgramPoints, includeCurrentStatementCompletionFacts);

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
        => _rangeQueryExecutor.QuerySpan(
            syntaxTree, compilation, spanStart, spanEnd, cancellationToken, smtAnalysis, impliedConditions,
            includeExpressionProgramPoints, includeCurrentStatementCompletionFacts);

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
        => _rangeQueryExecutor.QueryLineSpan(
            syntaxTree, compilation, startLine, startColumn, endLine, endColumn, cancellationToken,
            smtAnalysis, impliedConditions, includeExpressionProgramPoints,
            includeCurrentStatementCompletionFacts);

    public SymbolicQueryResult QuerySyntaxTreeAllLines(
        SyntaxTree syntaxTree,
        Compilation compilation,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
        => _rangeQueryExecutor.QueryAllLines(
            syntaxTree, compilation, cancellationToken, smtAnalysis, impliedConditions,
            includeExpressionProgramPoints, includeCurrentStatementCompletionFacts);

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
