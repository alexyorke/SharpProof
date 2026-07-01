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
