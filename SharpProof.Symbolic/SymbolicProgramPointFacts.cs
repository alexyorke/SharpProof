namespace SharpProof.Symbolic;
internal static class SymbolicProgramPointFacts {
    internal static void AddReachabilityCondition(
        ref SymbolicState state,
        ExpressionSyntax condition,
        bool mustBeTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var transition = SymbolicReachabilityLowerer.Apply(state, condition, mustBeTrue, semanticModel, cancellationToken);
        if (transition.IsExact)
            state = transition.State;
    }
    internal static bool StatementInvalidatesSymbolValue(
        StatementSyntax statement,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) => SymbolicMutationInventory.Create(statement, semanticModel, cancellationToken)
            .InvalidatesSymbol(symbol, mutableExposures: true);
}
