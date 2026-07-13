using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    private static bool TryLowerTypeOfComparison(
        BinaryExpressionSyntax binaryExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!binaryExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression) &&
            !binaryExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NotEqualsExpression))
            return false;

        var leftIsTypeOf = TryGetTypeOfType(binaryExpression.Left, context, out var leftType);
        var rightIsTypeOf = TryGetTypeOfType(binaryExpression.Right, context, out var rightType);
        var equals = binaryExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression);
        if (leftIsTypeOf && rightIsTypeOf)
        {
            if (SymbolEqualityComparer.Default.Equals(leftType, rightType))
            {
                condition = new SymbolicConstantCondition(equals);
                return true;
            }

            if (ContainsTypeParameter(leftType) || ContainsTypeParameter(rightType)) return false;

            condition = new SymbolicConstantCondition(!equals);
            return true;
        }

        if (leftIsTypeOf && TryLowerTerm(binaryExpression.Right, context, out var right) &&
            right is SymbolicNullTerm ||
            rightIsTypeOf && TryLowerTerm(binaryExpression.Left, context, out var left) &&
            left is SymbolicNullTerm)
        {
            condition = new SymbolicConstantCondition(!equals);
            return true;
        }

        return false;
    }

    private static bool TryGetTypeOfType(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out ITypeSymbol type)
    {
        expression = UnwrapExpression(expression);
        if (expression is TypeOfExpressionSyntax typeOfExpression)
        {
            type = context.SemanticModel.GetTypeInfo(typeOfExpression.Type, context.CancellationToken).Type!;
            return type is { TypeKind: not TypeKind.Error };
        }

        type = null!;
        return false;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.TypeParameter) return true;
        if (type is IArrayTypeSymbol arrayType) return ContainsTypeParameter(arrayType.ElementType);
        if (type is IPointerTypeSymbol pointerType) return ContainsTypeParameter(pointerType.PointedAtType);
        if (type.ContainingType != null && ContainsTypeParameter(type.ContainingType)) return true;

        return type is INamedTypeSymbol namedType && namedType.TypeArguments.Any(ContainsTypeParameter);
    }

    private static bool TryGetSymbolType(ISymbol symbol, out ITypeSymbol type)
    {
        switch (symbol)
        {
            case ILocalSymbol local:
                type = local.Type;
                return true;
            case IParameterSymbol parameter:
                type = parameter.Type;
                return true;
            case IPropertySymbol property:
                type = property.Type;
                return true;
            case IFieldSymbol field:
                type = field.Type;
                return true;
            default:
                type = null!;
                return false;
        }
    }

    internal static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
    {
        if (type.SpecialType == SpecialType.System_Boolean)
        {
            kind = SmtValueKind.Bool;
            return true;
        }

        if (IsIntegerSmtType(type))
        {
            kind = SmtValueKind.Int;
            return true;
        }

        if (type.TypeKind == TypeKind.Dynamic ||
            type.IsReferenceType ||
            SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(type) ||
            IsSupportedTupleCarrierType(type))
        {
            kind = SmtValueKind.Reference;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool IsIntegerSmtType(ITypeSymbol type)
    {
        return type.SpecialType is
                   SpecialType.System_Char or
                   SpecialType.System_SByte or
                   SpecialType.System_Byte or
                   SpecialType.System_Int16 or
                   SpecialType.System_UInt16 or
                   SpecialType.System_Int32 or
                   SpecialType.System_UInt32 or
                   SpecialType.System_Int64 or
                   SpecialType.System_UInt64 ||
               type.TypeKind == TypeKind.Enum ||
               IsBigIntegerType(type);
    }

    private static bool IsSupportedTupleCarrierType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { IsTupleType: true, TupleElements.Length: > 0 };
    }
}
