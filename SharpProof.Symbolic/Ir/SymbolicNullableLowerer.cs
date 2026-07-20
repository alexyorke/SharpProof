namespace SharpProof.Symbolic.Ir;

internal static class SymbolicNullableLowerer {
    internal static bool TryCreateSymbolTerms(
        ISymbol symbol,
        SymbolicLoweringContext context,
        out SymbolicTerm hasValue,
        out SymbolicTerm value) {
        hasValue = null!;
        value = null!;
        if (!SymbolicTypeFacts.TryGetNullableUnderlyingType(
                SymbolicFactFactory.GetTrackedSymbolType(symbol),
                out var underlyingType) ||
            !SymbolicFactFactory.TryGetValueKind(
                underlyingType,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsSymbolicReferenceLikeType,
                out var valueKind))
            return false;

        var symbolName = context.GetVariableName(symbol);
        hasValue = new SymbolicNullableHasValueTerm(symbolName);
        value = new SymbolicNullableValueTerm(symbolName, valueKind);
        return true;
    }

    internal static bool TryLowerCoalesceAssignmentTerm(
        AssignmentExpressionSyntax assignment,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (TryLowerNullableHasValueTerm(assignment.Left, context, out var hasValue) &&
            TryLowerNullableValueTerm(assignment.Left, context, out var nullableValue) &&
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(assignment.Right, context), out var fallback) &&
            nullableValue.Kind == fallback.Kind) {
            term = new SymbolicConditionalTerm(
                SymbolicIrLowerer.CreateFactCondition(
                    new SymbolicTruthAtom(hasValue),
                    assignment.Left,
                    "ir.coalesce-assignment.has-value"),
                nullableValue,
                fallback);
            return true;
        }

        if (SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(assignment.Left, context), out var reference) &&
            reference.Kind == SmtValueKind.Reference &&
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(assignment.Right, context), out var referenceFallback) &&
            referenceFallback.Kind == SmtValueKind.Reference) {
            term = new SymbolicConditionalTerm(
                SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    reference,
                    new SymbolicNullTerm(),
                    assignment.Left,
                    "ir.coalesce-assignment.non-null"),
                reference,
                referenceFallback);
            return true;
        }

        term = null!;
        return false;
    }

    internal static bool TryLowerNotNullIfNotNullNullComparison(
        BinaryExpressionSyntax comparison,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!comparison.IsKind(SyntaxKind.EqualsExpression) &&
            !comparison.IsKind(SyntaxKind.NotEqualsExpression))
            return false;

        ExpressionSyntax resultExpression;
        if (IsNullConstant(comparison.Left, context))
            resultExpression = comparison.Right;
        else if (IsNullConstant(comparison.Right, context))
            resultExpression = comparison.Left;
        else
            return false;

        if (!TryLowerNotNullIfNotNullResultNonNullTerm(
                resultExpression,
                context,
                false,
                out var resultNonNull))
            return false;

        condition = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicTruthAtom(resultNonNull),
            comparison,
            "ir.not-null-if-not-null.result");
        if (comparison.IsKind(SyntaxKind.EqualsExpression)) condition = new SymbolicNotCondition(condition);
        return true;
    }

    internal static bool TryLowerNotNullIfNotNullResultNonNullTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        bool requireLocalOrParameterSource,
        out SymbolicTerm term) {
        term = null!;
        var operation = context.SemanticModel.GetOperation(expression, context.CancellationToken);
        ExpressionSyntax? sourceExpression = null;
        if (operation is IInvocationOperation invocation &&
            NullableFlowFacts.TryGetNotNullIfNotNullParameterName(
                invocation.TargetMethod,
                out var methodParameterName)) {
            var parameter = invocation.TargetMethod.Parameters
                .FirstOrDefault(candidate => string.Equals(
                    candidate.Name,
                    methodParameterName,
                    StringComparison.Ordinal));
            if (parameter != null &&
                SymbolicValueFacts.TryGetInvocationArgumentExpression(
                    invocation,
                    parameter.Ordinal,
                    out var argumentExpression))
                sourceExpression = argumentExpression;
        }
        else if (operation is IPropertyReferenceOperation property &&
                 NullableFlowFacts.TryGetNotNullIfNotNullParameterName(
                     property.Property,
                     out var propertyParameterName)) {
            var argument = property.Arguments.FirstOrDefault(candidate => string.Equals(
                candidate.Parameter?.Name,
                propertyParameterName,
                StringComparison.Ordinal));
            sourceExpression = argument?.Value.Syntax as ExpressionSyntax;
        }

        if (sourceExpression == null ||
            requireLocalOrParameterSource &&
            context.SemanticModel.GetSymbolInfo(
                SymbolicLoweringValueFacts.UnwrapExpression(sourceExpression),
                context.CancellationToken).Symbol?.OriginalDefinition is not (ILocalSymbol or IParameterSymbol) ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(sourceExpression, context), out var source) ||
            source.Kind != SmtValueKind.Reference)
            return false;

        var sourceNonNull = SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.NotEqual,
            source,
            new SymbolicNullTerm(),
            sourceExpression,
            "ir.not-null-if-not-null.source");
        var filePath = expression.SyntaxTree.FilePath ?? string.Empty;
        var fallbackName = "$not-null-if-not-null:" + filePath + ":" +
                           expression.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                           expression.Span.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        term = new SymbolicConditionalTerm(
            sourceNonNull,
            new SymbolicBooleanConstantTerm(true),
            new SymbolicVariableTerm(fallbackName, SmtValueKind.Bool));
        return true;
    }

    internal static bool IsNullConstant(ExpressionSyntax expression, SymbolicLoweringContext context) {
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        return constant is { HasValue: true, Value: null } ||
               expression.IsKind(SyntaxKind.NullLiteralExpression);
    }

    internal static bool TryLowerNullableNullComparisonCondition(
        BinaryExpressionSyntax comparison,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!comparison.IsKind(SyntaxKind.EqualsExpression) &&
            !comparison.IsKind(SyntaxKind.NotEqualsExpression))
            return false;

        ExpressionSyntax nullableExpression;
        if (IsNullConstant(comparison.Left, context))
            nullableExpression = comparison.Right;
        else if (IsNullConstant(comparison.Right, context))
            nullableExpression = comparison.Left;
        else
            return false;

        if (!TryLowerNullableHasValueTerm(nullableExpression, context, out var hasValue)) return false;

        condition = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicTruthAtom(hasValue),
            comparison,
            "ir.nullable.null-comparison.has-value");
        if (comparison.IsKind(SyntaxKind.EqualsExpression)) condition = new SymbolicNotCondition(condition);
        return true;
    }

    internal static bool TryLowerNullableValueAccessRelationCondition(
        BinaryExpressionSyntax binaryExpression,
        SymbolicRelationOperator relationOperator,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        var nullableOnLeft = TryGetNullableValueAccessOperand(
            binaryExpression.Left,
            context,
            out var nullableHasValue,
            out var nullableValue);
        if (!nullableOnLeft &&
            !TryGetNullableValueAccessOperand(
                binaryExpression.Right,
                context,
                out nullableHasValue,
                out nullableValue))
            return false;

        var otherExpression = nullableOnLeft ? binaryExpression.Right : binaryExpression.Left;
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(otherExpression, context), out var otherValue) ||
            !SymbolicOperatorLowerer.CanCompareTerms(
                nullableOnLeft ? nullableValue : otherValue,
                nullableOnLeft ? otherValue : nullableValue,
                relationOperator))
            return false;

        var hasValueCondition = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicTruthAtom(nullableHasValue),
            binaryExpression,
            "ir.nullable.value-access.has-value");
        var valueCondition = SymbolicIrLowerer.CreateRelationCondition(
            relationOperator,
            nullableOnLeft ? nullableValue : otherValue,
            nullableOnLeft ? otherValue : nullableValue,
            binaryExpression,
            "ir.nullable.value-access.relation");
        condition = new SymbolicBinaryCondition(
            SymbolicConditionOperator.And,
            hasValueCondition,
            valueCondition);
        return true;
    }

    private static bool TryGetNullableValueAccessOperand(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm hasValue,
        out SymbolicTerm value) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        hasValue = null!;
        value = null!;
        if (expression is not MemberAccessExpressionSyntax memberAccess ||
            context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not
                IPropertySymbol {
                    Name: nameof(Nullable<int>.Value),
                    ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                })
            return false;

        return TryLowerNullableHasValueTerm(memberAccess.Expression, context, out hasValue) &&
               TryLowerNullableValueTerm(memberAccess.Expression, context, out value);
    }

    internal static bool TryLowerNullableRelationCondition(
        BinaryExpressionSyntax binaryExpression,
        SymbolicRelationOperator relationOperator,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        bool nullableOnLeft;
        SymbolicTerm nullableHasValue;
        SymbolicTerm nullableValue;
        SymbolicTerm otherValue;
        if (TryGetNullableRelationOperand(
                binaryExpression.Left,
                context,
                out nullableHasValue,
                out nullableValue) &&
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(binaryExpression.Right, context), out otherValue) &&
            SymbolicOperatorLowerer.CanCompareTerms(nullableValue, otherValue, relationOperator)) {
            nullableOnLeft = true;
        }
        else {
            if (!TryGetNullableRelationOperand(
                    binaryExpression.Right,
                    context,
                    out nullableHasValue,
                    out nullableValue) ||
                !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(binaryExpression.Left, context), out otherValue) ||
                !SymbolicOperatorLowerer.CanCompareTerms(otherValue, nullableValue, relationOperator))
                return false;

            nullableOnLeft = false;
        }

        var hasValueCondition = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicTruthAtom(nullableHasValue),
            binaryExpression,
            "ir.nullable.relation.has-value");
        var valueCondition = SymbolicIrLowerer.CreateRelationCondition(
            relationOperator,
            nullableOnLeft ? nullableValue : otherValue,
            nullableOnLeft ? otherValue : nullableValue,
            binaryExpression,
            "ir.nullable.relation.value");

        condition = relationOperator == SymbolicRelationOperator.NotEqual
            ? new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicNotCondition(hasValueCondition),
                valueCondition)
            : new SymbolicBinaryCondition(SymbolicConditionOperator.And, hasValueCondition, valueCondition);
        return true;
    }

    private static bool TryGetNullableRelationOperand(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm hasValue,
        out SymbolicTerm value) {
        hasValue = null!;
        value = null!;
        return TryLowerNullableHasValueTerm(expression, context, out hasValue) &&
               TryLowerNullableValueTerm(expression, context, out value);
    }

    internal static bool TryLowerNullableGetValueOrDefaultInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            invocation.ArgumentList.Arguments.Count is not 0 and not 1 ||
            method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
            method.ContainingType?.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T ||
            !TryLowerNullableHasValueTerm(memberAccess.Expression, context, out var hasValueTerm) ||
            !TryLowerNullableValueTerm(memberAccess.Expression, context, out var valueTerm))
            return false;

        SymbolicTerm fallbackTerm;
        if (invocation.ArgumentList.Arguments.Count == 0) {
            if (!TryCreateDefaultTerm(method.ReturnType, out fallbackTerm)) return false;
        }
        else if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(invocation.ArgumentList.Arguments[0].Expression, context), out fallbackTerm) ||
                 fallbackTerm.Kind != valueTerm.Kind) {
            return false;
        }

        term = new SymbolicConditionalTerm(
            SymbolicIrLowerer.CreateFactCondition(
                new SymbolicTruthAtom(hasValueTerm),
                invocation,
                "ir.known-api.nullable.get-value-or-default.has-value"),
            valueTerm,
            fallbackTerm);
        return true;
    }

    internal static bool TryLowerNullableHasValueTerm(
        ExpressionSyntax nullableExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        var originalExpression = nullableExpression;
        nullableExpression = SymbolicLoweringValueFacts.UnwrapExpression(nullableExpression);
        var typeInfo = context.SemanticModel.GetTypeInfo(originalExpression, context.CancellationToken);
        var expressionType = typeInfo.ConvertedType ?? typeInfo.Type;
        if (!SymbolicTypeFacts.TryGetNullableUnderlyingType(
                expressionType,
                out var underlyingType)) {
            term = null!;
            return false;
        }

        if (SymbolicAsyncLowerer.TryGetKnownCompletedAsyncResultExpression(
                nullableExpression,
                context,
                out var completedResultExpression) &&
            TryLowerNullableHasValueTerm(completedResultExpression, context, out term))
            return true;

        if (SymbolicLoweringValueFacts.TryGetStableVariableSymbol(nullableExpression, context, out var symbol)) {
            term = new SymbolicNullableHasValueTerm(context.GetVariableName(symbol));
            return true;
        }

        if (TryLowerNullLikeNullableHasValueTerm(nullableExpression, context, out term)) return true;

        if (nullableExpression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            TryLowerNullableCoalesceHasValueTerm(coalesceExpression, context, out term))
            return true;

        if (nullableExpression is ConditionalExpressionSyntax conditionalExpression &&
            TryLowerNullableConditionalHasValueTerm(conditionalExpression, context, out term))
            return true;

        if (nullableExpression is ConditionalAccessExpressionSyntax conditionalAccess &&
            TryLowerNullableConditionalAccessHasValueTerm(conditionalAccess, context, out term))
            return true;

        var valueExpression = nullableExpression is CastExpressionSyntax castExpression
            ? castExpression.Expression
            : nullableExpression;
        if (valueExpression != nullableExpression ||
            !SymbolicTypeFacts.TryGetNullableUnderlyingType(typeInfo.Type, out _)) {
            var valueTypeInfo = context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken);
            if (SymbolEqualityComparer.Default.Equals(valueTypeInfo.ConvertedType, underlyingType) ||
                SymbolEqualityComparer.Default.Equals(valueTypeInfo.Type, underlyingType)) {
                term = new SymbolicBooleanConstantTerm(true);
                return true;
            }
        }

        term = null!;
        return false;
    }

    private static bool TryLowerNullLikeNullableHasValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constant is { HasValue: true, Value: null } ||
            expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
            expression is DefaultExpressionSyntax) {
            term = new SymbolicBooleanConstantTerm(false);
            return true;
        }

        term = null!;
        return false;
    }

    private static bool TryLowerNullableCoalesceHasValueTerm(
        BinaryExpressionSyntax coalesceExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (!TryLowerNullableHasValueTerm(coalesceExpression.Left, context, out var leftHasValue) ||
            !TryLowerNullableHasValueTerm(coalesceExpression.Right, context, out var rightHasValue)) {
            term = null!;
            return false;
        }

        term = new SymbolicConditionalTerm(
            SymbolicIrLowerer.CreateFactCondition(
                new SymbolicTruthAtom(leftHasValue),
                coalesceExpression.Left,
                "ir.nullable.coalesce.left-has-value"),
            new SymbolicBooleanConstantTerm(true),
            rightHasValue);
        return true;
    }

    private static bool TryLowerNullableConditionalHasValueTerm(
        ConditionalExpressionSyntax conditionalExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(conditionalExpression.Condition, context), out var condition) ||
            !TryLowerNullableHasValueTerm(conditionalExpression.WhenTrue, context, out var whenTrueHasValue) ||
            !TryLowerNullableHasValueTerm(conditionalExpression.WhenFalse, context, out var whenFalseHasValue)) {
            term = null!;
            return false;
        }

        term = new SymbolicConditionalTerm(condition, whenTrueHasValue, whenFalseHasValue);
        return true;
    }

    private static bool TryLowerNullableConditionalAccessHasValueTerm(
        ConditionalAccessExpressionSyntax conditionalAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(conditionalAccess.Expression, context), out var receiver) ||
            receiver.Kind != SmtValueKind.Reference) {
            term = null!;
            return false;
        }

        term = new SymbolicConditionalTerm(
            SymbolicIrLowerer.CreateReferenceNullCondition(
                receiver,
                false,
                conditionalAccess.Expression,
                "ir.nullable.conditional-access.receiver-not-null"),
            new SymbolicBooleanConstantTerm(true),
            new SymbolicBooleanConstantTerm(false));
        return true;
    }

    private static bool TryLowerNullLikeNullableValueTerm(
        ExpressionSyntax expression,
        ITypeSymbol underlyingType,
        out SymbolicTerm term) {
        if (expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
            expression is DefaultExpressionSyntax ||
            expression.IsKind(SyntaxKind.NullLiteralExpression))
            return TryCreateDefaultTerm(underlyingType, out term);

        term = null!;
        return false;
    }

    private static bool TryLowerNullableCoalesceNullableValueTerm(
        BinaryExpressionSyntax coalesceExpression,
        SmtValueKind expectedKind,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (!TryLowerNullableHasValueTerm(coalesceExpression.Left, context, out var leftHasValue) ||
            !TryLowerNullableValueTerm(coalesceExpression.Left, context, out var leftValue) ||
            !TryLowerNullableValueTerm(coalesceExpression.Right, context, out var rightValue) ||
            leftValue.Kind != expectedKind ||
            rightValue.Kind != expectedKind) {
            term = null!;
            return false;
        }

        term = new SymbolicConditionalTerm(
            SymbolicIrLowerer.CreateFactCondition(
                new SymbolicTruthAtom(leftHasValue),
                coalesceExpression.Left,
                "ir.nullable.coalesce.left-has-value"),
            leftValue,
            rightValue);
        return true;
    }

    private static bool TryLowerNullableConditionalValueTerm(
        ConditionalExpressionSyntax conditionalExpression,
        SmtValueKind expectedKind,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(conditionalExpression.Condition, context), out var condition) ||
            !TryLowerNullableValueTerm(conditionalExpression.WhenTrue, context, out var whenTrueValue) ||
            !TryLowerNullableValueTerm(conditionalExpression.WhenFalse, context, out var whenFalseValue) ||
            whenTrueValue.Kind != expectedKind ||
            whenFalseValue.Kind != expectedKind) {
            term = null!;
            return false;
        }

        term = new SymbolicConditionalTerm(condition, whenTrueValue, whenFalseValue);
        return true;
    }

    private static bool TryLowerNullableConditionalAccessValueTerm(
        ConditionalAccessExpressionSyntax conditionalAccess,
        SmtValueKind expectedKind,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(conditionalAccess.Expression, context), out var receiver) ||
            receiver.Kind != SmtValueKind.Reference)
            return false;

        return conditionalAccess.WhenNotNull switch {
            ElementBindingExpressionSyntax elementBinding => TryLowerConditionalAccessElementBindingValueTerm(
                conditionalAccess,
                elementBinding,
                receiver,
                expectedKind,
                context,
                out term),
            MemberBindingExpressionSyntax memberBinding => TryLowerConditionalAccessMemberBindingValueTerm(
                conditionalAccess,
                memberBinding,
                receiver,
                expectedKind,
                context,
                out term),
            _ => false
        };
    }

    private static bool TryLowerConditionalAccessElementBindingValueTerm(
        ConditionalAccessExpressionSyntax conditionalAccess,
        ElementBindingExpressionSyntax elementBinding,
        SymbolicTerm receiver,
        SmtValueKind expectedKind,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        var receiverType = context.SemanticModel.GetTypeInfo(conditionalAccess.Expression, context.CancellationToken)
            .Type;
        if (elementBinding.ArgumentList.Arguments.Count != 1 ||
            receiverType is not IArrayTypeSymbol { Rank: 1 } arrayType ||
            !SymbolicTypeLowerer.TryGetValueKind(arrayType.ElementType, out var elementKind) ||
            elementKind != expectedKind ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(elementBinding.ArgumentList.Arguments[0].Expression, context), out var index) ||
            index.Kind != SmtValueKind.Int) {
            term = null!;
            return false;
        }

        term = new SymbolicElementTerm(receiver, index, elementKind);
        return true;
    }

    private static bool TryLowerConditionalAccessMemberBindingValueTerm(
        ConditionalAccessExpressionSyntax conditionalAccess,
        MemberBindingExpressionSyntax memberBinding,
        SymbolicTerm receiver,
        SmtValueKind expectedKind,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (context.SemanticModel.GetSymbolInfo(memberBinding.Name, context.CancellationToken).Symbol is not
            { } memberSymbol ||
            !SymbolicTypeLowerer.TryGetSymbolType(memberSymbol, out var memberType) ||
            !SymbolicTypeLowerer.TryGetValueKind(memberType, out var memberKind) ||
            memberKind != expectedKind) {
            term = null!;
            return false;
        }

        var receiverType = context.SemanticModel.GetTypeInfo(conditionalAccess.Expression, context.CancellationToken)
            .Type;
        if (string.Equals(memberSymbol.Name, nameof(string.Length), StringComparison.Ordinal)) {
            if (receiverType?.SpecialType == SpecialType.System_String &&
                SymbolicStringLowerer.TryLowerStringTerm(conditionalAccess.Expression, context, out var stringValue)) {
                term = new SymbolicLengthTerm(stringValue);
                return true;
            }

            if (receiverType is IArrayTypeSymbol { Rank: 1 } ||
                SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(receiverType)) {
                term = new SymbolicLengthTerm(receiver);
                return true;
            }

            if (receiverType is IArrayTypeSymbol { Rank: > 1 } multiDimensionalArray &&
                SymbolicIndexingLowerer.TryLowerArrayTotalLengthTerm(
                    conditionalAccess.Expression, multiDimensionalArray, context, out term))
                return true;
        }

        term = new SymbolicMemberTerm(receiver, memberSymbol.Name, memberKind);
        return true;
    }

    internal static bool TryLowerNullableValueTerm(
        ExpressionSyntax nullableExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        var originalExpression = nullableExpression;
        nullableExpression = SymbolicLoweringValueFacts.UnwrapExpression(nullableExpression);
        var typeInfo = context.SemanticModel.GetTypeInfo(originalExpression, context.CancellationToken);
        var expressionType = typeInfo.ConvertedType ?? typeInfo.Type;
        if (!SymbolicTypeFacts.TryGetNullableUnderlyingType(expressionType, out var underlyingType) ||
            !SymbolicTypeLowerer.TryGetValueKind(underlyingType, out var valueKind)) {
            term = null!;
            return false;
        }

        if (SymbolicAsyncLowerer.TryGetKnownCompletedAsyncResultExpression(
                nullableExpression,
                context,
                out var completedResultExpression) &&
            TryLowerNullableValueTerm(completedResultExpression, context, out term))
            return true;

        if (SymbolicLoweringValueFacts.TryGetStableVariableSymbol(nullableExpression, context, out var symbol)) {
            term = new SymbolicNullableValueTerm(context.GetVariableName(symbol), valueKind);
            return true;
        }

        if (TryLowerNullLikeNullableValueTerm(nullableExpression, underlyingType, out term)) return true;

        if (nullableExpression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            TryLowerNullableCoalesceNullableValueTerm(coalesceExpression, valueKind, context, out term))
            return true;

        if (nullableExpression is ConditionalExpressionSyntax conditionalExpression &&
            TryLowerNullableConditionalValueTerm(conditionalExpression, valueKind, context, out term))
            return true;

        if (nullableExpression is ConditionalAccessExpressionSyntax conditionalAccess &&
            TryLowerNullableConditionalAccessValueTerm(conditionalAccess, valueKind, context, out term))
            return true;

        var valueExpression = nullableExpression is CastExpressionSyntax castExpression
            ? castExpression.Expression
            : nullableExpression;
        if ((valueExpression != nullableExpression ||
             !SymbolicTypeFacts.TryGetNullableUnderlyingType(typeInfo.Type, out _)) &&
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(valueExpression, context), out var valueTerm) &&
            valueTerm.Kind == valueKind) {
            term = valueTerm;
            return true;
        }

        term = null!;
        return false;
    }

    internal static bool TryLowerNullableCoalesceValueTerm(
        BinaryExpressionSyntax coalesceExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        var typeInfo = context.SemanticModel.GetTypeInfo(coalesceExpression, context.CancellationToken);
        var resultType = typeInfo.ConvertedType ?? typeInfo.Type;
        if (SymbolicTypeFacts.TryGetNullableUnderlyingType(resultType, out var resultUnderlyingType))
            resultType = resultUnderlyingType;
        if (resultType == null ||
            !SymbolicTypeLowerer.TryGetValueKind(resultType, out var resultKind) ||
            !TryLowerNullableHasValueTerm(coalesceExpression.Left, context, out var leftHasValue) ||
            !TryLowerNullableValueTerm(coalesceExpression.Left, context, out var leftValue) ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(coalesceExpression.Right, context), out var fallbackValue) ||
            leftValue.Kind != resultKind ||
            fallbackValue.Kind != resultKind) {
            term = null!;
            return false;
        }

        term = new SymbolicConditionalTerm(
            SymbolicIrLowerer.CreateFactCondition(
                new SymbolicTruthAtom(leftHasValue),
                coalesceExpression.Left,
                "ir.nullable.coalesce.left-has-value"),
            leftValue,
            fallbackValue);
        return true;
    }

    private static bool TryCreateDefaultTerm(ITypeSymbol type, out SymbolicTerm term) {
        if (type.SpecialType == SpecialType.System_Boolean) {
            term = new SymbolicBooleanConstantTerm(false);
            return true;
        }

        if (SymbolicTypeLowerer.TryGetValueKind(type, out var kind) &&
            kind == SmtValueKind.Int) {
            term = new SymbolicIntegerConstantTerm(0);
            return true;
        }

        term = null!;
        return false;
    }
}
