using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic;

internal static class CSharpSyntaxFacts
{
    public static IEnumerable<SyntaxNode> DescendantNodesInExecution(
        SyntaxNode root,
        bool includeSelf = true,
        bool includeNestedCallables = false)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));

        bool DescendIntoChildren(SyntaxNode candidate)
        {
            return includeNestedCallables ||
                   ReferenceEquals(candidate, root) ||
                   !IsNestedLocalCallableBoundary(candidate);
        }

        return includeSelf
            ? root.DescendantNodesAndSelf(descendIntoTrivia: false, descendIntoChildren: DescendIntoChildren)
            : root.DescendantNodes(descendIntoTrivia: false, descendIntoChildren: DescendIntoChildren);
    }

    public static bool IsNestedLocalCallableBoundary(SyntaxNode node)
    {
        return node is AnonymousFunctionExpressionSyntax ||
               node is LocalFunctionStatementSyntax;
    }

    public static bool IsCallableBoundary(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax or
            ConstructorDeclarationSyntax or
            DestructorDeclarationSyntax or
            OperatorDeclarationSyntax or
            ConversionOperatorDeclarationSyntax or
            AccessorDeclarationSyntax or
            LocalFunctionStatementSyntax or
            AnonymousFunctionExpressionSyntax;
    }

    public static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }

    public static bool TryGetExpressionBody(SyntaxNode node, out ExpressionSyntax expression)
    {
        expression = node switch
        {
            MethodDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } body } => body,
            ConstructorDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            OperatorDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            ConversionOperatorDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            AccessorDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            PropertyDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            IndexerDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            _ => null!
        };
        return expression != null;
    }

    public static BlockSyntax? GetBlockBody(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => method.Body,
            LocalFunctionStatementSyntax local => local.Body,
            ConstructorDeclarationSyntax constructor => constructor.Body,
            DestructorDeclarationSyntax destructor => destructor.Body,
            OperatorDeclarationSyntax op => op.Body,
            ConversionOperatorDeclarationSyntax conversion => conversion.Body,
            AccessorDeclarationSyntax accessor => accessor.Body,
            _ => null
        };
    }

    public static bool IsThrowOnlyStatement(StatementSyntax statement)
    {
        return statement is ThrowStatementSyntax ||
               statement is BlockSyntax { Statements.Count: 1 } block &&
               block.Statements[0] is ThrowStatementSyntax;
    }

    public static bool IsNullLiteral(ExpressionSyntax expression)
    {
        return UnwrapParentheses(expression).IsKind(SyntaxKind.NullLiteralExpression);
    }

    public static bool TryGetNullPatternPolarity(PatternSyntax pattern, out bool matchesNonNull)
    {
        if (pattern is ConstantPatternSyntax { Expression: var expression } && IsNullLiteral(expression))
        {
            matchesNonNull = false;
            return true;
        }

        if (pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.IsKind(SyntaxKind.NotPattern) &&
            TryGetNullPatternPolarity(unaryPattern.Pattern, out var nestedMatchesNonNull))
        {
            matchesNonNull = !nestedMatchesNonNull;
            return true;
        }

        matchesNonNull = false;
        return false;
    }

    public static SyntaxNode GetContainingExecutionRoot(SyntaxNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        return node.AncestorsAndSelf().FirstOrDefault(static ancestor =>
                   ancestor is BaseMethodDeclarationSyntax or
                       AccessorDeclarationSyntax or
                       LocalFunctionStatementSyntax or
                       AnonymousFunctionExpressionSyntax)
               ?? node.SyntaxTree.GetRoot();
    }

    public static IEnumerable<(BlockSyntax Block, StatementSyntax ContainingStatement)> EnumerateContainingBlocks(
        SyntaxNode node,
        bool stopAtExecutionRoot = false)
    {
        var executionRoot = stopAtExecutionRoot ? GetContainingExecutionRoot(node) : null;
        for (var current = node; current != null; current = current.Parent)
        {
            if (current is StatementSyntax statement && statement.Parent is BlockSyntax block)
                yield return (block, statement);

            if (ReferenceEquals(current, executionRoot)) yield break;
        }
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

    public static bool TryGetListPatternElementPosition(
        ListPatternSyntax listPattern,
        int patternIndex,
        out int elementIndex,
        out bool fromEnd)
    {
        elementIndex = 0;
        fromEnd = false;

        if (patternIndex < 0 || patternIndex >= listPattern.Patterns.Count) return false;
        if (listPattern.Patterns[patternIndex] is SlicePatternSyntax) return false;

        var sliceIndex = -1;
        for (var index = 0; index < listPattern.Patterns.Count; index++)
            if (listPattern.Patterns[index] is SlicePatternSyntax)
            {
                sliceIndex = index;
                break;
            }

        if (sliceIndex < 0 || patternIndex < sliceIndex)
        {
            elementIndex = patternIndex;
            return true;
        }

        elementIndex = listPattern.Patterns.Count - patternIndex;
        fromEnd = true;
        return true;
    }

    public static IEnumerable<ExpressionSyntax> GetExplicitArraySizeExpressions(
        ArrayCreationExpressionSyntax arrayCreation)
    {
        foreach (var rankSpecifier in arrayCreation.Type.RankSpecifiers)
            foreach (var sizeExpression in rankSpecifier.Sizes)
                if (!sizeExpression.IsKind(SyntaxKind.OmittedArraySizeExpression))
                    yield return sizeExpression;
    }

    public static void GetListPatternLengthShape(
        ListPatternSyntax listPattern,
        out int minimumLength,
        out bool exactLength)
    {
        minimumLength = 0;
        exactLength = true;
        foreach (var subpattern in listPattern.Patterns)
        {
            if (subpattern is SlicePatternSyntax slicePattern)
            {
                if (TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                {
                    GetListPatternLengthShape(nestedListPattern, out var nestedMinimumLength,
                        out var nestedExactLength);
                    minimumLength += nestedMinimumLength;
                    exactLength &= nestedExactLength;
                }
                else
                {
                    exactLength = false;
                }

                continue;
            }

            minimumLength++;
        }
    }

    public static bool TryGetNestedListPattern(PatternSyntax? pattern, out ListPatternSyntax listPattern)
    {
        while (pattern is ParenthesizedPatternSyntax parenthesizedPattern) pattern = parenthesizedPattern.Pattern;

        if (pattern is ListPatternSyntax candidate)
        {
            listPattern = candidate;
            return true;
        }

        listPattern = null!;
        return false;
    }

    public static ExpressionSyntax UnwrapConditionExpression(ExpressionSyntax expression)
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
