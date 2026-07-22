namespace SharpProof.Symbolic.Ir;
internal static class SymbolicTypeLowerer {
    internal static bool TryLowerTypeOfComparison(
        BinaryExpressionSyntax binaryExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!binaryExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression) &&
            !binaryExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NotEqualsExpression))
            return false;
        var leftIsTypeOf = TryGetTypeOfType(binaryExpression.Left, context, out var leftType);
        var rightIsTypeOf = TryGetTypeOfType(binaryExpression.Right, context, out var rightType);
        var equals = binaryExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression);
        if (leftIsTypeOf && rightIsTypeOf) {
            if (SymbolEqualityComparer.Default.Equals(leftType, rightType)) {
                condition = new SymbolicConstantCondition(equals);
                return true;
            }
            if (ContainsTypeParameter(leftType) || ContainsTypeParameter(rightType)) return false;
            condition = new SymbolicConstantCondition(!equals);
            return true;
        }
        if (leftIsTypeOf && SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(binaryExpression.Right, context), out var right) &&
            right is SymbolicNullTerm ||
            rightIsTypeOf && SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(binaryExpression.Left, context), out var left) &&
            left is SymbolicNullTerm) {
            condition = new SymbolicConstantCondition(!equals);
            return true;
        }
        return false;
    }
    private static bool TryGetTypeOfType(ExpressionSyntax expression, SymbolicLoweringContext context, out ITypeSymbol type) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is TypeOfExpressionSyntax typeOfExpression) {
            type = context.SemanticModel.GetTypeInfo(typeOfExpression.Type, context.CancellationToken).Type!;
            return type is { TypeKind: not TypeKind.Error };
        }
        type = null!;
        return false;
    }
    private static bool ContainsTypeParameter(ITypeSymbol type) {
        if (type.TypeKind == TypeKind.TypeParameter) return true;
        if (type is IArrayTypeSymbol arrayType) return ContainsTypeParameter(arrayType.ElementType);
        if (type is IPointerTypeSymbol pointerType) return ContainsTypeParameter(pointerType.PointedAtType);
        if (type.ContainingType != null && ContainsTypeParameter(type.ContainingType)) return true;
        return type is INamedTypeSymbol namedType && namedType.TypeArguments.Any(ContainsTypeParameter);
    }
    internal static bool TryGetSymbolType(ISymbol symbol, out ITypeSymbol type) {
        switch (symbol) {
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
    internal static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind) {
        if (type.SpecialType == SpecialType.System_Boolean) {
            kind = SmtValueKind.Bool;
            return true;
        }
        if (IsIntegerSmtType(type)) {
            kind = SmtValueKind.Int;
            return true;
        }
        if (SymbolicTypeFacts.IsSymbolicReferenceLikeType(type)) {
            kind = SmtValueKind.Reference;
            return true;
        }
        kind = default;
        return false;
    }
    internal static bool IsIntegerSmtType(ITypeSymbol type) => SymbolicTypeFacts.IsBuiltInIntegralOrEnumType(type) ||
               SymbolicNumericLowerer.IsBigIntegerType(type);
}
