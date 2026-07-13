using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
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
        MethodSymbol = methodSymbol ?? throw new ArgumentNullException(nameof(methodSymbol));
        Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
        SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        OperationBlocks = operationBlocks.IsDefault ? ImmutableArray<IOperation>.Empty : operationBlocks;
        RootOperation = fallbackRootOperation ?? SelectRootOperation(OperationBlocks);
        VisibleOperations = RootOperation == null
            ? ImmutableArray<IOperation>.Empty
            : ExecutionVisibility.VisibleDescendants(RootOperation).ToImmutableArray();
        SemanticFacts = new MethodBodySemanticFacts(
            OperationBlocks.Length,
            VisibleOperations.Length,
            VisibleOperations.Count(static operation => operation is IReturnOperation),
            VisibleOperations.Any(static operation => operation is ILocalFunctionOperation),
            RootOperation != null);
        Source = SymbolicSourceInput.FromNode(Declaration, SemanticModel);
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal IMethodSymbol MethodSymbol { get; }

    internal SyntaxNode Declaration { get; }

    internal SemanticModel SemanticModel { get; }

    internal ImmutableArray<IOperation> OperationBlocks { get; }

    internal IOperation? RootOperation { get; }

    internal ImmutableArray<IOperation> VisibleOperations { get; }

    internal MethodBodySemanticFacts SemanticFacts { get; }

    internal SymbolicSourceInput Source { get; }

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
            () => query(QueryService, Source, SymbolicQueryTarget.Node(), cancellationToken));
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

    private static IOperation? SelectRootOperation(ImmutableArray<IOperation> operationBlocks)
    {
        if (operationBlocks.IsDefaultOrEmpty) return null;

        return operationBlocks
            .OrderByDescending(static operation => operation.Syntax.Span.Length)
            .First();
    }
}

internal sealed class MethodBodySemanticFacts
{
    internal MethodBodySemanticFacts(
        int operationBlockCount,
        int visibleOperationCount,
        int returnOperationCount,
        bool containsLocalFunction,
        bool hasRootOperation)
    {
        OperationBlockCount = operationBlockCount;
        VisibleOperationCount = visibleOperationCount;
        ReturnOperationCount = returnOperationCount;
        ContainsLocalFunction = containsLocalFunction;
        HasRootOperation = hasRootOperation;
    }

    internal int OperationBlockCount { get; }

    internal int VisibleOperationCount { get; }

    internal int ReturnOperationCount { get; }

    internal bool ContainsLocalFunction { get; }

    internal bool HasRootOperation { get; }
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

    internal IMethodSymbol MethodSymbol => State.MethodSymbol;

    internal SyntaxNode Node => State.Declaration;

    internal SemanticModel SemanticModel => State.SemanticModel;

    internal AnalyzerOptions Options { get; }

    internal CancellationToken CancellationToken { get; }

    internal void ReportDiagnostic(Diagnostic diagnostic)
    {
        _reportDiagnostic(diagnostic);
    }
}
