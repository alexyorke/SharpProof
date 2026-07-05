using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static bool TryGetStableVariableSymbol(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out ISymbol symbol)
        {
            if (expression is IdentifierNameSyntax)
            {
                symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol!;
                return symbol is ILocalSymbol or IParameterSymbol;
            }

            symbol = null!;
            return false;
        }

        private static bool TryGetIntegralConstant(object value, out long result)
        {
            try
            {
                switch (value)
                {
                    case char charValue:
                        result = charValue;
                        return true;
                    case sbyte sbyteValue:
                        result = sbyteValue;
                        return true;
                    case byte byteValue:
                        result = byteValue;
                        return true;
                    case short shortValue:
                        result = shortValue;
                        return true;
                    case ushort ushortValue:
                        result = ushortValue;
                        return true;
                    case int intValue:
                        result = intValue;
                        return true;
                    case uint uintValue:
                        result = uintValue;
                        return true;
                    case long longValue:
                        result = longValue;
                        return true;
                    case ulong ulongValue when ulongValue <= long.MaxValue:
                        result = (long)ulongValue;
                        return true;
                }
            }
            catch (OverflowException)
            {
            }

            result = 0;
            return false;
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        continue;
                    case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                        expression = postfix.Operand;
                        continue;
                    case CastExpressionSyntax castExpression
                        when castExpression.Type is NullableTypeSyntax:
                        expression = castExpression.Expression;
                        continue;
                    default:
                        return expression;
                }
            }
        }
    }
}
