namespace SharpProof.Symbolic.Ir;

internal static class SymbolicConversionLowerer {
    internal static bool TryLowerDecimalZeroComparison(
        BinaryExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!SymbolicOperatorLowerer.TryGetRelationOperator(expression.Kind(), out var relation)) return false;

        if (TryLowerDecimalZeroOperands(expression.Left, expression.Right, context, out var value)) {
            condition = SymbolicIrLowerer.CreateRelationCondition(
                relation,
                value,
                new SymbolicIntegerConstantTerm(0),
                expression,
                "ir.decimal-zero-comparison");
            return true;
        }

        if (!TryLowerDecimalZeroOperands(expression.Right, expression.Left, context, out value)) return false;
        relation = ReverseRelation(relation);
        condition = SymbolicIrLowerer.CreateRelationCondition(
            relation,
            value,
            new SymbolicIntegerConstantTerm(0),
            expression,
            "ir.decimal-zero-comparison.reversed");
        return true;
    }

    private static bool TryLowerDecimalZeroOperands(
        ExpressionSyntax valueExpression,
        ExpressionSyntax zeroExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm value) {
        value = null!;
        var zeroType = context.SemanticModel.GetTypeInfo(zeroExpression, context.CancellationToken);
        var zeroConstant = context.SemanticModel.GetConstantValue(zeroExpression, context.CancellationToken);
        if ((zeroType.ConvertedType ?? zeroType.Type)?.SpecialType != SpecialType.System_Decimal ||
            !zeroConstant.HasValue ||
            zeroConstant.Value is not decimal decimalValue ||
            decimalValue != 0m)
            return false;

        var symbol = context.SemanticModel.GetSymbolInfo(valueExpression, context.CancellationToken).Symbol;
        if (symbol is not ILocalSymbol and not IParameterSymbol ||
            context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken).Type?.SpecialType !=
            SpecialType.System_Decimal)
            return false;

        value = context.TryGetSubstitution(symbol, out var substituted)
            ? substituted
            : new SymbolicVariableTerm(context.GetVariableName(symbol), SmtValueKind.Int);
        return value.Kind == SmtValueKind.Int;
    }

    private static SymbolicRelationOperator ReverseRelation(SymbolicRelationOperator relation) => relation switch {
        SymbolicRelationOperator.LessThan => SymbolicRelationOperator.GreaterThan,
        SymbolicRelationOperator.LessThanOrEqual => SymbolicRelationOperator.GreaterThanOrEqual,
        SymbolicRelationOperator.GreaterThan => SymbolicRelationOperator.LessThan,
        SymbolicRelationOperator.GreaterThanOrEqual => SymbolicRelationOperator.LessThanOrEqual,
        _ => relation
    };

    internal static bool TryLowerCheckedIntegralConversionComparison(
        BinaryExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!SymbolicOperatorLowerer.TryGetRelationOperator(expression.Kind(), out var relation)) return false;

        var castOnLeft = TryGetCheckedIntegralCast(expression.Left, context, out var cast, out var targetType);
        if (!castOnLeft && !TryGetCheckedIntegralCast(expression.Right, context, out cast, out targetType))
            return false;

        var otherExpression = castOnLeft ? expression.Right : expression.Left;
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(cast.Expression, context), out var operand) ||
            operand.Kind != SmtValueKind.Int ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(otherExpression, context), out var other) ||
            other.Kind != SmtValueKind.Int ||
            !SymbolicTypeFacts.TryGetBoundedIntegralRange(targetType, out var minimum, out var maximum))
            return false;

        if (!castOnLeft) relation = ReverseRelation(relation);

        var bounds = SymbolicIrLowerer.CreateIntegerInRangeCondition(
            operand,
            minimum,
            maximum,
            cast,
            "ir.checked-conversion.normal-return");
        var comparison = SymbolicIrLowerer.CreateRelationCondition(
            relation,
            operand,
            other,
            expression,
            "ir.checked-conversion.value");
        condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, bounds, comparison);
        return true;
    }

    private static bool TryGetCheckedIntegralCast(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out CastExpressionSyntax cast,
        out ITypeSymbol targetType) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        cast = null!;
        targetType = null!;
        if (expression is CheckedExpressionSyntax {
            Keyword.RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.CheckedKeyword,
            Expression: CastExpressionSyntax checkedCast
        })
            cast = checkedCast;
        else if (expression is CastExpressionSyntax directCast &&
                 context.SemanticModel.GetOperation(directCast, context.CancellationToken) is
                     Microsoft.CodeAnalysis.Operations.IConversionOperation { IsChecked: true })
            cast = directCast;
        else
            return false;

        var sourceType = context.SemanticModel.GetTypeInfo(cast.Expression, context.CancellationToken).Type;
        var resolvedTargetType = context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type;
        if (sourceType == null || resolvedTargetType == null) return false;

        targetType = resolvedTargetType;
        return
               TryGetIntegralShape(sourceType.SpecialType, out _, out _) &&
               TryGetIntegralShape(targetType.SpecialType, out _, out _);
    }

    internal static bool TryLowerReferenceAsTerm(
        BinaryExpressionSyntax asExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (asExpression.Right is not TypeSyntax targetTypeSyntax ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(asExpression.Left, context), out var operand) ||
            operand.Kind != SmtValueKind.Reference)
            return false;

        if (IsIdentityPreservingReferenceConversion(asExpression.Left, targetTypeSyntax, context)) {
            term = operand;
            return true;
        }

        var sourceType = context.SemanticModel.GetTypeInfo(asExpression.Left, context.CancellationToken).Type;
        var targetType = context.SemanticModel.GetTypeInfo(targetTypeSyntax, context.CancellationToken).Type;
        if (sourceType?.IsReferenceType != true ||
            targetType?.IsReferenceType != true ||
            !SymbolicPatternLowerer.TryLowerTypeTestCondition(
                operand,
                targetTypeSyntax,
                asExpression,
                false,
                context,
                out var typeTest))
            return false;

        term = new SymbolicConditionalTerm(typeTest, operand, new SymbolicNullTerm());
        return true;
    }

    internal static bool TryLowerUnsignedCastBoundsComparison(
        BinaryExpressionSyntax binaryExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!binaryExpression.IsKind(SyntaxKind.LessThanExpression) &&
            !binaryExpression.IsKind(SyntaxKind.GreaterThanOrEqualExpression))
            return false;

        if (!TryGetUnsignedCastOperand(binaryExpression.Left, context, out var indexExpression,
                out var leftUnsignedType) ||
            !TryGetUnsignedCastOperand(binaryExpression.Right, context, out var lengthExpression,
                out var rightUnsignedType) ||
            leftUnsignedType != rightUnsignedType ||
            !IsKnownNonNegativeIntegralExpression(lengthExpression, context) ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(indexExpression, context), out var index) ||
            index.Kind != SmtValueKind.Int ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(lengthExpression, context), out var length) ||
            length.Kind != SmtValueKind.Int)
            return false;

        var inRange = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicBoundsAtom(index, length, true, true),
            binaryExpression,
            "ir.conversion.unsigned-bounds");
        condition = binaryExpression.IsKind(SyntaxKind.LessThanExpression)
            ? inRange
            : new SymbolicNotCondition(inRange);
        return true;
    }

    private static bool TryGetUnsignedCastOperand(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out ExpressionSyntax operand,
        out SpecialType unsignedType) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is CastExpressionSyntax castExpression &&
            context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type?.SpecialType is
                SpecialType.System_UInt32 or SpecialType.System_UInt64) {
            operand = castExpression.Expression;
            unsignedType = context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type!
                .SpecialType;
            return true;
        }

        operand = null!;
        unsignedType = SpecialType.None;
        return false;
    }

    private static bool IsKnownNonNegativeIntegralExpression(
        ExpressionSyntax expression,
        SymbolicLoweringContext context) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue &&
            SymbolicLoweringValueFacts.TryGetIntegralConstant(constantValue.Value!, out var integralValue))
            return integralValue >= 0;

        if (expression is not MemberAccessExpressionSyntax memberAccess ||
            !string.Equals(memberAccess.Name.Identifier.ValueText, "Length", StringComparison.Ordinal))
            return false;

        var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken)
            .ConvertedType ??
                           context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        return receiverType?.SpecialType == SpecialType.System_String ||
               receiverType is IArrayTypeSymbol ||
               SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(receiverType);
    }

    private static bool IsIdentityPreservingReferenceConversion(
        ExpressionSyntax expression,
        TypeSyntax targetTypeSyntax,
        SymbolicLoweringContext context) {
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

    internal static bool TryLowerSupportedConversionTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (expression is CheckedExpressionSyntax checkedExpression &&
            checkedExpression.IsKind(SyntaxKind.UncheckedExpression)) {
            if (checkedExpression.Expression is CastExpressionSyntax)
                return TryLowerSupportedConversionTerm(checkedExpression.Expression, context, out term);

            term = null!;
            return false;
        }

        if (expression is CastExpressionSyntax castExpression) {
            if (context.SemanticModel.GetOperation(castExpression, context.CancellationToken) is
                    Microsoft.CodeAnalysis.Operations.IConversionOperation { Conversion.IsIdentity: true } &&
                SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(castExpression.Expression, context), out var identityOperand)) {
                term = identityOperand;
                return true;
            }

            if (IsIdentityPreservingReferenceConversion(castExpression.Expression, castExpression.Type, context) &&
                SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(castExpression.Expression, context), out var referenceOperand) &&
                referenceOperand.Kind == SmtValueKind.Reference) {
                term = referenceOperand;
                return true;
            }

            var sourceType = context.SemanticModel.GetTypeInfo(castExpression.Expression, context.CancellationToken)
                .Type;
            var targetType = context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type;
            if (sourceType != null &&
                targetType != null &&
                IsValuePreservingIntegralConversion(sourceType, targetType) &&
                SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(castExpression.Expression, context), out var operand) &&
                operand.Kind == SmtValueKind.Int) {
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
        out SymbolicTerm term) {
        term = null!;
        if (!IsNumericSpecialType(sourceType.SpecialType) ||
            !TryGetIntegralShape(targetType.SpecialType, out _, out _))
            return false;

        string operandIdentity;
        if (SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(castExpression.Expression, context), out var operand)) {
            operandIdentity = SymbolicState.CreateProofTermKey(operand);
        }
        else {
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

    private static bool IsNumericSpecialType(SpecialType specialType) => specialType is SpecialType.System_SByte or
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

    private static bool IsValuePreservingIntegralConversion(ITypeSymbol sourceType, ITypeSymbol targetType) {
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

    private static bool TryGetIntegralShape(SpecialType specialType, out bool signed, out int bits) {
        switch (specialType) {
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
