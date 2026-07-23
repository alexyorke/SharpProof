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
            ? SymbolicOperationTransferKernel.Assume(
                state,
                branch,
                assumeTrue: true,
                condition.Span,
                "compiler-flow.branch")
            : SymbolicOperationTransitionResult.Unsupported(
                state,
                SymbolicUnknownReason.UnsupportedIrEncoding,
                [new SymbolicLoweringProvenance("compiler-flow.branch", condition.Span, "unsupported")]);
    }
}
