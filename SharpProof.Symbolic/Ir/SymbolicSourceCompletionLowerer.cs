namespace SharpProof.Symbolic.Ir;
internal static class SymbolicSourceCompletionLowerer {
    internal static SymbolicState ApplyNormalCompletion(
        SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var frameworkLowering = SymbolicFrameworkPostconditionLowerer.Lower(expression, statement, semanticModel, cancellationToken);
        if (frameworkLowering is { IsExact: true, Value: { } frameworkPlan })
            state = ApplyConditions(state, frameworkPlan.BeforeDoesNotReturnIf);
        foreach (var (_, _, parameter, argumentSyntax) in
                 SymbolicFrameworkPostconditionLowerer.EnumerateExplicitInvocationArguments(expression, semanticModel, cancellationToken)) {
            if (parameter.RefKind != RefKind.None ||
                !NullableFlowFacts.TryGetDoesNotReturnIfValue(parameter, out var doesNotReturnWhen) ||
                !argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.None) ||
                SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                    argumentSyntax.Expression,
                    statement,
                    semanticModel,
                    cancellationToken))
                continue;
            SymbolicReachabilityLowerer.Apply(
                ref state, argumentSyntax.Expression, !doesNotReturnWhen, semanticModel, cancellationToken);
        }
        if (frameworkLowering is { IsExact: true, Value: { } afterPlan })
            state = ApplyConditions(state, afterPlan.AfterDoesNotReturnIf);
        return state.Normalize();
    }
    private static SymbolicState ApplyConditions(
        SymbolicState state,
        IReadOnlyList<SymbolicCondition> conditions) =>
        conditions.Count == 0
            ? state
            : SymbolicOperationTransferKernel.AssumeAll(state, conditions);
}
