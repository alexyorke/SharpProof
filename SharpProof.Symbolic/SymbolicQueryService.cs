using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicQueryService
{
    private readonly SymbolicQueryExecutor _executor;

    public SymbolicQueryService()
        : this(new SymbolicInvariantService())
    {
    }

    internal SymbolicQueryService(SymbolicInvariantService invariantService)
    {
        _executor = new SymbolicQueryExecutor(invariantService);
    }

    public SymbolicQueryResult Query(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return _executor.Query(context, cancellationToken);
    }

    public SymbolicOperationResult<SymbolicQueryResult> TryQuery(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return _executor.TryQuery(context, cancellationToken);
    }

    public SymbolicConditionProofResult Prove(
        SymbolicQueryContext context,
        string conditionText,
        CancellationToken cancellationToken = default)
    {
        return _executor.Prove(context, conditionText, cancellationToken);
    }

    public SymbolicOperationResult<SymbolicConditionProofResult> TryProve(
        SymbolicQueryContext context,
        string conditionText,
        CancellationToken cancellationToken = default)
    {
        return _executor.TryProve(context, conditionText, cancellationToken);
    }

    internal SymbolicConditionProofResult ProveAtSyntaxNode(
        SemanticModel semanticModel,
        SyntaxNode node,
        string conditionText,
        SmtAnalysisService smtAnalysis,
        bool includeCurrentStatementCompletionFacts,
        CancellationToken cancellationToken = default)
    {
        return _executor.ProveAtSyntaxNode(
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
        return _executor.TryProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);
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
        return _executor.ProveAtSyntaxNode(
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
        return _executor.TryProveAtSyntaxNode(
            semanticModel,
            node,
            conditionText,
            symbolicCondition,
            initialState,
            smtAnalysis,
            includeCurrentStatementCompletionFacts,
            cancellationToken);
    }

    public SymbolicRuntimeHazardQueryResult QueryRuntimeHazards(
        SymbolicQueryContext context,
        SymbolicRuntimeHazardQueryOptions? hazardOptions = null,
        CancellationToken cancellationToken = default)
    {
        return _executor.QueryRuntimeHazards(context, hazardOptions, cancellationToken);
    }

    public SymbolicOperationResult<SymbolicRuntimeHazardQueryResult> TryQueryRuntimeHazards(
        SymbolicQueryContext context,
        SymbolicRuntimeHazardQueryOptions? hazardOptions = null,
        CancellationToken cancellationToken = default)
    {
        return _executor.TryQueryRuntimeHazards(context, hazardOptions, cancellationToken);
    }

    public SymbolicComplexityResult QueryComplexity(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return _executor.QueryComplexity(context, cancellationToken);
    }

    public SymbolicOperationResult<SymbolicComplexityResult> TryQueryComplexity(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return _executor.TryQueryComplexity(context, cancellationToken);
    }

    public SymbolicCapabilityResult QueryCapabilities(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return _executor.QueryCapabilities(context, cancellationToken);
    }

    public SymbolicOperationResult<SymbolicCapabilityResult> TryQueryCapabilities(
        SymbolicQueryContext context,
        CancellationToken cancellationToken = default)
    {
        return _executor.TryQueryCapabilities(context, cancellationToken);
    }
}
