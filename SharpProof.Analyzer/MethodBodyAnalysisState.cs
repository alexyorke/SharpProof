using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal sealed class MethodBodyAnalysisState
{
    private readonly ConcurrentDictionary<string, int> _queryExecutionCounts =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Lazy<object>> _symbolicQueryResults =
        new(StringComparer.Ordinal);

    internal MethodBodyAnalysisState(
        IMethodSymbol methodSymbol,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        ImmutableArray<IOperation> operationBlocks,
        IOperation? fallbackRootOperation,
        CancellationToken cancellationToken)
    {
        Snapshot = MethodAnalysisSnapshot.Create(
            methodSymbol,
            declaration,
            semanticModel,
            operationBlocks,
            fallbackRootOperation);
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal MethodAnalysisSnapshot Snapshot { get; }

    internal SymbolicQueryService QueryService { get; } = new();

    internal SymbolicOperationResult<SymbolicCapabilityResult> GetCapabilityOutcome(
        CancellationToken cancellationToken)
    {
        return GetNodeQueryOutcome(
            "capability",
            cancellationToken,
            static (queryService, source, target, token) => queryService.TryQueryCapabilities(
                new SymbolicQueryContext(source, target),
                token));
    }

    internal SymbolicOperationResult<SymbolicComplexityResult> GetComplexityOutcome(
        CancellationToken cancellationToken)
    {
        return GetNodeQueryOutcome(
            "complexity",
            cancellationToken,
            static (queryService, source, target, token) => queryService.TryQueryComplexity(
                new SymbolicQueryContext(source, target),
                token));
    }

    private SymbolicOperationResult<TResult> GetNodeQueryOutcome<TResult>(
        string queryKey,
        CancellationToken cancellationToken,
        Func<SymbolicQueryService, SymbolicSourceInput, SymbolicQueryTarget, CancellationToken,
            SymbolicOperationResult<TResult>> query)
        where TResult : class
    {
        return GetOrCreateSymbolicQueryResult(
            queryKey,
            () => query(QueryService, Snapshot.Source, SymbolicQueryTarget.Node(), cancellationToken));
    }

    internal T GetOrCreateSymbolicQueryResult<T>(
        string queryKey,
        Func<T> query)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(queryKey))
            throw new ArgumentException("A symbolic query key is required.", nameof(queryKey));

        if (query == null) throw new ArgumentNullException(nameof(query));

        var lazy = _symbolicQueryResults.GetOrAdd(
            queryKey,
            _ => new Lazy<object>(
                () =>
                {
                    _queryExecutionCounts.AddOrUpdate(queryKey, 1, static (_, count) => count + 1);
                    return query();
                },
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return (T)lazy.Value;
        }
        catch
        {
            if (_symbolicQueryResults.TryGetValue(queryKey, out var current) &&
                ReferenceEquals(current, lazy))
                _symbolicQueryResults.TryRemove(queryKey, out _);

            throw;
        }
    }

    internal int GetSymbolicQueryExecutionCount(string queryKey)
    {
        return _queryExecutionCounts.TryGetValue(queryKey, out var count) ? count : 0;
    }
}

internal static class AnalyzerSymbolicQueryBoundary
{
    internal static SymbolicConditionProofResult ResolveProof(
        SymbolicOperationResult<SymbolicConditionProofResult> outcome,
        string condition,
        CancellationToken cancellationToken)
    {
        if (outcome.IsSuccess && outcome.Value != null) return outcome.Value;

        cancellationToken.ThrowIfCancellationRequested();
        if (outcome.Error?.Category == SymbolicErrorCategory.Cancellation)
            throw new OperationCanceledException(outcome.Error.Message);

        var reason = outcome.Error == null
            ? "symbolic proof failed without error details"
            : outcome.Error.Code + ": " + outcome.Error.Message;
        return new SymbolicConditionProofResult(
            condition,
            SymbolicTruthValue.Unknown,
            reason);
    }
}

internal sealed class MethodBodyAnalysisContext
{
    private readonly Action<Diagnostic> _reportDiagnostic;

    internal MethodBodyAnalysisContext(
        MethodBodyAnalysisState state,
        AnalyzerOptions options,
        CancellationToken cancellationToken,
        Action<Diagnostic> reportDiagnostic)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        CancellationToken = cancellationToken;
        _reportDiagnostic = reportDiagnostic ?? throw new ArgumentNullException(nameof(reportDiagnostic));
    }

    internal MethodBodyAnalysisState State { get; }

    internal MethodAnalysisSnapshot Snapshot => State.Snapshot;

    internal IMethodSymbol MethodSymbol => Snapshot.MethodSymbol;

    internal SyntaxNode Node => Snapshot.Declaration;

    internal SemanticModel SemanticModel => Snapshot.SemanticModel;

    internal AnalyzerOptions Options { get; }

    internal CancellationToken CancellationToken { get; }

    internal void ReportDiagnostic(Diagnostic diagnostic)
    {
        _reportDiagnostic(diagnostic);
    }
}
