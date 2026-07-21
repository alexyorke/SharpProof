namespace SharpProof.Symbolic;

internal sealed partial class SymbolicQueryExecutor {
    private readonly SymbolicConditionProofEngine _conditionProofEngine;
    private readonly SymbolicInvariantService _invariantService;
    private readonly SymbolicRuntimeHazardQueryService _runtimeHazardService;
    private readonly SymbolicSourceProgramPointExecutor _programPointExecutor;
    private readonly SymbolicSourceRangeQueryExecutor _rangeQueryExecutor;

    internal SymbolicQueryExecutor() {
        _invariantService = new SymbolicInvariantService();
        _conditionProofEngine = new SymbolicConditionProofEngine(_invariantService);
        _programPointExecutor = new SymbolicSourceProgramPointExecutor(_invariantService);
        _rangeQueryExecutor = new SymbolicSourceRangeQueryExecutor(_programPointExecutor);
        _runtimeHazardService = new SymbolicRuntimeHazardQueryService(_invariantService);
    }

    public SymbolicQueryResult Query(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default) {
        return ExecuteWithLimits(context, cancellationToken, QuerySource);
    }

    public SymbolicConditionProofResult Prove(
        SymbolicQueryContext context,
        string conditionText,
        CancellationToken cancellationToken = default) {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));

        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
            ProveSource(request, conditionText, token));
    }

    public SymbolicRuntimeHazardQueryResult QueryRuntimeHazards(
        SymbolicQueryContext context,
        SymbolicRuntimeHazardQueryOptions? hazardOptions = null,
        CancellationToken cancellationToken = default) {
        hazardOptions ??= SymbolicRuntimeHazardQueryOptions.Default;
        return ExecuteWithLimits(context, cancellationToken, (request, token) => {
            var smtAnalysis = RequireSmt(request, "Runtime hazard queries require SMT analysis.");
            return QueryRuntimeHazardsSource(request, smtAnalysis, hazardOptions, token);
        });
    }

    public SymbolicComplexityResult QueryComplexity(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default) {
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
            SymbolicMethodLikeQueryDispatcher.Execute(
                request,
                "Complexity queries support point, position, or line targets only.",
                static node => SymbolicMethodLikeDeclaration.IsSupported(node, includeDestructors: true),
                ExecuteComplexityAnalysis,
                token));
    }

    private static SymbolicComplexityResult ExecuteComplexityAnalysis(
        ResolvedMethodLikeTarget target,
        Compilation compilation,
        CancellationToken cancellationToken) {
        if (target.BodyNode == null)
            throw new ArgumentException("The requested method-like declaration does not have a body.");
        if (target.MethodSymbol == null)
            throw new ArgumentException("Could not resolve the symbol for the requested method-like body.");

        var summary = new SymbolicComplexityAnalysisSession(compilation, cancellationToken).Analyze(target);
        return SymbolicComplexityResultProjector.Project(target, summary, cancellationToken);
    }

    private static TResult ExecuteWithLimits<TResult>(
        SymbolicQueryContext context,
        CancellationToken cancellationToken,
        Func<SymbolicQueryContext, CancellationToken, TResult> operation) {
        if (context == null) throw new ArgumentNullException(nameof(context));
        using var limitScope = SymbolicAnalysisLimitContext.Push(context.Options.AnalysisLimits);
        return operation(context, cancellationToken);
    }

}
