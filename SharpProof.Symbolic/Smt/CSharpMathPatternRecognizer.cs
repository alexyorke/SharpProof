using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Symbolic.Smt
{
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
            if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod.Name != "Abs" ||
                !invocationOperation.TargetMethod.IsStatic ||
                invocationOperation.TargetMethod.ContainingType?.ToDisplayString() != "System.Math" ||
                invocationOperation.TargetMethod.Parameters.Length != 1 ||
                !IsIntegralOrEnumType(invocationOperation.TargetMethod.ReturnType) ||
                !TryGetInvocationArgumentExpression(invocationOperation, parameterIndex: 0, out var argumentExpression))
            {
                return false;
            }

            argumentExpression = UnwrapExpression(argumentExpression);
            if (argumentExpression is not BinaryExpressionSyntax remainderExpression ||
                !remainderExpression.IsKind(SyntaxKind.ModuloExpression) ||
                !HasSupportedIntegralType(remainderExpression.Left, semanticModel, cancellationToken) ||
                !HasSupportedIntegralType(remainderExpression.Right, semanticModel, cancellationToken))
            {
                return false;
            }

            dividendExpression = remainderExpression.Left;
            divisorExpression = remainderExpression.Right;
            return true;
        }

        private static bool TryGetInvocationArgumentExpression(
            IInvocationOperation invocationOperation,
            int parameterIndex,
            out ExpressionSyntax expression)
        {
            expression = null!;
            if (parameterIndex < 0 ||
                parameterIndex >= invocationOperation.TargetMethod.Parameters.Length)
            {
                return false;
            }

            var parameter = invocationOperation.TargetMethod.Parameters[parameterIndex];
            foreach (var argument in invocationOperation.Arguments)
            {
                if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression)
                {
                    expression = argumentExpression;
                    return true;
                }
            }

            if (parameterIndex < invocationOperation.Arguments.Length &&
                invocationOperation.Arguments[parameterIndex].Value.Syntax is ExpressionSyntax fallbackExpression)
            {
                expression = fallbackExpression;
                return true;
            }

            return false;
        }

        private static bool HasSupportedIntegralType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            return type != null &&
                (IsIntegralOrEnumType(type) ||
                    type is INamedTypeSymbol namedType &&
                    namedType.ToDisplayString() == "System.Numerics.BigInteger");
        }

        private static bool IsIntegralOrEnumType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType is
                    SpecialType.System_Char or
                    SpecialType.System_SByte or
                    SpecialType.System_Byte or
                    SpecialType.System_Int16 or
                    SpecialType.System_UInt16 or
                    SpecialType.System_Int32 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64 ||
                typeSymbol.TypeKind == TypeKind.Enum;
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
                {
                    expression = parenthesizedExpression.Expression;
                    continue;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                if (expression is CheckedExpressionSyntax checkedExpression &&
                    checkedExpression.IsKind(SyntaxKind.CheckedExpression))
                {
                    expression = checkedExpression.Expression;
                    continue;
                }

                return expression;
            }
        }
    }
}
