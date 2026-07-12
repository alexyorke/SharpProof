using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicSemanticPipeline
{
    internal static SymbolicLoweringResult<SymbolicTerm> LowerTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var term))
            return Exact(term, expression, "term");

        return Unsupported<SymbolicTerm>(expression, "term");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerCondition(expression, context, out var condition))
            return Exact(condition, expression, "condition");

        return Unsupported<SymbolicCondition>(expression, "condition");
    }

    internal static SymbolicLoweringResult<SymbolicState> LowerBranchFacts(
        ExpressionSyntax expression,
        bool branchWhenTrue,
        SymbolicLoweringContext context)
    {
        var lowered = LowerCondition(expression, context);
        if (!lowered.IsExact || lowered.Value == null)
            return Unsupported<SymbolicState>(expression, "branch-facts");

        var condition = branchWhenTrue
            ? lowered.Value
            : new SymbolicNotCondition(lowered.Value);
        return Exact(new SymbolicState(pathConditions: new[] { condition }), expression, "branch-facts");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerPattern(
        ExpressionSyntax valueExpression,
        PatternSyntax pattern,
        SymbolicLoweringContext context)
    {
        var value = LowerTerm(valueExpression, context);
        if (value.IsExact &&
            value.Value != null &&
            SymbolicIrLowerer.TryLowerPatternCondition(
                value.Value,
                context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken).ConvertedType ??
                context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken).Type,
                pattern,
                pattern,
                context,
                out var condition))
            return Exact(condition, pattern, "pattern");

        return Unsupported<SymbolicCondition>(pattern, "pattern");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerPatternCondition(
                value,
                valueType,
                pattern,
                source,
                context,
                out var condition))
            return Exact(condition, source, "pattern");

        return Unsupported<SymbolicCondition>(source, "pattern");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerPatternCondition(
        SymbolicTerm value,
        PatternSyntax pattern,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerPatternCondition(
                value,
                pattern,
                source,
                context,
                out var condition))
            return Exact(condition, source, "pattern");

        return Unsupported<SymbolicCondition>(source, "pattern");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerMemberOrIndexAccess(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (expression is not MemberAccessExpressionSyntax and not ElementAccessExpressionSyntax)
            return Unsupported<SymbolicTerm>(expression, "member-or-index");

        if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var term))
            return Exact(term, expression, "member-or-index");

        return Unsupported<SymbolicTerm>(expression, "member-or-index");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerConversion(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (expression is not CastExpressionSyntax and not CheckedExpressionSyntax)
            return Unsupported<SymbolicTerm>(expression, "conversion");

        if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var term))
            return Exact(term, expression, "conversion");

        return Unsupported<SymbolicTerm>(expression, "conversion");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerReferenceTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerReferenceTerm(expression, context, out var term))
            return Exact(term, expression, "reference-term");

        return Unsupported<SymbolicTerm>(expression, "reference-term");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerStringTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerStringTerm(expression, context, out var term))
            return Exact(term, expression, "string-term");

        return Unsupported<SymbolicTerm>(expression, "string-term");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerBooleanValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerBooleanValueTerm(expression, context, out var term))
            return Exact(term, expression, "boolean-term");

        return Unsupported<SymbolicTerm>(expression, "boolean-term");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerBuiltInLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerBuiltInLengthTerm(expression, context, out var term))
            return Exact(term, expression, "built-in-length");

        return Unsupported<SymbolicTerm>(expression, "built-in-length");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerLengthProjectionTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var value = LowerTerm(expression, context);
        if (value is { IsExact: true, Value: { } valueTerm } &&
            valueTerm.Kind is SmtValueKind.String or SmtValueKind.Reference)
            return Exact<SymbolicTerm>(new SymbolicLengthTerm(valueTerm), expression, "length-projection");

        return Unsupported<SymbolicTerm>(expression, "length-projection");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerArrayDimensionLengthTerm(
        ExpressionSyntax expression,
        int dimension,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerArrayDimensionLengthTerm(expression, dimension, context, out var term))
            return Exact(term, expression, "array-dimension-length");

        return Unsupported<SymbolicTerm>(expression, "array-dimension-length");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerNullableHasValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerNullableHasValueTerm(expression, context, out var term))
            return Exact(term, expression, "nullable-has-value");

        return Unsupported<SymbolicTerm>(expression, "nullable-has-value");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerNullableValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerNullableValueTerm(expression, context, out var term))
            return Exact(term, expression, "nullable-value");

        return Unsupported<SymbolicTerm>(expression, "nullable-value");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerStringNonNullCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerStringNonNullCondition(expression, context, out var condition))
            return Exact(condition, expression, "string-non-null");

        return Unsupported<SymbolicCondition>(expression, "string-non-null");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerBuiltInElementAccessInRangeCondition(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicLoweringContext context)
    {
        var receiverType = context.SemanticModel.GetTypeInfo(
                               elementAccess.Expression,
                               context.CancellationToken).ConvertedType ??
                           context.SemanticModel.GetTypeInfo(
                               elementAccess.Expression,
                               context.CancellationToken).Type;
        if (receiverType is IArrayTypeSymbol { Rank: > 1 } &&
            SymbolicIrLowerer.TryCreateArrayElementBoundsCondition(
                elementAccess.Expression,
                elementAccess.ArgumentList.Arguments.Select(static argument => argument.Expression).ToArray(),
                elementAccess,
                "ir.element-access.multidimensional-bounds.in-range",
                context,
                out var multidimensionalCondition,
                out _))
            return Exact(multidimensionalCondition, elementAccess, "element-access-in-range");

        if (elementAccess.ArgumentList.Arguments.Count == 1 &&
            SymbolicIrLowerer.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess.Expression,
                elementAccess.ArgumentList.Arguments[0].Expression,
                elementAccess,
                "ir.element-access.bounds.in-range",
                context,
                out var condition))
            return Exact(condition, elementAccess, "element-access-in-range");

        return Unsupported<SymbolicCondition>(elementAccess, "element-access-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerArrayElementBoundsCondition(
        ExpressionSyntax arrayExpression,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryCreateArrayElementBoundsCondition(
                arrayExpression,
                indexExpressions,
                source,
                "ir.array-element.bounds.in-range",
                context,
                out var condition,
                out _))
            return Exact(condition, source, "array-element-in-range");

        return Unsupported<SymbolicCondition>(source, "array-element-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerSubsequenceInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax startExpression,
        ExpressionSyntax? lengthExpression,
        SyntaxNode source,
        SymbolicLoweringContext context,
        bool oneArgumentUpperBoundIsInclusive = true)
    {
        if (SymbolicIrLowerer.TryCreateSubsequenceInRangeCondition(
                receiverExpression,
                startExpression,
                lengthExpression,
                source,
                "ir.subsequence.in-range",
                context,
                oneArgumentUpperBoundIsInclusive,
                out var condition))
            return Exact(condition, source, "subsequence-in-range");

        return Unsupported<SymbolicCondition>(source, "subsequence-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerIntegerInRangeCondition(
        ExpressionSyntax expression,
        long minValue,
        long maxValue,
        SymbolicLoweringContext context)
    {
        var lowering = LowerTerm(expression, context);
        if (lowering is { IsExact: true, Value: { Kind: SmtValueKind.Int } value })
            return Exact<SymbolicCondition>(
                SymbolicIrLowerer.CreateIntegerInRangeCondition(
                    value,
                    minValue,
                    maxValue,
                    expression,
                    "ir.integer.in-range"),
                expression,
                "integer-in-range");

        return Unsupported<SymbolicCondition>(expression, "integer-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerIntegerBinaryInRangeCondition(
        ExpressionSyntax leftExpression,
        ExpressionSyntax rightExpression,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        var left = LowerTerm(leftExpression, context);
        var right = LowerTerm(rightExpression, context);
        if (SymbolicIrLowerer.TryGetBinaryTermOperator(smtOperator, out var binaryOperator) &&
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

    internal static SymbolicLoweringResult<SymbolicCondition> LowerNegatedIntegerInRangeCondition(
        ExpressionSyntax expression,
        long minValue,
        long maxValue,
        SymbolicLoweringContext context)
    {
        var operand = LowerTerm(expression, context);
        if (operand is { IsExact: true, Value: { Kind: SmtValueKind.Int } operandTerm })
            return Exact(
                SymbolicIrLowerer.CreateIntegerInRangeCondition(
                    new SymbolicBinaryTerm(
                        SymbolicBinaryTermOperator.Subtract,
                        new SymbolicIntegerConstantTerm(0),
                        operandTerm),
                    minValue,
                    maxValue,
                    expression,
                    "ir.integer.unary.in-range"),
                expression,
                "integer-unary-in-range");

        return Unsupported<SymbolicCondition>(expression, "integer-unary-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerIntegerUpdateInRangeCondition(
        ExpressionSyntax expression,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SymbolicLoweringContext context)
    {
        var operand = LowerTerm(expression, context);
        if (SymbolicIrLowerer.TryGetBinaryTermOperator(smtOperator, out var binaryOperator) &&
            binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Subtract &&
            operand is { IsExact: true, Value: { Kind: SmtValueKind.Int } operandTerm })
            return Exact(
                SymbolicIrLowerer.CreateIntegerInRangeCondition(
                    new SymbolicBinaryTerm(
                        binaryOperator,
                        operandTerm,
                        new SymbolicIntegerConstantTerm(1)),
                    minValue,
                    maxValue,
                    expression,
                    "ir.integer.update.in-range"),
                expression,
                "integer-update-in-range");

        return Unsupported<SymbolicCondition>(expression, "integer-update-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerNegativeIntegerCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var operand = LowerTerm(expression, context);
        if (operand is { IsExact: true, Value: { Kind: SmtValueKind.Int } operandTerm })
            return Exact<SymbolicCondition>(
                new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicRelationAtom(
                        SymbolicRelationOperator.LessThan,
                        operandTerm,
                        new SymbolicIntegerConstantTerm(0)),
                    expression,
                    "ir.integer.negative")),
                expression,
                "integer-negative");

        return Unsupported<SymbolicCondition>(expression, "integer-negative");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerNullableHasValueCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var hasValue = LowerNullableHasValueTerm(expression, context);
        if (hasValue is { IsExact: true, Value: { } term })
            return Exact<SymbolicCondition>(
                new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicTruthAtom(term),
                    expression,
                    "ir.nullable.has-value")),
                expression,
                "nullable-has-value-condition");

        return Unsupported<SymbolicCondition>(expression, "nullable-has-value-condition");
    }

    internal static SymbolicLoweringResult<SymbolicFact> LowerRuntimeHazardTrigger(
        SymbolicExceptionPreconditionKind kind,
        SymbolicTerm? subject,
        SymbolicCondition trigger,
        SyntaxNode source,
        string detail)
    {
        if (trigger == null) throw new ArgumentNullException(nameof(trigger));

        var provenance = "ir.runtime-hazard." + detail;
        return Exact(
            SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(kind, subject, trigger),
                source,
                provenance,
                evidenceKey: provenance),
            source,
            "runtime-hazard");
    }

    private static SymbolicLoweringResult<T> Exact<T>(T value, SyntaxNode source, string stage)
        where T : class
    {
        return SymbolicLoweringResult<T>.Exact(value, CreateProvenance(source, stage, "exact"));
    }

    private static SymbolicLoweringResult<T> Unsupported<T>(SyntaxNode source, string stage)
        where T : class
    {
        return SymbolicLoweringResult<T>.Unsupported(CreateProvenance(source, stage, "unsupported"));
    }

    private static SymbolicLoweringProvenance CreateProvenance(
        SyntaxNode source,
        string stage,
        string detail)
    {
        return new SymbolicLoweringProvenance("roslyn-to-ir." + stage, source.Span, detail);
    }
}
