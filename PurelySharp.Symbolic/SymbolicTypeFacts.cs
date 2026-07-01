using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PurelySharp.Symbolic
{
    internal static class SymbolicTypeFacts
    {
        public static bool IsReferenceType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            if (typeSymbol is ITypeParameterSymbol typeParameter)
            {
                return IsKnownReferenceTypeParameter(
                    typeParameter,
                    new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default));
            }

            return typeSymbol.IsReferenceType;
        }

        public static bool IsReferenceLikeType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol?.TypeKind == TypeKind.Dynamic ||
                IsReferenceType(typeSymbol);
        }

        public static bool IsNullableType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            };
        }

        public static bool TryGetNullableUnderlyingType(ITypeSymbol? typeSymbol, out ITypeSymbol underlyingType)
        {
            if (typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                namedType.TypeArguments.Length == 1)
            {
                underlyingType = namedType.TypeArguments[0];
                return true;
            }

            underlyingType = null!;
            return false;
        }

        public static bool IsDynamicExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ExpressionSyntax, ExpressionSyntax> unwrapExpression)
        {
            expression = unwrapExpression(expression);
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return typeInfo.Type?.TypeKind == TypeKind.Dynamic ||
                typeInfo.ConvertedType?.TypeKind == TypeKind.Dynamic;
        }

        public static bool IsSystemRangeType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol
            {
                Name: "Range",
                ContainingNamespace: { } containingNamespace
            } &&
            containingNamespace.ToDisplayString() == "System";
        }

        public static bool IsSystemIndexType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol
            {
                Name: "Index",
                ContainingNamespace: { } containingNamespace
            } &&
            containingNamespace.ToDisplayString() == "System";
        }

        public static bool IsBuiltInSpanType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>";
        }

        public static bool IsBuiltInMemoryType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() is "System.Memory<T>" or "System.ReadOnlyMemory<T>";
        }

        public static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? typeSymbol)
        {
            return IsBuiltInSpanType(typeSymbol) ||
                IsBuiltInMemoryType(typeSymbol);
        }

        public static bool IsNullableValueAccess(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return memberAccess.Name.Identifier.ValueText == "Value" &&
                semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol
                {
                    Name: "Value",
                    ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                };
        }

        public static bool IsThrowingDivideByZeroType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Decimal:
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryGetCheckedIntegralRange(
            ITypeSymbol? typeSymbol,
            out long minValue,
            out long maxValue)
        {
            switch (typeSymbol?.SpecialType)
            {
                case SpecialType.System_Int32:
                    minValue = int.MinValue;
                    maxValue = int.MaxValue;
                    return true;
                case SpecialType.System_UInt32:
                    minValue = uint.MinValue;
                    maxValue = uint.MaxValue;
                    return true;
                case SpecialType.System_Int64:
                    minValue = long.MinValue;
                    maxValue = long.MaxValue;
                    return true;
                default:
                    minValue = default;
                    maxValue = default;
                    return false;
            }
        }

        public static bool TryGetBoundedIntegralRange(
            ITypeSymbol? typeSymbol,
            out long minValue,
            out long maxValue)
        {
            return TryGetCheckedNumericConversionRange(typeSymbol, out minValue, out maxValue);
        }

        public static bool TryGetCheckedNumericConversionRange(
            ITypeSymbol? typeSymbol,
            out long minValue,
            out long maxValue)
        {
            switch (typeSymbol?.SpecialType)
            {
                case SpecialType.System_Char:
                    minValue = char.MinValue;
                    maxValue = char.MaxValue;
                    return true;
                case SpecialType.System_SByte:
                    minValue = sbyte.MinValue;
                    maxValue = sbyte.MaxValue;
                    return true;
                case SpecialType.System_Byte:
                    minValue = byte.MinValue;
                    maxValue = byte.MaxValue;
                    return true;
                case SpecialType.System_Int16:
                    minValue = short.MinValue;
                    maxValue = short.MaxValue;
                    return true;
                case SpecialType.System_UInt16:
                    minValue = ushort.MinValue;
                    maxValue = ushort.MaxValue;
                    return true;
                case SpecialType.System_Int32:
                    minValue = int.MinValue;
                    maxValue = int.MaxValue;
                    return true;
                case SpecialType.System_UInt32:
                    minValue = uint.MinValue;
                    maxValue = uint.MaxValue;
                    return true;
                case SpecialType.System_Int64:
                    minValue = long.MinValue;
                    maxValue = long.MaxValue;
                    return true;
                default:
                    minValue = default;
                    maxValue = default;
                    return false;
            }
        }

        private static bool IsKnownReferenceTypeParameter(
            ITypeParameterSymbol typeParameter,
            HashSet<ITypeParameterSymbol> visited)
        {
            if (!visited.Add(typeParameter))
            {
                return false;
            }

            if (typeParameter.HasReferenceTypeConstraint)
            {
                return true;
            }

            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                if (constraint.IsReferenceType)
                {
                    return true;
                }

                if (constraint is ITypeParameterSymbol nestedTypeParameter &&
                    IsKnownReferenceTypeParameter(nestedTypeParameter, visited))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
