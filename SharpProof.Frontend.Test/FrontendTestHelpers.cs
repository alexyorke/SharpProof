using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Frontend.Test;

internal static class FrontendTestHelpers
{
    internal static string WrapSubjectMembers(string members)
    {
        return """
            #nullable enable
            public static class Subject {
            """ +
            Environment.NewLine +
            members +
            Environment.NewLine +
            "}";
    }

    internal static IOperation? TryGetExpressionOperation(
        SemanticModel model,
        ExpressionSyntax expression)
    {
        var operation = model.GetOperation(expression);
        if (operation != null)
        {
            return operation;
        }

        return expression switch
        {
            CheckedExpressionSyntax checkedExpression =>
                TryGetExpressionOperation(model, checkedExpression.Expression),
            ParenthesizedExpressionSyntax parenthesized =>
                TryGetExpressionOperation(model, parenthesized.Expression),
            _ => null
        };
    }
}
