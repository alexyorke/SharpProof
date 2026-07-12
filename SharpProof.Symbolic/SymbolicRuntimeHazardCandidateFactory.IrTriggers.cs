using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed partial class SymbolicRuntimeHazardQueryService
{
    private static bool TryCreateDirectThrowTrigger(
        SyntaxNode throwNode,
        out RuntimeHazardTrigger trigger)
    {
        var precondition = SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DirectThrow,
                null,
                new SymbolicConstantCondition(true)),
            throwNode,
            "ir.runtime-hazard.direct-throw");

        return RuntimeHazardTrigger.TryCreate(precondition, out trigger);
    }

    private static bool TryCreateDivideByZeroTrigger(
        ExpressionSyntax divisor,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        if (TryCreateNumericZeroCondition(
                divisor,
                semanticModel,
                cancellationToken,
                "ir.runtime-hazard.divide-by-zero.trigger",
                out var subject,
                out var zeroCondition) &&
            TryEncodeIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.DivideByZero,
                subject,
                zeroCondition,
                divisor,
                "ir.runtime-hazard.divide-by-zero",
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            divisor,
            SymbolicExceptionPreconditionKind.DivideByZero,
            null,
            "ir.runtime-hazard.divide-by-zero.unsupported");
        return true;
    }

    private static bool TryCreateNumericZeroCondition(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out SymbolicTerm? subject,
        out SymbolicCondition condition)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerNumericZeroCondition(expression, context);
        if (lowering is { IsExact: true, Value: { } zeroCondition })
        {
            condition = zeroCondition;
            subject = zeroCondition is SymbolicFactCondition
                {
                    Fact.Atom: SymbolicRelationAtom { Left: var left }
                }
                ? left
                : null;
            return true;
        }

        subject = null;
        condition = null!;
        return false;
    }

    private static bool TryCreateIndexOrRangeTrigger(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicRuntimeHazardKind kind,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        if (TryCreateIrElementAccessOutOfRangeTrigger(
                elementAccess,
                kind,
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        var preconditionKind = kind == SymbolicRuntimeHazardKind.ArgumentOutOfRange
            ? SymbolicExceptionPreconditionKind.ArgumentOutOfRange
            : SymbolicExceptionPreconditionKind.IndexOutOfRange;
        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            elementAccess,
            preconditionKind,
            null,
            "ir.runtime-hazard.index.out-of-range.unsupported");
        return true;
    }

    private static bool TryCreateIrElementAccessOutOfRangeTrigger(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicRuntimeHazardKind kind,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (TryCreateIrSafeAbsModuloLengthIndexTrigger(
                elementAccess,
                kind,
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        if (GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken) is IArrayTypeSymbol
            {
                Rank: > 1
            } arrayType)
            return TryCreateIrMultidimensionalArrayElementAccessOutOfRangeTrigger(
                elementAccess,
                kind,
                arrayType,
                semanticModel,
                cancellationToken,
                out trigger);

        if (elementAccess.ArgumentList.Arguments.Count != 1 ||
            IsBuiltInRangeAccessArgument(
                elementAccess.ArgumentList.Arguments[0].Expression,
                semanticModel,
                cancellationToken))
            return false;

        var indexExpression = elementAccess.ArgumentList.Arguments[0].Expression;
        var indexType = GetExpressionType(indexExpression, semanticModel, cancellationToken);
        if (indexType?.SpecialType != SpecialType.System_Int32) return false;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var indexLowering = SymbolicSemanticPipeline.LowerTerm(indexExpression, context);
        if (indexLowering is not { IsExact: true, Value: { } index } ||
            index.Kind != SmtValueKind.Int ||
            !TryCreateIrElementAccessLengthTerm(elementAccess, semanticModel, cancellationToken, context,
                out var length))
            return false;

        var inRangeCondition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                index,
                length,
                true,
                true),
            elementAccess,
            "ir.runtime-hazard.index.bounds.in-range"));
        var outOfRangeCondition = new SymbolicNotCondition(inRangeCondition);
        var preconditionKind = kind == SymbolicRuntimeHazardKind.ArgumentOutOfRange
            ? SymbolicExceptionPreconditionKind.ArgumentOutOfRange
            : SymbolicExceptionPreconditionKind.IndexOutOfRange;

        return TryEncodeIrExceptionPreconditionTrigger(
            preconditionKind,
            index,
            outOfRangeCondition,
            elementAccess,
            "ir.runtime-hazard.index.out-of-range",
            out trigger);
    }

    private static bool TryCreateIrSafeAbsModuloLengthIndexTrigger(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicRuntimeHazardKind kind,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (elementAccess.ArgumentList.Arguments.Count != 1) return false;

        var indexExpression = UnwrapExpression(elementAccess.ArgumentList.Arguments[0].Expression);
        if (indexExpression is not InvocationExpressionSyntax absInvocation ||
            !CSharpMathPatternRecognizer.TryGetMathAbsRemainderOperands(
                absInvocation,
                semanticModel,
                cancellationToken,
                out _,
                out var divisorExpression))
            return false;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var sourceLengthLowering = SymbolicSemanticPipeline.LowerBuiltInLengthTerm(elementAccess.Expression, context);
        var divisorLengthLowering = SymbolicSemanticPipeline.LowerTerm(divisorExpression, context);
        if (sourceLengthLowering is not { IsExact: true, Value: { } sourceLength } ||
            sourceLength.Kind != SmtValueKind.Int ||
            divisorLengthLowering is not { IsExact: true, Value: { } divisorLength } ||
            divisorLength.Kind != SmtValueKind.Int ||
            !Equals(sourceLength, divisorLength))
            return false;

        var preconditionKind = kind == SymbolicRuntimeHazardKind.ArgumentOutOfRange
            ? SymbolicExceptionPreconditionKind.ArgumentOutOfRange
            : SymbolicExceptionPreconditionKind.IndexOutOfRange;
        return TryEncodeIrExceptionPreconditionTrigger(
            preconditionKind,
            null,
            new SymbolicConstantCondition(false),
            elementAccess,
            "ir.runtime-hazard.index.abs-modulo.same-length-unreachable",
            out trigger);
    }

    private static bool TryCreateIrMultidimensionalArrayElementAccessOutOfRangeTrigger(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicRuntimeHazardKind kind,
        IArrayTypeSymbol arrayType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (arrayType.Rank <= 1 ||
            elementAccess.ArgumentList.Arguments.Count != arrayType.Rank)
            return false;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var receiver = SymbolicSemanticPipeline.LowerTerm(elementAccess.Expression, context);
        var bounds = SymbolicSemanticPipeline.LowerArrayElementBoundsCondition(
            elementAccess.Expression,
            elementAccess.ArgumentList.Arguments.Select(static argument => argument.Expression).ToArray(),
            elementAccess,
            context);
        if (receiver is not { IsExact: true, Value: { Kind: SmtValueKind.Reference } subject } ||
            bounds is not { IsExact: true, Value: { } inRangeCondition })
            return false;

        var preconditionKind = kind == SymbolicRuntimeHazardKind.ArgumentOutOfRange
            ? SymbolicExceptionPreconditionKind.ArgumentOutOfRange
            : SymbolicExceptionPreconditionKind.IndexOutOfRange;
        return TryEncodeIrExceptionPreconditionTrigger(
            preconditionKind,
            subject,
            new SymbolicNotCondition(inRangeCondition),
            elementAccess,
            "ir.runtime-hazard.index.multidimensional-out-of-range",
            out trigger);
    }

    private static bool TryCreateIrElementAccessLengthTerm(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicLoweringContext context,
        out SymbolicTerm length)
    {
        length = null!;
        var lengthLowering = SymbolicSemanticPipeline.LowerBuiltInLengthTerm(elementAccess.Expression, context);
        if (lengthLowering is { IsExact: true, Value: { } loweredLength })
        {
            length = loweredLength;
            return true;
        }

        if (IsCountBackedIntIndexerElementAccess(elementAccess, semanticModel, cancellationToken))
        {
            var receiverLowering = SymbolicSemanticPipeline.LowerTerm(elementAccess.Expression, context);
            if (receiverLowering is not { IsExact: true, Value: { } receiver })
            {
                length = null!;
                return false;
            }

            if (receiver.Kind != SmtValueKind.Reference) return false;

            length = new SymbolicCountTerm(receiver);
            return true;
        }

        return false;
    }

    private static bool TryCreateIrArrayGetValueIndexOutOfRangeTrigger(
        InvocationExpressionSyntax invocation,
        IInvocationOperation invocationOperation,
        ExpressionSyntax receiverExpression,
        IArrayTypeSymbol arrayType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (arrayType.Rank <= 0 ||
            invocationOperation.Arguments.Length != arrayType.Rank)
            return false;

        var indexExpressions = new List<ExpressionSyntax>(arrayType.Rank);
        for (var dimension = 0; dimension < arrayType.Rank; dimension++)
        {
            if (!SymbolicValueFacts.TryGetInvocationArgumentExpressionByOrdinal(invocationOperation, dimension,
                    out var indexExpression) ||
                GetExpressionType(indexExpression, semanticModel, cancellationToken)?.SpecialType !=
                SpecialType.System_Int32)
                return false;

            indexExpressions.Add(indexExpression);
        }

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var receiver = SymbolicSemanticPipeline.LowerTerm(receiverExpression, context);
        var bounds = SymbolicSemanticPipeline.LowerArrayElementBoundsCondition(
            receiverExpression,
            indexExpressions,
            invocation,
            context);
        if (receiver is not { IsExact: true, Value: { Kind: SmtValueKind.Reference } subject } ||
            bounds is not { IsExact: true, Value: { } inRangeCondition })
            return false;

        return TryEncodeIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.IndexOutOfRange,
            subject,
            new SymbolicNotCondition(inRangeCondition),
            invocation,
            arrayType.Rank == 1
                ? "ir.runtime-hazard.array-get-value.index-out-of-range"
                : "ir.runtime-hazard.array-get-value.multidimensional-index-out-of-range",
            out trigger);
    }

    private static bool TryCreateNegativeLengthTrigger(
        ExpressionSyntax lengthExpression,
        SymbolicExceptionPreconditionKind kind,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        if (TryCreateIrRelationalExceptionPreconditionTrigger(
                kind,
                lengthExpression,
                SymbolicRelationOperator.LessThan,
                new SymbolicIntegerConstantTerm(0),
                provenance,
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            lengthExpression,
            kind,
            null,
            provenance + ".unsupported");
        return true;
    }

    private static bool TryCreateCheckedIntegralOutOfRangeTrigger(
        ExpressionSyntax expression,
        long minValue,
        long maxValue,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        if (lowering is not { IsExact: true, Value: { } value } ||
            value.Kind != SmtValueKind.Int)
            return false;

        var lowerOverflow = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                value,
                new SymbolicIntegerConstantTerm(minValue)),
            expression,
            provenance + ".below-min"));
        var upperOverflow = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                value,
                new SymbolicIntegerConstantTerm(maxValue)),
            expression,
            provenance + ".above-max"));
        var outOfRange = new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            lowerOverflow,
            upperOverflow);

        return TryEncodeIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            value,
            outOfRange,
            expression,
            provenance,
            out trigger);
    }

    private static bool TryCreateCheckedSignedDivisionOverflowTrigger(
        SyntaxNode site,
        ExpressionSyntax leftExpression,
        ExpressionSyntax rightExpression,
        long minValue,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var leftLowering = SymbolicSemanticPipeline.LowerTerm(leftExpression, context);
        var rightLowering = SymbolicSemanticPipeline.LowerTerm(rightExpression, context);
        if (leftLowering is not { IsExact: true, Value: { } left } ||
            left.Kind != SmtValueKind.Int ||
            rightLowering is not { IsExact: true, Value: { } right } ||
            right.Kind != SmtValueKind.Int)
            return false;

        var overflowCondition = SymbolicIrLowerer.CreateSignedDivisionOverflowCondition(
            left,
            right,
            minValue,
            site,
            provenance);

        return TryEncodeIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            left,
            overflowCondition,
            site,
            provenance,
            out trigger);
    }

    private static bool TryCreateCheckedEqualityOverflowTrigger(
        SyntaxNode site,
        ExpressionSyntax expression,
        long overflowingValue,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        if (lowering is not { IsExact: true, Value: { } value } ||
            value.Kind != SmtValueKind.Int)
            return false;

        var overflowCondition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                value,
                new SymbolicIntegerConstantTerm(overflowingValue)),
            expression,
            provenance + ".operand"));

        return TryEncodeIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            value,
            overflowCondition,
            site,
            provenance,
            out trigger);
    }

    private static bool TryCreateNullDereferenceTrigger(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        if (NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                receiver,
                semanticModel,
                cancellationToken))
        {
            trigger = default;
            return false;
        }

        if (TryCreateIrRelationalExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.NullDereference,
                receiver,
                SymbolicRelationOperator.Equal,
                new SymbolicNullTerm(),
                "ir.runtime-hazard.null-dereference",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            receiver,
            SymbolicExceptionPreconditionKind.NullDereference,
            null,
            "ir.runtime-hazard.null-dereference.unsupported");
        return true;
    }

    private static bool TryCreateUnboxNullTrigger(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        if (TryCreateIrRelationalExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.UnboxNull,
                expression,
                SymbolicRelationOperator.Equal,
                new SymbolicNullTerm(),
                "ir.runtime-hazard.unbox-null",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            expression,
            SymbolicExceptionPreconditionKind.UnboxNull,
            null,
            "ir.runtime-hazard.unbox-null.unsupported");
        return true;
    }

    private static bool TryCreateArgumentNullTrigger(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        if (TryCreateIrRelationalExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.ArgumentNull,
                expression,
                SymbolicRelationOperator.Equal,
                new SymbolicNullTerm(),
                "ir.runtime-hazard.argument-null",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            expression,
            SymbolicExceptionPreconditionKind.ArgumentNull,
            null,
            "ir.runtime-hazard.argument-null.unsupported");
        return true;
    }

    private static bool TryCreateNullableValueWithoutValueTrigger(
        ExpressionSyntax nullableExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerNullableHasValueTerm(nullableExpression, context);
        if (lowering is { IsExact: true, Value: { } hasValueTerm } &&
            hasValueTerm is SymbolicNullableHasValueTerm nullableHasValue)
        {
            var hasValueCondition = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicTruthAtom(hasValueTerm),
                nullableExpression,
                "ir.runtime-hazard.nullable-value.has-value"));
            return TryEncodeIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.NullableValueWithoutValue,
                new SymbolicVariableTerm(nullableHasValue.NullableName, SmtValueKind.Reference),
                new SymbolicNotCondition(hasValueCondition),
                nullableExpression,
                "ir.runtime-hazard.nullable-value.without-value",
                out trigger);
        }

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            nullableExpression,
            SymbolicExceptionPreconditionKind.NullableValueWithoutValue,
            null,
            "ir.runtime-hazard.nullable-value.without-value.unsupported");
        return true;
    }

    private static bool TryCreateRuntimeReferenceInvalidCastTrigger(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;

        if (SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(targetType, out var typeKey))
        {
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
            if (lowering is { IsExact: true, Value: { } value } &&
                value.Kind == SmtValueKind.Reference)
            {
                var nonNull = new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicRelationAtom(
                        SymbolicRelationOperator.NotEqual,
                        value,
                        new SymbolicNullTerm()),
                    expression,
                    "ir.runtime-hazard.invalid-cast.non-null"));

                var isTargetType = new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicTypeTestAtom(value, typeKey),
                    expression,
                    "ir.runtime-hazard.invalid-cast.type-test"));
                var invalidCast = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    nonNull,
                    new SymbolicNotCondition(isTargetType));
                if (TryEncodeIrExceptionPreconditionTrigger(
                        SymbolicExceptionPreconditionKind.InvalidCast,
                        value,
                        invalidCast,
                        expression,
                        "ir.runtime-hazard.invalid-cast.mismatch",
                        out trigger))
                    return true;
            }
        }

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            expression,
            SymbolicExceptionPreconditionKind.InvalidCast,
            null,
            "ir.runtime-hazard.invalid-cast.unsupported");
        return true;
    }

    private static bool TryCreateExactRuntimeInvalidCastTrigger(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        SymbolicTerm? subject = null;
        _ = TryCreateOptionalReferenceSubject(expression, semanticModel, cancellationToken, out subject);
        if (TryCreateReferenceNullCondition(
                expression,
                semanticModel,
                cancellationToken,
                "ir.runtime-hazard.reference.non-null.guard",
                out var nullCondition) &&
            TryEncodeIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.InvalidCast,
                subject,
                new SymbolicNotCondition(nullCondition),
                expression,
                "ir.runtime-hazard.invalid-cast.exact-mismatch",
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            expression,
            SymbolicExceptionPreconditionKind.InvalidCast,
            subject,
            "ir.runtime-hazard.invalid-cast.exact-mismatch.unsupported");
        return true;
    }

    private static bool TryCreateDynamicNullBindingTrigger(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        if (TryCreateReferenceNullCondition(
                receiver,
                semanticModel,
                cancellationToken,
                "ir.runtime-hazard.dynamic-null-binding.trigger",
                out var condition) &&
            TryCreateOptionalReferenceSubject(receiver, semanticModel, cancellationToken, out var subject) &&
            TryEncodeIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.DynamicNullBinding,
                subject,
                condition,
                receiver,
                "ir.runtime-hazard.dynamic-null-binding",
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            receiver,
            SymbolicExceptionPreconditionKind.DynamicNullBinding,
            null,
            "ir.runtime-hazard.dynamic-null-binding.unsupported");
        return true;
    }

    private static bool TryCreateInvalidCollectionCardinalityTrigger(
        ExpressionSyntax receiver,
        SymbolicRelationOperator relation,
        long triggeringCount,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerBuiltInLengthTerm(receiver, context);
        if (lowering is not { IsExact: true, Value: { } count } ||
            count.Kind != SmtValueKind.Int)
            return false;

        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                relation,
                count,
                new SymbolicIntegerConstantTerm(triggeringCount)),
            receiver,
            "ir.runtime-hazard.collection-cardinality.trigger"));
        return TryEncodeIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.InvalidCollectionCardinality,
            count,
            condition,
            receiver,
            "ir.runtime-hazard.collection-cardinality",
            out trigger);
    }

    private static bool TryCreateIrRelationalExceptionPreconditionTrigger(
        SymbolicExceptionPreconditionKind kind,
        ExpressionSyntax subjectExpression,
        SymbolicRelationOperator relation,
        SymbolicTerm triggeringValue,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(subjectExpression, context);
        if (lowering is not { IsExact: true, Value: { } subject } ||
            subject.Kind != triggeringValue.Kind)
            return false;

        var triggerCondition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                relation,
                subject,
                triggeringValue),
            subjectExpression,
            provenance + ".trigger"));
        return TryEncodeIrExceptionPreconditionTrigger(
            kind,
            subject,
            triggerCondition,
            subjectExpression,
            provenance,
            out trigger);
    }

    private static bool TryEncodeIrExceptionPreconditionTrigger(
        SymbolicExceptionPreconditionKind kind,
        SymbolicTerm? subject,
        SymbolicCondition triggerCondition,
        SyntaxNode site,
        string provenance,
        out RuntimeHazardTrigger trigger)
    {
        var precondition = SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(kind, subject, triggerCondition),
            site,
            provenance);

        return RuntimeHazardTrigger.TryCreate(precondition, out trigger);
    }

    private static bool TryCreateOptionalReferenceSubject(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm? subject)
    {
        expression = UnwrapExpression(expression);
        if (expression.IsKind(SyntaxKind.NullLiteralExpression) ||
            (expression is DefaultExpressionSyntax defaultExpression &&
             IsReferenceLikeType(GetExpressionType(defaultExpression, semanticModel, cancellationToken))))
        {
            subject = null;
            return true;
        }

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        if (lowering is { IsExact: true, Value: { } term } &&
            term.Kind == SmtValueKind.Reference)
        {
            subject = term;
            return true;
        }

        subject = null;
        return false;
    }

    private static bool TryCreateReferenceNullCondition(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out SymbolicCondition condition)
    {
        expression = UnwrapExpression(expression);
        if (expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            condition = new SymbolicConstantCondition(true);
            return true;
        }

        if (expression is DefaultExpressionSyntax defaultExpression &&
            IsReferenceLikeType(GetExpressionType(defaultExpression, semanticModel, cancellationToken)))
        {
            condition = new SymbolicConstantCondition(true);
            return true;
        }

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        if (lowering is not { IsExact: true, Value: { } term } ||
            term.Kind != SmtValueKind.Reference)
        {
            condition = null!;
            return false;
        }

        condition = SymbolicIrLowerer.CreateReferenceNullCondition(
            term,
            true,
            expression,
            provenance);
        return true;
    }
}
