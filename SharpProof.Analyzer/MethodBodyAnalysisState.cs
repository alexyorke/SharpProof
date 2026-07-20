namespace SharpProof.Analyzer;

internal sealed record AnalyzerQueryOutcome<T>(T? Value, SharpProofError? Error) where T : class
{
    internal bool IsSuccess => Error == null;
}

internal sealed class MethodBodyAnalysisState
{
    private readonly ConcurrentDictionary<string, Lazy<object>> _symbolicQueryResults =
        new(StringComparer.Ordinal);
    private readonly SymbolicConditionProofEngine _conditionProofEngine =
        new(new SymbolicInvariantService());

    internal MethodBodyAnalysisState(MethodAnalysisSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    internal MethodAnalysisSnapshot Snapshot { get; }

    internal SymbolicQueryExecutor QueryExecutor { get; } = new();

    internal AnalyzerQueryOutcome<SymbolicCapabilityResult> GetCapabilityOutcome(
        CancellationToken cancellationToken)
    {
        return GetOrCreateSymbolicQueryResult(
            "capability",
            () => AnalyzerSymbolicQueryBoundary.TryExecute(() => QueryExecutor.QueryCapabilities(
                new SymbolicQueryContext(Snapshot.Source, new SharpProofTarget(SharpProofTargetKind.Node)),
                cancellationToken)));
    }

    internal AnalyzerQueryOutcome<SymbolicComplexityResult> GetComplexityOutcome(
        CancellationToken cancellationToken)
    {
        return GetOrCreateSymbolicQueryResult(
            "complexity",
            () => AnalyzerSymbolicQueryBoundary.TryExecute(() => QueryExecutor.QueryComplexity(
                new SymbolicQueryContext(Snapshot.Source, new SharpProofTarget(SharpProofTargetKind.Node)),
                cancellationToken)));
    }

    internal SymbolicConditionProofResult ProveAtNode(
        SyntaxNode node,
        string condition,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken)
    {
        return ProveAtNode(node, condition, cancellationToken, () => _conditionProofEngine.ProveAtSyntaxNode(
            Snapshot.SemanticModel,
            node,
            condition,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken));
    }

    internal SymbolicConditionProofResult ProveAtNode(
        SyntaxNode node,
        string condition,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken)
    {
        return ProveAtNode(node, condition, cancellationToken, () => _conditionProofEngine.ProveAtSyntaxNode(
            Snapshot.SemanticModel,
            node,
            condition,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken));
    }

    private SymbolicConditionProofResult ProveAtNode(
        SyntaxNode node,
        string condition,
        CancellationToken cancellationToken,
        Func<SymbolicConditionProofResult> prove)
    {
        ValidateNode(node);
        return AnalyzerSymbolicQueryBoundary.ResolveProof(
            AnalyzerSymbolicQueryBoundary.TryExecute(prove),
            condition,
            cancellationToken);
    }

    private void ValidateNode(SyntaxNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        if (node.SyntaxTree != Snapshot.Declaration.SyntaxTree)
            throw new ArgumentException(
                "The proof node must belong to the analyzed method syntax tree.",
                nameof(node));
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
                () => query(),
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

}

internal static class AnalyzerSymbolicQueryBoundary
{
    internal static AnalyzerQueryOutcome<T> TryExecute<T>(Func<T> operation) where T : class
    {
        try
        {
            return new AnalyzerQueryOutcome<T>(operation(), null);
        }
        catch (Exception exception) when (!SymbolicErrorClassifier.IsFatal(exception))
        {
            return new AnalyzerQueryOutcome<T>(null, SymbolicErrorClassifier.FromException(exception));
        }
    }

    internal static SymbolicConditionProofResult ResolveProof(
        AnalyzerQueryOutcome<SymbolicConditionProofResult> outcome,
        string condition,
        CancellationToken cancellationToken)
    {
        if (outcome.IsSuccess && outcome.Value != null) return outcome.Value;

        cancellationToken.ThrowIfCancellationRequested();
        if (outcome.Error?.Category == SharpProofErrorCategory.Cancellation)
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
        AnalyzerTreeConfiguration configuration,
        CancellationToken cancellationToken,
        Action<Diagnostic> reportDiagnostic)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        CancellationToken = cancellationToken;
        _reportDiagnostic = reportDiagnostic ?? throw new ArgumentNullException(nameof(reportDiagnostic));
    }

    internal MethodBodyAnalysisState State { get; }

    internal MethodAnalysisSnapshot Snapshot => State.Snapshot;

    internal IMethodSymbol MethodSymbol => Snapshot.MethodSymbol;

    internal SyntaxNode Node => Snapshot.Declaration;

    internal SemanticModel SemanticModel => Snapshot.SemanticModel;

    internal AnalyzerTreeConfiguration Configuration { get; }

    internal CancellationToken CancellationToken { get; }

    internal void ReportDiagnostic(Diagnostic diagnostic)
    {
        _reportDiagnostic(diagnostic);
    }
}
