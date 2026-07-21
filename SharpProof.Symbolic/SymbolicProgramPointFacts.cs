using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic;

internal static class SymbolicProgramPointFacts {
    internal static void AddReachabilityCondition(
        ref SymbolicState state,
        ExpressionSyntax condition,
        bool mustBeTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var transition = SymbolicReachabilityLowerer.Apply(
            state,
            condition,
            mustBeTrue,
            semanticModel,
            cancellationToken);
        if (transition.IsExact)
            state = transition.State;
    }

    internal static void AddReferenceNullCondition(
        ref SymbolicState state,
        ExpressionSyntax expression,
        bool isNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? provenance = null) {
        if (NullableFlowFacts.IsDefinitelyNullReferenceValue(expression, semanticModel, cancellationToken)) {
            if (!isNull)
                state = SymbolicOperationTransferKernel.Complete(state, expression.Span).State;

            return;
        }

        if (NullableFlowFacts.IsDefinitelyNotNullReferenceValue(expression, semanticModel, cancellationToken)) {
            if (isNull)
                state = SymbolicOperationTransferKernel.Complete(state, expression.Span).State;

            return;
        }

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        if (lowering is not { IsExact: true, Value: { } subject } ||
            subject.Kind != SmtValueKind.Reference)
            return;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                subject,
                new SymbolicNullTerm()),
            expression,
            provenance ?? (isNull ? "ir.path.reference-null" : "ir.path.reference-not-null"));
        state = state.AddPathCondition(new SymbolicFactCondition(fact));
    }

    internal static bool StatementInvalidatesSymbolValue(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        return SymbolicMutationInventory.Create(statement, semanticModel, cancellationToken)
            .InvalidatesSymbol(symbol, mutableExposures: true);
    }
}
