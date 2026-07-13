using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeTypeFacts
{
    internal static bool TryGetExactRuntimeType(
        ExpressionSyntax expression,
        SyntaxNode useNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ITypeSymbol exactType,
        int inlineDepth = 0)
    {
        exactType = null!;
        if (inlineDepth > 8) return false;

        expression = UnwrapRuntimeTypeExpression(expression);
        if (SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                expression,
                useNode,
                semanticModel,
                cancellationToken,
                out var currentValueExpression))
            return TryGetExactRuntimeType(
                currentValueExpression,
                useNode,
                semanticModel,
                cancellationToken,
                out exactType,
                inlineDepth + 1);

        var expressionType = GetNaturalExpressionType(expression, semanticModel, cancellationToken);
        if (expressionType != null && IsNonNullableValueType(expressionType))
        {
            exactType = expressionType;
            return true;
        }

        if (expressionType?.TypeKind == TypeKind.Dynamic) return false;

        if (expression is CastExpressionSyntax castExpression)
        {
            var targetType = CSharpSyntaxFacts.GetExpressionType(castExpression, semanticModel, cancellationToken);
            if (targetType == null ||
                targetType.TypeKind == TypeKind.Dynamic)
                return false;

            if (SymbolicTypeFacts.IsReferenceType(targetType))
            {
                var operandType = CSharpSyntaxFacts.GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
                if (IsNonNullableValueType(operandType) &&
                    TryGetExactRuntimeType(
                        castExpression.Expression,
                        useNode,
                        semanticModel,
                        cancellationToken,
                        out var boxedValueType,
                        inlineDepth + 1))
                {
                    exactType = boxedValueType;
                    return true;
                }

                if (TryGetExactRuntimeType(
                        castExpression.Expression,
                        useNode,
                        semanticModel,
                        cancellationToken,
                        out var operandExactType,
                        inlineDepth + 1) &&
                    CanCastExactRuntimeTypeToReferenceType(
                        operandExactType,
                        targetType,
                        semanticModel.Compilation))
                {
                    exactType = operandExactType;
                    return true;
                }
            }

            if (IsNonNullableValueType(targetType))
            {
                exactType = targetType;
                return true;
            }

            return false;
        }

        if (expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or
            ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax
            or AnonymousObjectCreationExpressionSyntax)
        {
            if (expressionType != null && !expressionType.IsAbstract)
            {
                exactType = expressionType;
                return true;
            }

            return false;
        }

        if (expression.IsKind(SyntaxKind.StringLiteralExpression) &&
            expressionType?.SpecialType == SpecialType.System_String)
        {
            exactType = expressionType;
            return true;
        }

        return false;
    }

    internal static ITypeSymbol? GetNaturalExpressionType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return typeInfo.Type ?? typeInfo.ConvertedType;
    }

    internal static bool CanStoreExactRuntimeTypeInArrayElement(
        ITypeSymbol exactRuntimeType,
        ITypeSymbol elementType,
        Compilation compilation)
    {
        if (exactRuntimeType.TypeKind == TypeKind.Dynamic ||
            elementType.TypeKind == TypeKind.Dynamic)
            return true;

        return CanCastExactRuntimeTypeToReferenceType(exactRuntimeType, elementType, compilation);
    }

    internal static bool CanUnboxExactRuntimeTypeToValueType(ITypeSymbol exactRuntimeType, ITypeSymbol targetType)
    {
        if (!IsNonNullableValueType(targetType)) return false;

        return SymbolEqualityComparer.Default.Equals(exactRuntimeType, targetType);
    }

    internal static bool CanCastExactRuntimeTypeToReferenceType(
        ITypeSymbol exactRuntimeType,
        ITypeSymbol targetType,
        Compilation compilation)
    {
        if (targetType.TypeKind == TypeKind.Dynamic ||
            exactRuntimeType.TypeKind == TypeKind.Dynamic)
            return true;

        if (SymbolicTypeFacts.IsReferenceType(targetType) &&
            targetType.SpecialType == SpecialType.System_Object)
            return true;

        var conversion = compilation.ClassifyCommonConversion(exactRuntimeType, targetType);
        return conversion.Exists &&
               (conversion.IsIdentity || conversion.IsImplicit);
    }

    internal static bool TryGetRuntimeTypeTestKey(ITypeSymbol? targetType, out string typeKey)
    {
        if (targetType == null ||
            targetType.TypeKind is TypeKind.Dynamic or TypeKind.Error or TypeKind.TypeParameter ||
            !targetType.IsReferenceType)
        {
            typeKey = null!;
            return false;
        }

        if (targetType.SpecialType == SpecialType.System_Object)
        {
            typeKey = "System.Object";
            return true;
        }

        if (targetType.SpecialType == SpecialType.System_String)
        {
            typeKey = "System.String";
            return true;
        }

        typeKey = targetType
            .WithNullableAnnotation(NullableAnnotation.None)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
        return true;
    }

    private static bool IsNonNullableValueType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.IsValueType == true &&
               !IsNullableType(typeSymbol);
    }

    private static bool IsNullableType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsNullableType(typeSymbol);
    }

    private static ExpressionSyntax UnwrapRuntimeTypeExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }
}
