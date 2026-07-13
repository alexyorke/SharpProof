using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic;

internal static class SymbolMutationFacts
{
    internal static bool ContainsMutation(
        SyntaxNode root,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeSelf = true)
    {
        return CSharpSyntaxFacts.DescendantNodesInExecution(root, includeSelf)
            .Any(node => MutatesSymbol(node, symbol, semanticModel, cancellationToken));
    }

    internal static bool MutatesSymbol(
        SyntaxNode node,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return node switch
        {
            AssignmentExpressionSyntax assignment =>
                MutatedExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken),
            PrefixUnaryExpressionSyntax prefixUnary
                when prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) ||
                     prefixUnary.IsKind(SyntaxKind.PreDecrementExpression) =>
                ExpressionMatchesSymbol(prefixUnary.Operand, symbol, semanticModel, cancellationToken),
            PostfixUnaryExpressionSyntax postfixUnary
                when postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) ||
                     postfixUnary.IsKind(SyntaxKind.PostDecrementExpression) =>
                ExpressionMatchesSymbol(postfixUnary.Operand, symbol, semanticModel, cancellationToken),
            ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) =>
                ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken),
            _ => false
        };
    }

    internal static bool ExpressionReferencesSymbol(
        SyntaxNode root,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return CSharpSyntaxFacts.DescendantNodesInExecution(root)
            .OfType<ExpressionSyntax>()
            .Any(expression => ExpressionMatchesSymbol(
                expression,
                symbol,
                semanticModel,
                cancellationToken));
    }

    internal static bool ExpressionMatchesSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return TryGetLocalOrParameterSymbol(expression, semanticModel, cancellationToken, out var expressionSymbol) &&
               SymbolEqualityComparer.Default.Equals(expressionSymbol, symbol);
    }

    internal static bool TryGetLocalOrParameterSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol symbol)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        return SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
            expression,
            semanticModel,
            cancellationToken,
            out symbol);
    }

    private static bool MutatedExpressionMatchesSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is TupleExpressionSyntax tuple)
            return tuple.Arguments.Any(argument => MutatedExpressionMatchesSymbol(
                argument.Expression,
                symbol,
                semanticModel,
                cancellationToken));

        return ExpressionMatchesSymbol(expression, symbol, semanticModel, cancellationToken);
    }
}
