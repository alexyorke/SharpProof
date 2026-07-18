using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicQueryExecutor
{
    private readonly SymbolicCapabilityService _capabilityService;
    private readonly SymbolicComplexityService _complexityService;
    private readonly SymbolicConditionProofDispatcher _conditionProofDispatcher;
    private readonly SymbolicRuntimeHazardQueryDispatcher _runtimeHazardDispatcher;
    private readonly SymbolicSourceQueryDispatcher _sourceQueryDispatcher;

    internal SymbolicQueryExecutor()
    {
        var invariantService = new SymbolicInvariantService();
        var sourceQueryService = new SymbolicSourceQueryService(invariantService);
        _sourceQueryDispatcher = new SymbolicSourceQueryDispatcher(invariantService, sourceQueryService);
        _conditionProofDispatcher = new SymbolicConditionProofDispatcher(sourceQueryService);
        _runtimeHazardDispatcher = new SymbolicRuntimeHazardQueryDispatcher(
            new SymbolicRuntimeHazardQueryService(invariantService));
        _complexityService = new SymbolicComplexityService();
        _capabilityService = new SymbolicCapabilityService();
    }

    public SymbolicQueryResult Query(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
        {
            var result = _sourceQueryDispatcher.Query(request, token);
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
        var validatedRequest = ValidatedSymbolicQueryRequest.Create(context);
        if (string.IsNullOrWhiteSpace(conditionText))
            throw new ArgumentException("Condition text is required.", nameof(conditionText));

        return ExecuteWithLimits(validatedRequest, cancellationToken, (request, token) =>
            _conditionProofDispatcher.Prove(request, conditionText, token));
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
        return _conditionProofDispatcher.ProveAtSyntaxNode(
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
        return _conditionProofDispatcher.ProveAtSyntaxNode(
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
            var smtAnalysis = request.RequireSmt("Runtime hazard queries require SMT analysis.");
            return _runtimeHazardDispatcher.Query(request, smtAnalysis, hazardOptions, token);
        });
    }

    public SymbolicOperationResult<SymbolicRuntimeHazardQueryResult> TryQueryRuntimeHazards(
        SymbolicQueryContext context,
        SymbolicRuntimeHazardQueryOptions? hazardOptions = null,
        CancellationToken cancellationToken = default)
    {
        return TryExecute(() => QueryRuntimeHazards(context, hazardOptions, cancellationToken));
    }

    public SymbolicComplexityResult QueryComplexity(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithLimits(context, cancellationToken, (request, token) =>
            _complexityService.Query(request.Source, request.Target, request.Options, token));
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
            _capabilityService.Query(request.Source, request.Target, request.Options, token));
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
        Func<ValidatedSymbolicQueryRequest, CancellationToken, TResult> operation)
    {
        return ExecuteWithLimits(
            ValidatedSymbolicQueryRequest.Create(context),
            cancellationToken,
            operation);
    }

    private static TResult ExecuteWithLimits<TResult>(
        ValidatedSymbolicQueryRequest request,
        CancellationToken cancellationToken,
        Func<ValidatedSymbolicQueryRequest, CancellationToken, TResult> operation)
    {
        using var limitScope = SymbolicAnalysisLimitContext.Push(request.Options.AnalysisLimits);
        return operation(request, cancellationToken);
    }

}
