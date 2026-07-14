using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    internal static ExpressionSyntax UnwrapFactExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }

    internal static bool IsDefaultExpressionSyntax(ExpressionSyntax expression)
    {
        return expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
               expression is DefaultExpressionSyntax;
    }

}
