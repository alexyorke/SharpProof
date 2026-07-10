using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic;

internal static class CSharpSyntaxFacts
{
    public static bool IsNestedCallableBoundary(SyntaxNode node)
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

    public static bool TryGetListPatternElementPosition(
        ListPatternSyntax listPattern,
        int patternIndex,
        out int elementIndex,
        out bool fromEnd)
    {
        elementIndex = 0;
        fromEnd = false;

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