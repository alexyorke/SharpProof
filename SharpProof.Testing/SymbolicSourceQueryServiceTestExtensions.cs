using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

internal static class SymbolicSourceQueryServiceTestExtensions
{
    internal static SymbolicProgramPointResult QuerySyntaxTree(
        this SymbolicQueryExecutor executor,
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column = 1,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null)
    {
        return executor.Query(new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTarget.Point(line, column),
                new SymbolicQueryOptions(smtAnalysis: smtAnalysis, impliedConditions: impliedConditions)),
            cancellationToken).ProgramPoints.Single();
    }

    internal static SymbolicQueryResult QuerySyntaxTreeLine(
        this SymbolicQueryExecutor executor,
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        return executor.Query(new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTarget.LineNumber(line),
                new SymbolicQueryOptions(
                    smtAnalysis: smtAnalysis,
                    impliedConditions: impliedConditions,
                    includeExpressionProgramPoints: includeExpressionProgramPoints,
                    includeCurrentStatementCompletionFacts: includeCurrentStatementCompletionFacts)),
            cancellationToken);
    }

    internal static SymbolicProgramPointResult QuerySyntaxTreeLinePoint(
        this SymbolicQueryExecutor executor,
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
        return executor.Query(new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTarget.Point(line, column),
                new SymbolicQueryOptions(
                    smtAnalysis: smtAnalysis,
                    impliedConditions: impliedConditions,
                    includeExpressionProgramPoints: includeExpressionProgramPoints,
                    includeCurrentStatementCompletionFacts: includeCurrentStatementCompletionFacts)),
            cancellationToken).ProgramPoints.Single();
    }

    internal static SymbolicQueryResult QuerySyntaxTreeSpan(
        this SymbolicQueryExecutor executor,
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
        return executor.Query(new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTarget.Span(spanStart, spanEnd),
                new SymbolicQueryOptions(
                    smtAnalysis: smtAnalysis,
                    impliedConditions: impliedConditions,
                    includeExpressionProgramPoints: includeExpressionProgramPoints,
                    includeCurrentStatementCompletionFacts: includeCurrentStatementCompletionFacts)),
            cancellationToken);
    }

    internal static SymbolicQueryResult QuerySyntaxTreeLineSpan(
        this SymbolicQueryExecutor executor,
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
        return executor.Query(new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTarget.LineSpan(startLine, startColumn, endLine, endColumn),
                new SymbolicQueryOptions(
                    smtAnalysis: smtAnalysis,
                    impliedConditions: impliedConditions,
                    includeExpressionProgramPoints: includeExpressionProgramPoints,
                    includeCurrentStatementCompletionFacts: includeCurrentStatementCompletionFacts)),
            cancellationToken);
    }

    internal static SymbolicQueryResult QuerySyntaxTreeAllLines(
        this SymbolicQueryExecutor executor,
        SyntaxTree syntaxTree,
        Compilation compilation,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false)
    {
        return executor.Query(new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTarget.AllLines(),
                new SymbolicQueryOptions(
                    smtAnalysis: smtAnalysis,
                    impliedConditions: impliedConditions,
                    includeExpressionProgramPoints: includeExpressionProgramPoints,
                    includeCurrentStatementCompletionFacts: includeCurrentStatementCompletionFacts)),
            cancellationToken);
    }

    internal static SymbolicProgramPointResult QuerySyntaxTreeAtPosition(
        this SymbolicQueryExecutor executor,
        SyntaxTree syntaxTree,
        Compilation compilation,
        int position,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null)
    {
        return executor.Query(new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTarget.AtPosition(position),
                new SymbolicQueryOptions(smtAnalysis: smtAnalysis, impliedConditions: impliedConditions)),
            cancellationToken).ProgramPoints.Single();
    }

    internal static SymbolicConditionProofResult ProveConditionAtSource(
        this SymbolicQueryExecutor executor,
        string sourceText,
        string filePath,
        int line,
        int column,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var source = compilationProfile == null
            ? SymbolicSourceInput.FromText(sourceText, filePath)
            : SymbolicSourceInput.FromTextWithProfile(sourceText, compilationProfile, filePath);
        return executor.Prove(
            new SymbolicQueryContext(
                source,
                SharpProofTarget.Point(line, column),
                new SymbolicQueryOptions(references, smtAnalysis)),
            conditionText,
            cancellationToken);
    }

    internal static SymbolicConditionProofResult ProveConditionAtSyntaxTree(
        this SymbolicQueryExecutor executor,
        SyntaxTree syntaxTree,
        Compilation compilation,
        int line,
        int column,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken = default)
    {
        return executor.Prove(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
                SharpProofTarget.Point(line, column),
                new SymbolicQueryOptions(smtAnalysis: smtAnalysis)),
            conditionText,
            cancellationToken);
    }

    internal static SymbolicProgramPointResult QueryFile(
        this SymbolicQueryExecutor service,
        string filePath,
        int line,
        int column = 1,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Source file does not exist.", filePath);
        return service.QuerySource(
            File.ReadAllText(filePath),
            Path.GetFullPath(filePath),
            line,
            column,
            references,
            cancellationToken,
            smtAnalysis,
            impliedConditions,
            compilationProfile);
    }

    internal static SymbolicProgramPointResult QuerySource(
        this SymbolicQueryExecutor service,
        string sourceText,
        string filePath,
        int line,
        int column = 1,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = Compile(
            sourceText, filePath, references, cancellationToken, compilationProfile);
        return service.QuerySyntaxTree(
            syntaxTree,
            compilation,
            line,
            column,
            cancellationToken,
            smtAnalysis,
            impliedConditions);
    }

    internal static SymbolicProgramPointResult QuerySourceAtPosition(
        this SymbolicQueryExecutor service,
        string sourceText,
        string filePath,
        int position,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = Compile(
            sourceText, filePath, references, cancellationToken, compilationProfile);
        return service.QuerySyntaxTreeAtPosition(
            syntaxTree,
            compilation,
            position,
            cancellationToken,
            smtAnalysis,
            impliedConditions);
    }

    internal static SymbolicQueryResult QuerySourceLine(
        this SymbolicQueryExecutor service,
        string sourceText,
        string filePath,
        int line,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = Compile(
            sourceText,
            filePath,
            references,
            cancellationToken,
            compilationProfile);
        return service.QuerySyntaxTreeLine(
            syntaxTree,
            compilation,
            line,
            cancellationToken,
            smtAnalysis,
            impliedConditions,
            includeExpressionProgramPoints,
            includeCurrentStatementCompletionFacts);
    }

    internal static SymbolicProgramPointResult AnalyzeSource(
        this SymbolicQueryExecutor service,
        string sourceText,
        string filePath,
        int line,
        int column = 1,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = Compile(
            sourceText, filePath, references, cancellationToken, compilationProfile);
        return service.QuerySyntaxTree(
            syntaxTree,
            compilation,
            line,
            column,
            cancellationToken,
            smtAnalysis);
    }

    internal static SymbolicProgramPointResult AnalyzeSourceAtPosition(
        this SymbolicQueryExecutor service,
        string sourceText,
        string filePath,
        int position,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = Compile(
            sourceText, filePath, references, cancellationToken, compilationProfile);
        return service.QuerySyntaxTreeAtPosition(
            syntaxTree,
            compilation,
            position,
            cancellationToken,
            smtAnalysis);
    }

    internal static SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazards(
        this SymbolicRuntimeHazardQueryService service,
        string sourceText,
        string filePath,
        SmtAnalysisService smtAnalysis,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = Compile(
            sourceText,
            filePath,
            references,
            cancellationToken,
            compilationProfile,
            SymbolicSourceCompilationKind.RuntimeHazards);
        return service.QuerySyntaxTreeRuntimeHazards(
            syntaxTree,
            compilation,
            SharpProofTarget.AllLines(),
            smtAnalysis,
            cancellationToken,
            options);
    }

    internal static SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazardsLine(
        this SymbolicRuntimeHazardQueryService service,
        string sourceText,
        string filePath,
        int line,
        SmtAnalysisService smtAnalysis,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = Compile(
            sourceText,
            filePath,
            references,
            cancellationToken,
            compilationProfile,
            SymbolicSourceCompilationKind.RuntimeHazards);
        return service.QuerySyntaxTreeRuntimeHazards(
            syntaxTree,
            compilation,
            SharpProofTarget.LineNumber(line),
            smtAnalysis,
            cancellationToken,
            options);
    }

    internal static SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazardsSpan(
        this SymbolicRuntimeHazardQueryService service,
        string sourceText,
        string filePath,
        int spanStart,
        int spanEnd,
        SmtAnalysisService smtAnalysis,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        var (syntaxTree, compilation) = Compile(
            sourceText,
            filePath,
            references,
            cancellationToken,
            compilationProfile,
            SymbolicSourceCompilationKind.RuntimeHazards);
        return service.QuerySyntaxTreeRuntimeHazards(
            syntaxTree,
            compilation,
            SharpProofTarget.Span(spanStart, spanEnd),
            smtAnalysis,
            cancellationToken,
            options);
    }

    private static (SyntaxTree SyntaxTree, Compilation Compilation) Compile(
        string sourceText,
        string filePath,
        IEnumerable<MetadataReference>? references,
        CancellationToken cancellationToken,
        SymbolicSourceCompilationProfile? compilationProfile,
        SymbolicSourceCompilationKind compilationKind = SymbolicSourceCompilationKind.Query)
    {
        return SymbolicSourceCompilation.Create(
            sourceText,
            filePath,
            compilationKind,
            references,
            cancellationToken,
            compilationProfile);
    }
}
