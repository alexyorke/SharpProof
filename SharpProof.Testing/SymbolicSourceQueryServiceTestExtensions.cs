using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

internal static class SymbolicSourceQueryServiceTestExtensions
{
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
                SharpProofTargetFactory.Point(line, column),
                new SymbolicQueryOptions(smtAnalysis: smtAnalysis)),
            conditionText,
            cancellationToken);
    }

    internal static SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazards(
        this SymbolicRuntimeHazardQueryService service,
        string sourceText,
        string filePath,
        SmtAnalysisService smtAnalysis,
        IEnumerable<MetadataReference>? references = null,
        CancellationToken cancellationToken = default,
        SymbolicRuntimeHazardQueryOptions? options = null)
    {
        var (syntaxTree, compilation) = Compile(
            sourceText,
            filePath,
            references,
            cancellationToken,
            SymbolicSourceCompilationKind.RuntimeHazards);
        return service.QuerySyntaxTreeRuntimeHazards(
            syntaxTree,
            compilation,
            SharpProofTargetFactory.AllLines(),
            smtAnalysis,
            cancellationToken,
            options);
    }

    private static (SyntaxTree SyntaxTree, Compilation Compilation) Compile(
        string sourceText,
        string filePath,
        IEnumerable<MetadataReference>? references,
        CancellationToken cancellationToken,
        SymbolicSourceCompilationKind compilationKind = SymbolicSourceCompilationKind.Query)
    {
        return SymbolicSourceCompilation.Create(
            sourceText,
            filePath,
            compilationKind,
            references,
            cancellationToken);
    }
}
