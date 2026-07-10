using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    private static bool TryLowerIdentityPreservingAsTerm(
        BinaryExpressionSyntax asExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (asExpression.Right is not TypeSyntax targetTypeSyntax ||
            !IsIdentityPreservingReferenceConversion(asExpression.Left, targetTypeSyntax, context) ||
            !TryLowerTerm(asExpression.Left, context, out var operand) ||
            operand.Kind != SmtValueKind.Reference)
            return false;

        term = operand;
        return true;
    }

    private static bool IsIdentityPreservingReferenceConversion(
        ExpressionSyntax expression,
        TypeSyntax targetTypeSyntax,
        SymbolicLoweringContext context)
    {
        var sourceType = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        var targetType = context.SemanticModel.GetTypeInfo(targetTypeSyntax, context.CancellationToken).Type;
        if (sourceType == null ||
            targetType == null ||
            !sourceType.IsReferenceType ||
            !targetType.IsReferenceType)
            return false;

        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType) ||
            targetType.SpecialType == SpecialType.System_Object)
            return true;

        if (sourceType is INamedTypeSymbol sourceNamedType)
            for (var current = sourceNamedType.BaseType; current != null; current = current.BaseType)
                if (SymbolEqualityComparer.Default.Equals(current, targetType))
                    return true;

        foreach (var candidate in sourceType.AllInterfaces)
            if (SymbolEqualityComparer.Default.Equals(candidate, targetType))
                return true;

        return false;
    }

    private static bool TryLowerSupportedConversionTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        if (expression is CheckedExpressionSyntax checkedExpression &&
            checkedExpression.IsKind(SyntaxKind.UncheckedExpression))
        {
            if (checkedExpression.Expression is CastExpressionSyntax)
                return TryLowerSupportedConversionTerm(checkedExpression.Expression, context, out term);

            term = null!;
            return false;
        }

        if (expression is CastExpressionSyntax castExpression)
        {
            if (IsIdentityPreservingReferenceConversion(castExpression.Expression, castExpression.Type, context) &&
                TryLowerTerm(castExpression.Expression, context, out var referenceOperand) &&
                referenceOperand.Kind == SmtValueKind.Reference)
            {
                term = referenceOperand;
                return true;
            }

            var sourceType = context.SemanticModel.GetTypeInfo(castExpression.Expression, context.CancellationToken)
                .Type;
            var targetType = context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type;
            if (sourceType?.TypeKind == TypeKind.Enum &&
                sourceType is INamedTypeSymbol { EnumUnderlyingType.SpecialType: SpecialType.System_Int32 } &&
                targetType?.SpecialType == SpecialType.System_Int32 &&
                TryLowerTerm(castExpression.Expression, context, out var operand) &&
                operand.Kind == SmtValueKind.Int)
            {
                term = operand;
                return true;
            }
        }

        term = null!;
        return false;
    }
}