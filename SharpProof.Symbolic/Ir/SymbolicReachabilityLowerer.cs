namespace SharpProof.Symbolic.Ir;

internal static class SymbolicReachabilityLowerer {
    internal static SymbolicOperationTransitionResult Apply(
        SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        condition = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition);
        var lowering = SymbolicSemanticPipeline.LowerBranchCondition(
            condition,
            branchWhenTrue,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        return lowering is { IsExact: true, Value: { } branch }
            ? SymbolicOperationTransitionResult.Exact(state.AddPathCondition(branch))
            : SymbolicOperationTransitionResult.Unsupported(state);
    }
}
