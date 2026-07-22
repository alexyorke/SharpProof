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
    /// <summary>
    /// A type is sized when it exposes an instance <see cref="int" /> Length or Count,
    /// which is the shape this model actually depends on. Asking the symbol that
    /// question covers the spans, lists, dictionaries and immutable arrays a name table
    /// used to enumerate, and additionally reaches every collection such a table missed
    /// — HashSet, Queue, Stack, ImmutableList and user-defined collections. Arrays are
    /// checked separately because their Length is a special member rather than a
    /// declared one.
    /// </summary>
    internal static bool IsKnownSizedType(ITypeSymbol? typeSymbol) =>
        typeSymbol is IArrayTypeSymbol ||
        typeSymbol?.SpecialType == SpecialType.System_String ||
        SymbolicTypeFacts.HasInstanceInt32Member(typeSymbol, "Length") ||
        SymbolicTypeFacts.HasInstanceInt32Member(typeSymbol, "Count");

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
             string.Equals(memberAccess.Name.Identifier.ValueText, "Count", StringComparison.Ordinal))) {
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
