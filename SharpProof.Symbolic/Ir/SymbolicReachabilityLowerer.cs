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
        if (!TryResolveConditionalInvocation(
                condition,
                semanticModel,
                cancellationToken,
                out var invocation,
                out var aliasNegated) ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return false;
        if (aliasNegated)
            methodReturnValue = !methodReturnValue;
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
                    out var target) ||
                IsMutatedBetween(
                    target,
                    invocation,
                    condition,
                    semanticModel,
                    cancellationToken))
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
    private static bool TryResolveConditionalInvocation(
        ExpressionSyntax condition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out InvocationExpressionSyntax invocation,
        out bool negated) {
        condition = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition);
        negated = false;
        if (condition is InvocationExpressionSyntax directInvocation) {
            invocation = directInvocation;
            return true;
        }
        if (semanticModel.GetSymbolInfo(condition, cancellationToken).Symbol is not ILocalSymbol local ||
            local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not
                VariableDeclaratorSyntax { Initializer.Value: { } initializer }) {
            invocation = null!;
            return false;
        }
        var initializerExpression =
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(initializer);
        if (initializerExpression is PrefixUnaryExpressionSyntax logicalNot &&
            logicalNot.IsKind(SyntaxKind.LogicalNotExpression)) {
            initializerExpression =
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(logicalNot.Operand);
            negated = true;
        }
        if (initializerExpression is not InvocationExpressionSyntax aliasedInvocation ||
            !ReferenceEquals(aliasedInvocation.SyntaxTree, condition.SyntaxTree) ||
            aliasedInvocation.Span.End > condition.SpanStart ||
            IsMutatedBetween(local, aliasedInvocation, condition, semanticModel, cancellationToken)) {
            invocation = null!;
            negated = false;
            return false;
        }
        invocation = aliasedInvocation;
        return true;
    }
    private static bool IsMutatedBetween(
        ISymbol symbol,
        SyntaxNode origin,
        ExpressionSyntax condition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(condition);
        var repeatedLoop = GetRepeatedLoop(condition);
        return CSharpSyntaxFacts.DescendantNodesInExecution(
                executionRoot,
                includeNestedCallables: true)
            .Where(node =>
                node.SpanStart >= origin.Span.End && node.Span.End <= condition.SpanStart ||
                IsInsideRepeatedLoopMutationRegion(node, repeatedLoop) ||
                IsInsideNestedCallable(node, executionRoot))
            .Any(node =>
                SymbolMutationFacts.TryGetMutationTarget(node, out var target) &&
                SymbolMutationFacts.ExpressionMatchesSymbol(
                    target,
                    symbol,
                    semanticModel,
                    cancellationToken));
    }
    private static bool IsInsideNestedCallable(SyntaxNode node, SyntaxNode executionRoot) =>
        node.Ancestors()
            .TakeWhile(ancestor => !ReferenceEquals(ancestor, executionRoot))
            .Any(CSharpSyntaxFacts.IsNestedLocalCallableBoundary);
    private static StatementSyntax? GetRepeatedLoop(ExpressionSyntax condition) {
        foreach (var ancestor in condition.Ancestors())
            switch (ancestor) {
                case WhileStatementSyntax whileStatement
                    when whileStatement.Condition.Span.Contains(condition.Span):
                    return whileStatement;
                case DoStatementSyntax doStatement
                    when doStatement.Condition.Span.Contains(condition.Span):
                    return doStatement;
                case ForStatementSyntax { Condition: { } forCondition } forStatement
                    when forCondition.Span.Contains(condition.Span):
                    return forStatement;
            }
        return null;
    }
    private static bool IsInsideRepeatedLoopMutationRegion(SyntaxNode node, StatementSyntax? loop) =>
        loop switch {
            WhileStatementSyntax whileStatement => whileStatement.Statement.Span.Contains(node.Span),
            DoStatementSyntax doStatement => doStatement.Statement.Span.Contains(node.Span),
            ForStatementSyntax forStatement =>
                forStatement.Statement.Span.Contains(node.Span) ||
                forStatement.Incrementors.Any(incrementor => incrementor.Span.Contains(node.Span)),
            _ => false
        };
}
