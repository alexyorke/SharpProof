namespace SharpProof.Symbolic.Ir;

internal sealed record SymbolicLoopTransferPlan(
    StatementSyntax Loop,
    SymbolicCondition EntryCondition,
    SymbolicCondition ExitCondition,
    ImmutableArray<SymbolicInvalidationTarget> BackEdgeInvalidations,
    ImmutableArray<SymbolicCondition> Invariants);

internal sealed record SymbolicLoopInvariantPlan(
    ImmutableArray<SymbolicCondition> Conditions);

internal static class SymbolicLoopTransferLowerer
{
    internal static SymbolicLoweringResult<SymbolicLoopTransferPlan> Lower(
        StatementSyntax loop,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool allowAbruptCompletion = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetCondition(loop, out var condition))
            return Unsupported(loop, "loop-kind");
        if (!allowAbruptCompletion &&
            CSharpSyntaxFacts.DescendantNodesInExecution(loop).Any(static node =>
                node is BreakStatementSyntax or ContinueStatementSyntax))
            return Unsupported(loop, "abrupt-completion");
        SymbolicCondition entryCondition;
        SymbolicCondition exitCondition;
        if (condition == null)
        {
            entryCondition = new SymbolicConstantCondition(true);
            exitCondition = new SymbolicConstantCondition(false);
        }
        else
        {
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            var entry = SymbolicSemanticPipeline.LowerBranchCondition(condition, true, context);
            var exit = SymbolicSemanticPipeline.LowerBranchCondition(condition, false, context);
            if (entry is not { IsExact: true, Value: { } exactEntry } ||
                exit is not { IsExact: true, Value: { } exactExit })
                return Unsupported(condition, "condition");
            entryCondition = exactEntry;
            exitCondition = exactExit;
        }

        if (!TryCollectBackEdgeInvalidations(
                loop,
                semanticModel,
                cancellationToken,
                out var invalidations))
            return Unsupported(loop, "invalidation");

        var invariantLowering = SymbolicLoopStateTransfer.LowerLoopBodyInvariants(
            loop,
            semanticModel,
            cancellationToken);
        if (invariantLowering is not { IsExact: true, Value: { } invariantPlan } &&
            !allowAbruptCompletion)
            return Unsupported(loop, "invariants");
        var invariants = invariantLowering is { IsExact: true, Value: { } exactInvariantPlan }
            ? exactInvariantPlan.Conditions
            : ImmutableArray<SymbolicCondition>.Empty;
        return SymbolicLoweringResult<SymbolicLoopTransferPlan>.Exact(
            new SymbolicLoopTransferPlan(
                loop,
                entryCondition,
                exitCondition,
                invalidations,
                invariants),
            Provenance(loop, "exact"));
    }

    private static bool TryGetCondition(
        StatementSyntax loop,
        out ExpressionSyntax? condition)
    {
        condition = loop switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Condition,
            DoStatementSyntax doStatement => doStatement.Condition,
            ForStatementSyntax forStatement => forStatement.Condition,
            _ => null
        };
        return condition != null || loop is ForStatementSyntax;
    }

    private static bool TryCollectBackEdgeInvalidations(
        StatementSyntax loop,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ImmutableArray<SymbolicInvalidationTarget> invalidations)
    {
        var targets = ImmutableArray.CreateBuilder<SymbolicInvalidationTarget>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in EnumerateBackEdgeMutationRoots(loop))
            if (!SymbolicMutationInventory.Create(root, semanticModel, cancellationToken)
                    .TryCollectLocalOrParameterInvalidations(keys, targets))
            {
                invalidations = default;
                return false;
            }

        invalidations = targets.ToImmutable();
        return true;
    }

    private static IEnumerable<SyntaxNode> EnumerateBackEdgeMutationRoots(StatementSyntax loop)
    {
        switch (loop)
        {
            case WhileStatementSyntax whileStatement:
                yield return whileStatement.Condition;
                yield return whileStatement.Statement;
                break;
            case DoStatementSyntax doStatement:
                yield return doStatement.Statement;
                yield return doStatement.Condition;
                break;
            case ForStatementSyntax forStatement:
                if (forStatement.Condition != null)
                    yield return forStatement.Condition;
                yield return forStatement.Statement;
                foreach (var incrementor in forStatement.Incrementors)
                    yield return incrementor;
                break;
        }
    }

    private static SymbolicLoweringResult<SymbolicLoopTransferPlan> Unsupported(
        SyntaxNode source,
        string detail) =>
        SymbolicLoweringResult<SymbolicLoopTransferPlan>.Unsupported(Provenance(source, detail));

    private static SymbolicLoweringProvenance Provenance(SyntaxNode source, string detail) =>
        new("cfg-loop", source.Span, detail);
}
