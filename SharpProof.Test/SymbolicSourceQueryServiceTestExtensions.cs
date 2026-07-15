using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

internal static class SymbolicSourceQueryServiceTestExtensions
{
    internal static SymbolicProgramPointResult QueryFile(
        this SymbolicSourceQueryService service,
        SymbolicFileQuery query,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        return service.QueryFile(
            query.FilePath,
            query.Line,
            query.Column,
            query.References.IsDefaultOrEmpty ? null : query.References,
            cancellationToken,
            smtAnalysis,
            query.ImpliedConditions);
    }

    internal static SymbolicProgramPointResult QueryFile(
        this SymbolicSourceQueryService service,
        string filePath,
        int line,
        int column = 1,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        SymbolicSourceCompilationProfile? compilationProfile = null)
    {
        return SymbolicSourceFile.WithFile(filePath, (sourceText, sourcePath) => service.QuerySource(
            sourceText,
            sourcePath,
            line,
            column,
            references,
            cancellationToken,
            smtAnalysis,
            impliedConditions,
            compilationProfile));
    }

    internal static SymbolicProgramPointResult QuerySource(
        this SymbolicSourceQueryService service,
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
        this SymbolicSourceQueryService service,
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
        this SymbolicSourceQueryService service,
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

    internal static SymbolicProgramPointQueryResult AnalyzeSource(
        this SymbolicSourceQueryService service,
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
        return service.AnalyzeSyntaxTree(
            syntaxTree,
            compilation,
            line,
            column,
            cancellationToken,
            smtAnalysis);
    }

    internal static SymbolicProgramPointQueryResult AnalyzeSourceAtPosition(
        this SymbolicSourceQueryService service,
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
        return service.AnalyzeSyntaxTreeAtPosition(
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
        return service.QuerySyntaxTreeRuntimeHazardsLine(
            syntaxTree,
            compilation,
            line,
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
        return service.QuerySyntaxTreeRuntimeHazardsSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
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
