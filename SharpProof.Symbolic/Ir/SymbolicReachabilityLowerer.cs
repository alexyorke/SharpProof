namespace SharpProof.Symbolic.Ir;

internal static class SymbolicReachabilityLowerer {
    internal static bool Apply(
        ref SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        condition = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition);
        var appliedOutputContract = ApplyConditionalOutputContracts(
            ref state,
            condition,
            branchWhenTrue,
            semanticModel,
            cancellationToken);
        var appliedCondition = ApplyConditionOnly(
            ref state,
            condition,
            branchWhenTrue,
            semanticModel,
            cancellationToken);
        if (appliedOutputContract && !appliedCondition)
            state = state.Normalize();
        return appliedCondition || appliedOutputContract;
    }
    internal static bool ApplyConditionOnly(
        ref SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var lowering = SymbolicSemanticPipeline.LowerBranchCondition(
            condition,
            branchWhenTrue,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } branch })
            return false;
        state = state.AddPathCondition(branch).Normalize();
        return true;
    }
    private static bool ApplyConditionalOutputContracts(
        ref SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var methodReturnValue = branchWhenTrue;
        if (condition is PrefixUnaryExpressionSyntax logicalNot &&
            logicalNot.IsKind(SyntaxKind.LogicalNotExpression)) {
            condition = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(logicalNot.Operand);
            methodReturnValue = !methodReturnValue;
        }
        if (condition is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return false;
        var applied = false;
        foreach (var argument in operation.Arguments) {
            if (argument is not
                {
                    ArgumentKind: ArgumentKind.Explicit,
                    Parameter: { RefKind: RefKind.Ref or RefKind.Out } parameter,
                    Syntax: ArgumentSyntax syntax
                } ||
                !SymbolicFrameworkPostconditionLowerer.ArgumentRefKindMatches(parameter, syntax) ||
                !SymbolicFrameworkPostconditionLowerer.IsUniqueOutputArgumentTarget(
                    operation,
                    argument,
                    semanticModel,
                    cancellationToken) ||
                !NullableFlowFacts.TryGetArgumentTargetSymbol(
                    syntax.Expression,
                    semanticModel,
                    cancellationToken,
                    out var target))
                continue;
            state = SymbolicStateValueFacts.RemoveReferences(state, target);
            applied = true;
            if (NullableFlowFacts.GetParameterOutputState(parameter, methodReturnValue) !=
                    NullableFlowFactState.NotNull ||
                !SymbolicStateFactBuilder.TryCreateSymbolTerm(target, out var term) ||
                term.Kind != SmtValueKind.Reference)
                continue;
            state = state.AddPathCondition(SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                term,
                new SymbolicNullTerm(),
                syntax.Expression,
                "ir.path.branch.parameter-not-null"));
        }
        return applied;
    }
}
