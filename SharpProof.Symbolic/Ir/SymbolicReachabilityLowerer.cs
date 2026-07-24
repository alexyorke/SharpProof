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
        if (!TryResolveConditionalInvocation(
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                out var invocations))
            return false;
        var applied = false;
        foreach (var resolved in invocations) {
            if (semanticModel.GetOperation(resolved.Invocation, cancellationToken) is not IInvocationOperation operation)
                continue;
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
                        resolved.Invocation,
                        condition,
                        semanticModel,
                        cancellationToken))
                    continue;
                state = SymbolicStateValueFacts.RemoveReferences(state, target);
                applied = true;
                if (NullableFlowFacts.GetParameterOutputState(parameter, resolved.ReturnValue) !=
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
        }
        return applied;
    }
    private readonly record struct ConditionalInvocation(
        InvocationExpressionSyntax Invocation,
        bool ReturnValue);
    private static bool TryResolveConditionalInvocation(
        ExpressionSyntax condition,
        bool conditionValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IReadOnlyList<ConditionalInvocation> invocations) => TryResolveConditionalInvocation(
            condition,
            conditionValue,
            semanticModel,
            cancellationToken,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            out invocations);
    private static bool TryResolveConditionalInvocation(
        ExpressionSyntax expression,
        bool expressionValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        HashSet<ISymbol> visitedLocals,
        out IReadOnlyList<ConditionalInvocation> invocations) {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        invocations = [];
        if (expression is PrefixUnaryExpressionSyntax logicalNot &&
            logicalNot.IsKind(SyntaxKind.LogicalNotExpression)) {
            return TryResolveConditionalInvocation(
                logicalNot.Operand,
                !expressionValue,
                semanticModel,
                cancellationToken,
                visitedLocals,
                out invocations);
        }
        if (TryGetIdentityBooleanConversionOperand(
                expression,
                semanticModel,
                cancellationToken,
                out var convertedOperand)) {
            return TryResolveConditionalInvocation(
                convertedOperand,
                expressionValue,
                semanticModel,
                cancellationToken,
                visitedLocals,
                out invocations);
        }
        if (TryGetBooleanWrapperOperand(
                expression,
                semanticModel,
                cancellationToken,
                out var comparedOperand,
                out var comparisonNegated)) {
            return TryResolveConditionalInvocation(
                comparedOperand,
                expressionValue ^ comparisonNegated,
                semanticModel,
                cancellationToken,
                visitedLocals,
                out invocations);
        }
        if (TryGetImpliedConditionalOperand(
                expression,
                expressionValue,
                semanticModel,
                cancellationToken,
                out var conditionalOperand)) {
            return TryResolveConditionalInvocation(
                conditionalOperand,
                expressionValue,
                semanticModel,
                cancellationToken,
                visitedLocals,
                out invocations);
        }
        if (TryGetImpliedBooleanOperandValue(expression, expressionValue, semanticModel, cancellationToken,
                out var impliedValue) &&
            expression is BinaryExpressionSyntax logical) {
            var resolved = new List<ConditionalInvocation>();
            var leftVisited = new HashSet<ISymbol>(visitedLocals, SymbolEqualityComparer.Default);
            if (TryResolveConditionalInvocation(
                    logical.Left,
                    impliedValue,
                    semanticModel,
                    cancellationToken,
                    leftVisited,
                    out var leftInvocations))
                resolved.AddRange(leftInvocations);
            if (TryResolveConditionalInvocation(
                    logical.Right,
                    impliedValue,
                    semanticModel,
                    cancellationToken,
                    visitedLocals,
                    out var rightInvocations))
                resolved.AddRange(rightInvocations);
            invocations = resolved;
            return resolved.Count > 0;
        }
        if (expression is InvocationExpressionSyntax directInvocation) {
            invocations = [new ConditionalInvocation(directInvocation, expressionValue)];
            return true;
        }
        if (semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not ILocalSymbol local ||
            !visitedLocals.Add(local) ||
            !SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                local,
                expression,
                semanticModel,
                cancellationToken,
                out var valueExpression)) {
            return false;
        }
        valueExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression);
        if (!ReferenceEquals(valueExpression.SyntaxTree, expression.SyntaxTree) ||
            valueExpression.Span.End > expression.SpanStart ||
            IsMutatedBetween(local, valueExpression, expression, semanticModel, cancellationToken) ||
            !TryResolveConditionalInvocation(
                valueExpression,
                expressionValue,
                semanticModel,
                cancellationToken,
                visitedLocals,
                out invocations)) {
            invocations = [];
            return false;
        }
        return true;
    }
    private static bool TryGetIdentityBooleanConversionOperand(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax operand) {
        operand = null!;
        if (semanticModel.GetOperation(expression, cancellationToken) is not IConversionOperation conversion ||
            !conversion.Conversion.IsIdentity ||
            conversion.OperatorMethod != null ||
            conversion.Type?.SpecialType != SpecialType.System_Boolean ||
            conversion.Operand.Type?.SpecialType != SpecialType.System_Boolean ||
            conversion.Operand.Syntax is not ExpressionSyntax operandSyntax)
            return false;
        operand = operandSyntax;
        return true;
    }
    private static bool TryGetImpliedConditionalOperand(
        ExpressionSyntax expression,
        bool expressionValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax operand) {
        operand = null!;
        if (expression is not ConditionalExpressionSyntax conditional ||
            CSharpSyntaxFacts.GetExpressionType(
                conditional,
                semanticModel,
                cancellationToken)?.SpecialType != SpecialType.System_Boolean)
            return false;
        if (semanticModel.GetConstantValue(conditional.WhenFalse, cancellationToken) is { HasValue: true, Value: bool falseArmValue } &&
            falseArmValue != expressionValue) {
            operand = conditional.WhenTrue;
            return true;
        }
        if (semanticModel.GetConstantValue(conditional.WhenTrue, cancellationToken) is { HasValue: true, Value: bool trueArmValue } &&
            trueArmValue != expressionValue) {
            operand = conditional.WhenFalse;
            return true;
        }
        return false;
    }
    private static bool TryGetImpliedBooleanOperandValue(
        ExpressionSyntax expression,
        bool expressionValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool operandValue) {
        operandValue = false;
        if (expression is not BinaryExpressionSyntax binary ||
            semanticModel.GetOperation(binary, cancellationToken) is not
                IBinaryOperation { OperatorMethod: null, Type.SpecialType: SpecialType.System_Boolean })
            return false;
        var isAnd = binary.IsKind(SyntaxKind.LogicalAndExpression) ||
                    binary.IsKind(SyntaxKind.BitwiseAndExpression);
        var isOr = binary.IsKind(SyntaxKind.LogicalOrExpression) ||
                   binary.IsKind(SyntaxKind.BitwiseOrExpression);
        if (isAnd && expressionValue) {
            operandValue = true;
            return true;
        }
        if (isOr && !expressionValue) {
            operandValue = false;
            return true;
        }
        return false;
    }
    private static bool TryGetBooleanWrapperOperand(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax operand,
        out bool negated) {
        operand = null!;
        negated = false;
        if (expression is IsPatternExpressionSyntax isPattern &&
            CSharpSyntaxFacts.GetExpressionType(
                isPattern.Expression,
                semanticModel,
                cancellationToken)?.SpecialType == SpecialType.System_Boolean &&
            TryGetBooleanPatternValue(
                isPattern.Pattern,
                semanticModel,
                cancellationToken,
                out var patternValue)) {
            operand = isPattern.Expression;
            negated = !patternValue;
            return true;
        }
        if (expression is not BinaryExpressionSyntax binary ||
            semanticModel.GetOperation(binary, cancellationToken) is not
                IBinaryOperation { OperatorMethod: null, Type.SpecialType: SpecialType.System_Boolean })
            return false;
        bool constant;
        if (semanticModel.GetConstantValue(binary.Left, cancellationToken) is { HasValue: true, Value: bool leftConstant }) {
            operand = binary.Right;
            constant = leftConstant;
        }
        else if (semanticModel.GetConstantValue(binary.Right, cancellationToken) is { HasValue: true, Value: bool rightConstant }) {
            operand = binary.Left;
            constant = rightConstant;
        }
        else
            return false;
        if (binary.IsKind(SyntaxKind.EqualsExpression)) {
            negated = !constant;
            return true;
        }
        if (binary.IsKind(SyntaxKind.NotEqualsExpression) ||
            binary.IsKind(SyntaxKind.ExclusiveOrExpression)) {
            negated = constant;
            return true;
        }
        if ((binary.IsKind(SyntaxKind.LogicalAndExpression) ||
             binary.IsKind(SyntaxKind.BitwiseAndExpression)) &&
            constant)
            return true;
        if ((binary.IsKind(SyntaxKind.LogicalOrExpression) ||
             binary.IsKind(SyntaxKind.BitwiseOrExpression)) &&
            !constant)
            return true;
        operand = null!;
        return false;
    }
    private static bool TryGetBooleanPatternValue(
        PatternSyntax pattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool value) {
        if (pattern is ParenthesizedPatternSyntax parenthesized)
            return TryGetBooleanPatternValue(
                parenthesized.Pattern,
                semanticModel,
                cancellationToken,
                out value);
        if (pattern is UnaryPatternSyntax unary &&
            unary.IsKind(SyntaxKind.NotPattern) &&
            TryGetBooleanPatternValue(
                unary.Pattern,
                semanticModel,
                cancellationToken,
                out var nestedValue)) {
            value = !nestedValue;
            return true;
        }
        if (pattern is ConstantPatternSyntax constant &&
            semanticModel.GetConstantValue(constant.Expression, cancellationToken) is { HasValue: true, Value: bool booleanValue }) {
            value = booleanValue;
            return true;
        }
        value = false;
        return false;
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
            .Any(node =>
                SymbolMutationFacts.TryGetMutationTarget(node, out var target) &&
                (node.SpanStart >= origin.Span.End && node.Span.End <= condition.SpanStart ||
                 IsInsideRepeatedLoopMutationRegion(node, repeatedLoop) ||
                 IsInsidePotentiallyPriorNestedCallable(node, executionRoot, condition)) &&
                SymbolMutationFacts.ExpressionMatchesSymbol(
                    target,
                    symbol,
                    semanticModel,
                    cancellationToken));
    }
    private static bool IsInsidePotentiallyPriorNestedCallable(
        SyntaxNode node,
        SyntaxNode executionRoot,
        ExpressionSyntax condition) {
        var boundaries = node.Ancestors()
            .TakeWhile(ancestor => !ReferenceEquals(ancestor, executionRoot))
            .Where(CSharpSyntaxFacts.IsNestedLocalCallableBoundary)
            .ToArray();
        return boundaries.Any(static boundary => boundary is LocalFunctionStatementSyntax) ||
               boundaries.Any(boundary =>
                   boundary is AnonymousFunctionExpressionSyntax &&
                   boundary.SpanStart < condition.SpanStart);
    }
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
