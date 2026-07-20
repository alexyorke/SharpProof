namespace SharpProof.Symbolic.Ir;

internal static class SymbolicAsyncLowerer {
    internal static bool TryGetKnownCompletedAsyncResultExpression(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out ExpressionSyntax resultExpression) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        switch (expression) {
            case AwaitExpressionSyntax awaitExpression:
                return TryGetKnownCompletedAwaitableResultExpression(
                    awaitExpression.Expression,
                    context,
                    out resultExpression);
            case MemberAccessExpressionSyntax memberAccess
                when context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is
                    IPropertySymbol { Name: "Result", IsStatic: false } property &&
                     TryGetTaskLikeResultType(property.ContainingType, out _):
                return TryGetKnownCompletedAwaitableResultExpression(
                    memberAccess.Expression,
                    context,
                    out resultExpression);
            case InvocationExpressionSyntax getResultInvocation
                when TryGetAwaiterSourceExpression(
                    getResultInvocation,
                    context,
                    out var awaitableExpression):
                return TryGetKnownCompletedAwaitableResultExpression(
                    awaitableExpression,
                    context,
                    out resultExpression);
            default:
                resultExpression = null!;
                return false;
        }
    }

    private static bool TryGetKnownCompletedAwaitableResultExpression(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out ExpressionSyntax resultExpression) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is InvocationExpressionSyntax fromResultInvocation &&
            context.SemanticModel.GetOperation(fromResultInvocation, context.CancellationToken) is
                IInvocationOperation fromResultOperation &&
            IsKnownFromResultFactory(fromResultOperation.TargetMethod) &&
            fromResultInvocation.ArgumentList.Arguments.Count == 1) {
            resultExpression = fromResultInvocation.ArgumentList.Arguments[0].Expression;
            return true;
        }

        if (expression is ObjectCreationExpressionSyntax valueTaskCreation &&
            context.SemanticModel.GetOperation(valueTaskCreation, context.CancellationToken) is
                IObjectCreationOperation creationOperation &&
            TryGetTaskLikeResultType(creationOperation.Type, out var resultType) &&
            creationOperation.Type is INamedTypeSymbol { MetadataName: "ValueTask`1" } &&
            valueTaskCreation.ArgumentList?.Arguments.Count == 1) {
            var argumentExpression = valueTaskCreation.ArgumentList.Arguments[0].Expression;
            var parameterType = creationOperation.Constructor?.Parameters.SingleOrDefault()?.Type;
            if (parameterType != null &&
                SymbolEqualityComparer.Default.Equals(parameterType, resultType)) {
                resultExpression = argumentExpression;
                return true;
            }

            if (TryGetTaskLikeResultType(parameterType, out var nestedResultType) &&
                SymbolEqualityComparer.Default.Equals(nestedResultType, resultType))
                return TryGetKnownCompletedAwaitableResultExpression(
                    argumentExpression,
                    context,
                    out resultExpression);
        }

        resultExpression = null!;
        return false;
    }

    private static bool TryGetAwaiterSourceExpression(
        InvocationExpressionSyntax getResultInvocation,
        SymbolicLoweringContext context,
        out ExpressionSyntax awaitableExpression) {
        awaitableExpression = null!;
        if (context.SemanticModel.GetOperation(getResultInvocation, context.CancellationToken) is not
                IInvocationOperation {
                    TargetMethod: { Name: "GetResult", Parameters.Length: 0 }
                } ||
            getResultInvocation.Expression is not MemberAccessExpressionSyntax {
                Expression: InvocationExpressionSyntax getAwaiterInvocation
            } ||
            context.SemanticModel.GetOperation(getAwaiterInvocation, context.CancellationToken) is not
                IInvocationOperation {
                    TargetMethod: { Name: "GetAwaiter", Parameters.Length: 0 }
                } ||
            getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterMember ||
            !TryGetTaskLikeResultType(
                context.SemanticModel.GetTypeInfo(getAwaiterMember.Expression, context.CancellationToken).Type,
                out _))
            return false;

        awaitableExpression = getAwaiterMember.Expression;
        return true;
    }

    private static bool IsKnownFromResultFactory(IMethodSymbol method) {
        if (!method.IsStatic ||
            method.Name != "FromResult" ||
            method.Parameters.Length != 1 ||
            !TryGetTaskLikeResultType(method.ReturnType, out _))
            return false;

        var containingType = method.ContainingType;
        return containingType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
               containingType.MetadataName is "Task" or "ValueTask";
    }

    private static bool TryGetTaskLikeResultType(ITypeSymbol? type, out ITypeSymbol resultType) {
        if (type is INamedTypeSymbol {
            TypeArguments.Length: 1,
            ContainingNamespace: { } containingNamespace
        } namedType &&
            containingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
            namedType.MetadataName is "Task`1" or "ValueTask`1") {
            resultType = namedType.TypeArguments[0];
            return true;
        }

        resultType = null!;
        return false;
    }
}
