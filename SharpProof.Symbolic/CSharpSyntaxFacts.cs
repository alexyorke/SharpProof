using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic
{
    internal static class CSharpSyntaxFacts
    {
        public static bool IsNestedCallableBoundary(Microsoft.CodeAnalysis.SyntaxNode node)
        {
            return node is AnonymousFunctionExpressionSyntax ||
                node is LocalFunctionStatementSyntax;
        }

        internal static ExpressionSyntax UnwrapParenthesesAndNullableSuppression(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                return expression;
            }
        }
    }
}
