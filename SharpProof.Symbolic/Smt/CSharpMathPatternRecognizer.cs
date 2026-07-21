namespace SharpProof.Symbolic.Smt;

internal static class CSharpMathPatternRecognizer {
    internal static bool TryGetMathAbsRemainderOperands(
        InvocationExpressionSyntax invocationExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax dividendExpression,
        out ExpressionSyntax divisorExpression) {
        dividendExpression = null!;
        divisorExpression = null!;
        if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation
                invocationOperation ||
            !SymbolicKnownApiLowerer.IsMathAbs(invocationOperation.TargetMethod) ||
            !invocationOperation.TargetMethod.IsStatic ||
            invocationOperation.TargetMethod.Parameters.Length != 1 ||
            !SymbolicTypeFacts.IsBuiltInIntegralOrEnumType(invocationOperation.TargetMethod.ReturnType) ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var argumentExpression))
            return false;

        argumentExpression = CSharpSyntaxFacts.UnwrapConditionExpression(argumentExpression);
        if (argumentExpression is not BinaryExpressionSyntax remainderExpression ||
            !remainderExpression.IsKind(SyntaxKind.ModuloExpression) ||
            !HasSupportedIntegralType(remainderExpression.Left, semanticModel, cancellationToken) ||
            !HasSupportedIntegralType(remainderExpression.Right, semanticModel, cancellationToken))
            return false;

        dividendExpression = remainderExpression.Left;
        divisorExpression = remainderExpression.Right;
        return true;
    }

    private static bool HasSupportedIntegralType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        return SymbolicTypeFacts.IsBuiltInIntegralOrEnumType(type) ||
               SymbolicTypeFacts.IsBigIntegerType(type);
    }
}
