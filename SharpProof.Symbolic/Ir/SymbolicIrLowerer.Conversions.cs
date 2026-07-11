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
            if (sourceType != null &&
                targetType != null &&
                IsValuePreservingIntegralConversion(sourceType, targetType) &&
                TryLowerTerm(castExpression.Expression, context, out var operand) &&
                operand.Kind == SmtValueKind.Int)
            {
                term = operand;
                return true;
            }

            if (sourceType != null &&
                targetType != null &&
                TryCreateNumericConversionTerm(
                    castExpression,
                    sourceType,
                    targetType,
                    context,
                    out term))
                return true;
        }

        term = null!;
        return false;
    }

    private static bool TryCreateNumericConversionTerm(
        CastExpressionSyntax castExpression,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (!IsNumericSpecialType(sourceType.SpecialType) ||
            !TryGetIntegralShape(targetType.SpecialType, out _, out _))
            return false;

        string operandIdentity;
        if (TryLowerTerm(castExpression.Expression, context, out var operand))
        {
            operandIdentity = SymbolicStructuralKey.ForTerm(operand);
        }
        else
        {
            var operandSymbol = context.SemanticModel
                .GetSymbolInfo(castExpression.Expression, context.CancellationToken)
                .Symbol;
            if (operandSymbol is not ILocalSymbol and not IParameterSymbol) return false;

            operandIdentity = "symbol:" + context.GetVariableName(operandSymbol);
        }

        var isChecked = context.SemanticModel.GetOperation(castExpression, context.CancellationToken) is
            Microsoft.CodeAnalysis.Operations.IConversionOperation { IsChecked: true };
        term = new SymbolicNumericConversionTerm(
            operandIdentity,
            sourceType.SpecialType,
            targetType.SpecialType,
            isChecked);
        return true;
    }

    private static bool IsNumericSpecialType(SpecialType specialType)
    {
        return specialType is SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Char or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal;
    }

    private static bool IsValuePreservingIntegralConversion(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        if (sourceType is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } enumUnderlyingType })
            sourceType = enumUnderlyingType;

        if (targetType is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } targetUnderlyingType })
            targetType = targetUnderlyingType;

        if (!TryGetIntegralShape(sourceType.SpecialType, out var sourceSigned, out var sourceBits) ||
            !TryGetIntegralShape(targetType.SpecialType, out var targetSigned, out var targetBits))
            return false;

        if (sourceSigned) return targetSigned && targetBits >= sourceBits;

        return targetSigned
            ? targetBits > sourceBits
            : targetBits >= sourceBits;
    }

    private static bool TryGetIntegralShape(SpecialType specialType, out bool signed, out int bits)
    {
        switch (specialType)
        {
            case SpecialType.System_SByte:
                signed = true;
                bits = 8;
                return true;
            case SpecialType.System_Byte:
                signed = false;
                bits = 8;
                return true;
            case SpecialType.System_Int16:
                signed = true;
                bits = 16;
                return true;
            case SpecialType.System_UInt16:
            case SpecialType.System_Char:
                signed = false;
                bits = 16;
                return true;
            case SpecialType.System_Int32:
                signed = true;
                bits = 32;
                return true;
            case SpecialType.System_UInt32:
                signed = false;
                bits = 32;
                return true;
            case SpecialType.System_Int64:
                signed = true;
                bits = 64;
                return true;
            case SpecialType.System_UInt64:
                signed = false;
                bits = 64;
                return true;
            default:
                signed = false;
                bits = 0;
                return false;
        }
    }
}
