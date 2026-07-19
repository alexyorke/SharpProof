using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed partial class SymbolicQueryExecutor
{
    private readonly SymbolicCapabilityService _capabilityService;
    private readonly SymbolicComplexityService _complexityService;
    private readonly SymbolicConditionProofEngine _conditionProofEngine;
    private readonly SymbolicInvariantService _invariantService;
    private readonly SymbolicRuntimeHazardQueryService _runtimeHazardService;
    private readonly SymbolicSourceProgramPointExecutor _programPointExecutor;
    private readonly SymbolicSourceRangeQueryExecutor _rangeQueryExecutor;

    internal SymbolicQueryExecutor()
    {
        _invariantService = new SymbolicInvariantService();
        var programPointAnalyzer = new SymbolicProgramPointAnalyzer(_invariantService);
        _conditionProofEngine = new SymbolicConditionProofEngine(programPointAnalyzer);
        _programPointExecutor = new SymbolicSourceProgramPointExecutor(
            programPointAnalyzer,
            _conditionProofEngine);
        _rangeQueryExecutor = new SymbolicSourceRangeQueryExecutor(_programPointExecutor);
        _runtimeHazardService = new SymbolicRuntimeHazardQueryService(_invariantService);
        _complexityService = new SymbolicComplexityService();
        _capabilityService = new SymbolicCapabilityService();
    }

    public SymbolicQueryResult Query(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
        {
            var result = QuerySource(request, token);
            return request.Options.Filter == null || request.Options.Filter.IsEmpty
                ? result
                : result.Filter(request.Options.Filter);
        });
    }

    public SymbolicOperationResult<SymbolicQueryResult> TryQuery(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => Query(context, cancellationToken));
    }

    public SymbolicConditionProofResult Prove(
        SymbolicQueryContext context,
        string conditionText,
        CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));

        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
            ProveSource(request, conditionText, token));
    }

    public SymbolicOperationResult<SymbolicConditionProofResult> TryProve(
        SymbolicQueryContext context,
        string conditionText,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => Prove(context, conditionText, cancellationToken));
    }

    internal SymbolicConditionProofResult ProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        return _conditionProofEngine.ProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);
    }

    internal SymbolicOperationResult<SymbolicConditionProofResult> TryProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => ProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken));
    }

    internal SymbolicConditionProofResult ProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        return _conditionProofEngine.ProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);
    }

    internal SymbolicOperationResult<SymbolicConditionProofResult> TryProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => ProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken));
    }

    public SymbolicRuntimeHazardQueryResult QueryRuntimeHazards(
        SymbolicQueryContext context,
        SymbolicRuntimeHazardQueryOptions? hazardOptions = null,
        CancellationToken cancellationToken = default)
    {
        hazardOptions ??= SymbolicRuntimeHazardQueryOptions.Default;
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
        {
            var smtAnalysis = RequireSmt(request, "Runtime hazard queries require SMT analysis.");
            return QueryRuntimeHazardsSource(request, smtAnalysis, hazardOptions, token);
        });
    }

    public SymbolicComplexityResult QueryComplexity(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
            _complexityService.Query(request, token));
    }

    public SymbolicOperationResult<SymbolicComplexityResult> TryQueryComplexity(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => QueryComplexity(context, cancellationToken));
    }

    public SymbolicCapabilityResult QueryCapabilities(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
            _capabilityService.Query(request, token));
    }

    public SymbolicOperationResult<SymbolicCapabilityResult> TryQueryCapabilities(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => QueryCapabilities(context, cancellationToken));
    }

    private static SymbolicOperationResult<T> TryExecute<T>(Func<T> operation)
        where T : class
    {
        try
        {
            return SymbolicOperationResult<T>.Success(operation());
        }
        catch (Exception exception) when (!SymbolicErrorClassifier.IsFatal(exception))
        {
            return SymbolicOperationResult<T>.Failure(SymbolicErrorClassifier.FromException(exception));
        }
    }

    private static TResult ExecuteWithLimits<TResult>(
        SymbolicQueryContext context,
        CancellationToken cancellationToken,
        Func<SymbolicQueryContext, CancellationToken, TResult> operation)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        using var limitScope = SymbolicAnalysisLimitContext.Push(context.Options.AnalysisLimits);
        return operation(context, cancellationToken);
    }

}
