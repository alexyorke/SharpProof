namespace SharpProof.Symbolic;
internal sealed class SymbolicComplexityCostModel(CancellationToken _cancellationToken) {
    internal bool TryCreate(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod,
        CostProjection projection,
        bool allowConstants,
        out SymbolicCostExpression cost) {
        cost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.Unknown);
        if (expression == null) return false;
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (allowConstants && TryGetIntegralConstant(expression, semanticModel, out _)) {
            cost = SymbolicCostExpression.Constant();
            return true;
        }
        if (projection == CostProjection.LengthOrCount) {
            if (TryCreateLengthOrCount(expression, semanticModel, currentMethod, out cost)) return true;
        }
        else if (TryCreateScalar(expression, semanticModel, currentMethod, out cost)) {
            return true;
        }
        if (expression is BinaryExpressionSyntax binaryExpression &&
            (binaryExpression.IsKind(SyntaxKind.AddExpression) ||
             binaryExpression.IsKind(SyntaxKind.SubtractExpression))) {
            if (TryGetIntegralConstant(binaryExpression.Right, semanticModel, out _) &&
                TryCreate(binaryExpression.Left, semanticModel, currentMethod, projection, allowConstants, out cost))
                return true;
            if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                TryGetIntegralConstant(binaryExpression.Left, semanticModel, out _) &&
                TryCreate(binaryExpression.Right, semanticModel, currentMethod, projection, allowConstants, out cost))
                return true;
        }
        return false;
    }
    internal bool TryGetIntegralConstant(ExpressionSyntax expression, SemanticModel semanticModel, out long value)
        => SymbolicLoweringValueFacts.TryGetIntegralConstant(expression, semanticModel, _cancellationToken, out value);
    internal static bool IsKnownSizedType(ITypeSymbol? typeSymbol) =>
        typeSymbol is IArrayTypeSymbol ||
        typeSymbol?.SpecialType == SpecialType.System_String ||
        typeSymbol is INamedTypeSymbol namedType && IsKnownConstantTimeSizedType(namedType);
    internal static bool IsKnownConstantTimeIndexer(IPropertySymbol property) =>
        property.IsIndexer && IsKnownSizedType(property.ContainingType) &&
        property.Parameters.Length == 1 &&
        property.Parameters[0].Type.SpecialType == SpecialType.System_Int32;
    internal static bool IsKnownConstantTimeSizeProperty(IPropertySymbol property) =>
        !property.IsStatic &&
        property.Type.SpecialType == SpecialType.System_Int32 &&
        property.Parameters.Length == 0 &&
        (string.Equals(property.Name, "Length", StringComparison.Ordinal) ||
         string.Equals(property.Name, "Count", StringComparison.Ordinal)) &&
        IsKnownSizedType(property.ContainingType);
    private static bool IsKnownConstantTimeSizedType(INamedTypeSymbol typeSymbol) =>
        typeSymbol.OriginalDefinition.ToDisplayString() is
            "System.Span<T>" or
            "System.ReadOnlySpan<T>" or
            "System.Memory<T>" or
            "System.ReadOnlyMemory<T>" or
            "System.ArraySegment<T>" or
            "System.Collections.Generic.List<T>" or
            "System.Collections.Generic.Dictionary<TKey, TValue>" or
            "System.Collections.Generic.HashSet<T>" or
            "System.Collections.Generic.Queue<T>" or
            "System.Collections.Generic.Stack<T>" or
            "System.Collections.Immutable.ImmutableArray<T>";
    private bool TryCreateScalar(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod,
        out SymbolicCostExpression cost) {
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            (string.Equals(memberAccess.Name.Identifier.ValueText, "Length", StringComparison.Ordinal) ||
             string.Equals(memberAccess.Name.Identifier.ValueText, "Count", StringComparison.Ordinal)) &&
            TryCreateLengthOrCount(expression, semanticModel, currentMethod, out cost))
            return true;
        if (semanticModel.GetSymbolInfo(expression, _cancellationToken).Symbol is IParameterSymbol parameter &&
            SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition, currentMethod.OriginalDefinition)) {
            cost = SymbolicCostExpression.Variable("$p" + parameter.Ordinal + ":value");
            return true;
        }
        if (semanticModel.GetSymbolInfo(expression, _cancellationToken).Symbol is ISymbol symbol) {
            if (SymbolEqualityComparer.Default.Equals(symbol, currentMethod.AssociatedSymbol)) {
                cost = SymbolicCostExpression.Variable("$this");
                return true;
            }
            cost = SymbolicCostExpression.Variable("name:" + symbol.Name);
            return true;
        }
        cost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.Unknown);
        return false;
    }
    private bool TryCreateLengthOrCount(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod,
        out SymbolicCostExpression cost) {
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            (string.Equals(memberAccess.Name.Identifier.ValueText, "Length", StringComparison.Ordinal) ||
             string.Equals(memberAccess.Name.Identifier.ValueText, "Count", StringComparison.Ordinal)) &&
            semanticModel.GetSymbolInfo(memberAccess, _cancellationToken).Symbol is IPropertySymbol sizeProperty &&
            IsKnownConstantTimeSizeProperty(sizeProperty)) {
            if (semanticModel.GetSymbolInfo(memberAccess.Expression, _cancellationToken).Symbol is IParameterSymbol
                    parameter &&
                SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition, currentMethod.OriginalDefinition)) {
                cost = SymbolicCostExpression.Variable("$p" + parameter.Ordinal + ":length");
                return true;
            }
            if (memberAccess.Expression is ThisExpressionSyntax) {
                cost = SymbolicCostExpression.Variable("$this.length");
                return true;
            }
            if (semanticModel.GetSymbolInfo(memberAccess.Expression, _cancellationToken).Symbol is ISymbol
                receiverSymbol) {
                cost = SymbolicCostExpression.Variable("name:" + receiverSymbol.Name + "." + memberAccess.Name.Identifier.ValueText);
                return true;
            }
        }
        var expressionType = semanticModel.GetTypeInfo(expression, _cancellationToken).Type;
        if (expressionType != null && IsKnownSizedType(expressionType)) {
            if (semanticModel.GetSymbolInfo(expression, _cancellationToken).Symbol is IParameterSymbol parameter &&
                SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition, currentMethod.OriginalDefinition)) {
                cost = SymbolicCostExpression.Variable("$p" + parameter.Ordinal + ":length");
                return true;
            }
            if (expression is ThisExpressionSyntax) {
                cost = SymbolicCostExpression.Variable("$this.length");
                return true;
            }
            if (semanticModel.GetSymbolInfo(expression, _cancellationToken).Symbol is ISymbol receiverSymbol) {
                cost = SymbolicCostExpression.Variable("name:" + receiverSymbol.Name + ".Length");
                return true;
            }
        }
        cost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.Unknown);
        return false;
    }
}
