using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Symbolic.Smt;

internal static class CSharpMathPatternRecognizer
{
    internal static bool TryGetMathAbsRemainderOperands(
        InvocationExpressionSyntax invocationExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax dividendExpression,
        out ExpressionSyntax divisorExpression)
    {
        dividendExpression = null!;
        divisorExpression = null!;
        if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation
                invocationOperation ||
            invocationOperation.TargetMethod.Name != "Abs" ||
            !invocationOperation.TargetMethod.IsStatic ||
            invocationOperation.TargetMethod.ContainingType?.ToDisplayString() != "System.Math" ||
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
        CancellationToken cancellationToken)
    {
        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        return type != null &&
               (SymbolicTypeFacts.IsBuiltInIntegralOrEnumType(type) ||
                (type is INamedTypeSymbol namedType &&
                 namedType.ToDisplayString() == "System.Numerics.BigInteger"));
    }
}
