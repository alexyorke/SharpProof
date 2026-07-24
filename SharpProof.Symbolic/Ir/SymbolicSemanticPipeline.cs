namespace SharpProof.Symbolic.Ir;
internal static class SymbolicSemanticPipeline {
    private delegate bool TryLowerExpression<T>(ExpressionSyntax expression, SymbolicLoweringContext context, out T value) where T : class;
    private static SymbolicLoweringResult<T> Lower<T>(ExpressionSyntax expression, SymbolicLoweringContext context,
        TryLowerExpression<T> lower, string stage) where T : class =>
        LowerExactOrUnsupported(lower(expression, context, out var value) ? value : default, expression, stage);
    internal static SymbolicLoweringResult<SymbolicTerm> LowerTerm(ExpressionSyntax expression, SymbolicLoweringContext context) =>
        LowerExactOrUnsupported(SymbolicIrLowerer.LowerTerm(expression, context), expression, "term");
    internal static SymbolicLoweringResult<SymbolicCondition> LowerCondition(ExpressionSyntax expression,
        SymbolicLoweringContext context) => LowerExactOrUnsupported(
            SymbolicIrLowerer.LowerCondition(expression, context),
            expression,
            "condition");
    internal static SymbolicLoweringResult<SymbolicCondition> LowerBranchCondition(
        ExpressionSyntax expression,
        bool branchWhenTrue,
        SymbolicLoweringContext context) {
        var lowered = LowerCondition(expression, context);
        if (!lowered.IsExact || lowered.Value == null)
            return Unsupported<SymbolicCondition>(expression, "branch-facts");
        var condition = branchWhenTrue
            ? lowered.Value
            : new SymbolicNotCondition(lowered.Value);
        return Exact(condition, expression, "branch-facts");
    }
    internal static SymbolicLoweringResult<SymbolicCondition> LowerPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode source,
        SymbolicLoweringContext context) => LowerExactOrUnsupported(
            SymbolicPatternLowerer.TryLowerPatternCondition(value, valueType, pattern, source, context, out var condition)
                ? condition
                : null,
            source,
            "pattern");
    internal static SymbolicLoweringResult<SymbolicTerm> LowerReferenceTerm(ExpressionSyntax expression, SymbolicLoweringContext context) =>
        LowerExactOrUnsupported(
            SymbolicIrLowerer.TryLowerReferenceTerm(expression, context, out var term) ? term : null,
            expression,
            "reference-term");
    internal static SymbolicLoweringResult<SymbolicTerm> LowerBooleanValueTerm(ExpressionSyntax expression,
        SymbolicLoweringContext context) => Lower<SymbolicTerm>(expression, context,
            SymbolicSourcePredicateLowerer.TryLowerBooleanValueTerm, "boolean-term");
    internal static SymbolicLoweringResult<SymbolicTerm> LowerBuiltInLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context) => Lower<SymbolicTerm>(expression, context,
            SymbolicIndexingLowerer.TryLowerBuiltInLengthTerm, "built-in-length");
    internal static SymbolicLoweringResult<SymbolicTerm> ProjectBuiltInLengthTerm(
        ITypeSymbol? receiverType,
        SymbolicTerm receiver,
        SyntaxNode source) => LowerExactOrUnsupported(
            SymbolicIndexingLowerer.TryCreateBuiltInLengthReferenceTerm(receiverType, receiver, out var term)
                ? term
                : null,
            source,
            "built-in-length-projection");
    internal static SymbolicLoweringResult<SymbolicTerm> ProjectStringContentTerm(SymbolicTerm receiver, SyntaxNode source)
        => LowerExactOrUnsupported(
            SymbolicStringLowerer.TryCreateStringContentReferenceTerm(receiver, out var term) ? term : null,
            source,
            "string-content-projection");
    internal static SymbolicLoweringResult<SymbolicTerm> LowerNullableHasValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context) => Lower<SymbolicTerm>(expression, context,
            SymbolicNullableLowerer.TryLowerNullableHasValueTerm, "nullable-has-value");
    internal static SymbolicLoweringResult<SymbolicCondition> LowerBuiltInElementAccessInRangeCondition(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicLoweringContext context) {
        var receiverType = context.SemanticModel.GetTypeInfo(elementAccess.Expression, context.CancellationToken).ConvertedType ??
                           context.SemanticModel.GetTypeInfo(elementAccess.Expression, context.CancellationToken).Type;
        if (receiverType is IArrayTypeSymbol { Rank: > 1 } &&
            SymbolicIndexingLowerer.TryCreateArrayElementBoundsCondition(
                elementAccess.Expression,
                elementAccess.ArgumentList.Arguments.Select(static argument => argument.Expression).ToArray(),
                elementAccess,
                "ir.element-access.multidimensional-bounds.in-range",
                context,
                out var multidimensionalCondition,
                out _))
            return Exact(multidimensionalCondition, elementAccess, "element-access-in-range");
        if (elementAccess.ArgumentList.Arguments.Count == 1)
            return LowerBuiltInElementAccessInRangeCondition(
                elementAccess.Expression,
                elementAccess.ArgumentList.Arguments[0].Expression,
                elementAccess,
                context);
        return Unsupported<SymbolicCondition>(elementAccess, "element-access-in-range");
    }
    internal static SymbolicLoweringResult<SymbolicCondition> LowerBuiltInElementAccessOutOfRangeCondition(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicLoweringContext context) {
        var inRangeLowering = LowerBuiltInElementAccessInRangeCondition(elementAccess, context);
        if (inRangeLowering is not { IsExact: true, Value: { } inRangeCondition })
            return Unsupported<SymbolicCondition>(elementAccess, "element-access-out-of-range");
        var condition = (SymbolicCondition)new SymbolicNotCondition(inRangeCondition);
        foreach (var candidate in elementAccess.ArgumentList.Arguments
                     .SelectMany(static argument => argument.Expression.DescendantNodesAndSelf())) {
            if (!SymbolicIndexingLowerer.TryGetIndexConstructionValueExpression(
                    candidate, context, out var valueExpression) ||
                LowerTerm(valueExpression, context) is not
                    { IsExact: true, Value: { Kind: SmtValueKind.Int } value })
                continue;
            var normalCompletion = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(SymbolicRelationOperator.GreaterThanOrEqual, value, new SymbolicIntegerConstantTerm(0)),
                candidate,
                "ir.runtime-hazard.index.constructor-normal-completion"));
            condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, normalCompletion, condition);
        }
        return Exact(condition, elementAccess, "element-access-out-of-range");
    }
    internal static SymbolicLoweringResult<SymbolicCondition> LowerIndexConstructionArgumentOutOfRangeCondition(
        ExpressionSyntax indexConstructionExpression,
        SymbolicLoweringContext context) {
        if (!SymbolicIndexingLowerer.TryGetIndexConstructionValueExpression(
                indexConstructionExpression, context, out var valueExpression) ||
            LowerTerm(valueExpression, context) is not
                { IsExact: true, Value: { Kind: SmtValueKind.Int } value })
            return Unsupported<SymbolicCondition>(indexConstructionExpression, "index-construction-argument-out-of-range");
        SymbolicCondition condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(SymbolicRelationOperator.LessThan, value, new SymbolicIntegerConstantTerm(0)),
            indexConstructionExpression,
            "ir.runtime-hazard.index.constructor-argument-out-of-range"));
        return Exact(condition, indexConstructionExpression, "index-construction-argument-out-of-range");
    }
    internal static SymbolicLoweringResult<SymbolicCondition> LowerBuiltInElementAccessInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax indexExpression,
        SyntaxNode source,
        SymbolicLoweringContext context) => LowerExactOrUnsupported(
            SymbolicIndexingLowerer.TryCreateBuiltInElementAccessInRangeCondition(
                receiverExpression,
                indexExpression,
                source,
                "ir.element-access.bounds.in-range",
                context,
                out var condition)
                ? condition
                : null,
            source,
            "element-access-in-range");
    internal static SymbolicLoweringResult<SymbolicCondition> LowerArrayElementBoundsCondition(
        ExpressionSyntax arrayExpression,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        SyntaxNode source,
        SymbolicLoweringContext context) => LowerExactOrUnsupported(
            SymbolicIndexingLowerer.TryCreateArrayElementBoundsCondition(
                arrayExpression,
                indexExpressions,
                source,
                "ir.array-element.bounds.in-range",
                context,
                out var condition,
                out _)
                ? condition
                : null,
            source,
            "array-element-in-range");
    internal static SymbolicLoweringResult<SymbolicCondition> LowerSubsequenceInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax startExpression,
        ExpressionSyntax? lengthExpression,
        SyntaxNode source,
        SymbolicLoweringContext context,
        bool oneArgumentUpperBoundIsInclusive = true) => LowerExactOrUnsupported(
            SymbolicIndexingLowerer.TryCreateSubsequenceInRangeCondition(
                receiverExpression,
                startExpression,
                lengthExpression,
                source,
                "ir.subsequence.in-range",
                context,
                oneArgumentUpperBoundIsInclusive,
                out var condition)
                ? condition
                : null,
            source,
            "subsequence-in-range");
    internal static SymbolicLoweringResult<SymbolicCondition> LowerIntegerBinaryInRangeCondition(
        ExpressionSyntax leftExpression,
        ExpressionSyntax rightExpression,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SyntaxNode source,
        SymbolicLoweringContext context) {
        var left = LowerTerm(leftExpression, context);
        var right = LowerTerm(rightExpression, context);
        if (SymbolicOperatorLowerer.TryGetBinaryTermOperator(smtOperator, out var binaryOperator) &&
            binaryOperator is not (SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder) &&
            left is { IsExact: true, Value: { Kind: SmtValueKind.Int } leftTerm } &&
            right is { IsExact: true, Value: { Kind: SmtValueKind.Int } rightTerm })
            return Exact<SymbolicCondition>(
                SymbolicIrLowerer.CreateIntegerInRangeCondition(
                    new SymbolicBinaryTerm(binaryOperator, leftTerm, rightTerm),
                    minValue,
                    maxValue,
                    source,
                    "ir.integer.binary.in-range"),
                source,
                "integer-binary-in-range");
        return Unsupported<SymbolicCondition>(source, "integer-binary-in-range");
    }
    internal static SymbolicLoweringResult<SymbolicCondition> LowerNumericZeroCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context) {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is PrefixUnaryExpressionSyntax unary &&
            unary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.UnaryMinusExpression) &&
            context.SemanticModel.GetOperation(unary, context.CancellationToken) is
                Microsoft.CodeAnalysis.Operations.IUnaryOperation { OperatorMethod: null })
            return LowerNumericZeroCondition(unary.Operand, context);
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constant.HasValue) {
            if (SymbolicValueFacts.IsIntegralOrDecimalZero(constant.Value))
                return Exact<SymbolicCondition>(new SymbolicConstantCondition(true), expression, "numeric-zero");
            if (constant.Value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal)
                return Exact<SymbolicCondition>(new SymbolicConstantCondition(false), expression, "numeric-zero");
        }
        var lowered = LowerTerm(expression, context);
        SymbolicTerm? value = lowered is { IsExact: true, Value: { Kind: SmtValueKind.Int } integer }
            ? integer
            : null;
        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        if (value == null &&
            symbol is ILocalSymbol or IParameterSymbol &&
            context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type?.SpecialType ==
            SpecialType.System_Decimal)
            value = context.TryGetSubstitution(symbol, out var substituted)
                ? substituted
                : new SymbolicVariableTerm(context.GetVariableName(symbol), SmtValueKind.Int);
        if (value is { Kind: SmtValueKind.Int })
            return Exact(SymbolicIrLowerer.CreateIntegerZeroCondition(value, expression, "ir.numeric-zero"), expression, "numeric-zero");
        return Unsupported<SymbolicCondition>(expression, "numeric-zero");
    }
    private static SymbolicLoweringResult<T> LowerExactOrUnsupported<T>(T? value, SyntaxNode source, string stage)
        where T : class => value == null ? Unsupported<T>(source, stage) : Exact(value, source, stage);
    private static SymbolicLoweringResult<T> Exact<T>(T value, SyntaxNode source, string stage)
        where T : class => SymbolicLoweringResult<T>.Exact(value, CreateProvenance(source, stage, "exact"));
    private static SymbolicLoweringResult<T> Unsupported<T>(SyntaxNode source, string stage)
        where T : class => SymbolicLoweringResult<T>.Unsupported(CreateProvenance(source, stage, "unsupported"));
    private static SymbolicLoweringProvenance CreateProvenance(SyntaxNode source, string stage, string detail)
        => new("roslyn-to-ir." + stage, source.Span, detail);
}
