using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

using static SharpProof.Symbolic.SymbolicRuntimeHazardSyntaxFacts;
using static SharpProof.Symbolic.SymbolicRuntimeHazardTriggerFactory;
namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeHazardIrTriggerFactory
{
    internal static bool TryCreateDirectThrowTrigger(
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

    internal static bool TryCreateDivideByZeroTrigger(
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
            TryCreateIrExceptionPreconditionTrigger(
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

    internal static bool TryCreateNumericZeroCondition(
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

    internal static bool TryCreateIndexOrRangeTrigger(
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

    internal static bool TryCreateIrElementAccessOutOfRangeTrigger(
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

        if (CSharpSyntaxFacts.GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken) is IArrayTypeSymbol
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

        if (elementAccess.ArgumentList.Arguments.Count != 1) return false;

        var indexExpression = elementAccess.ArgumentList.Arguments[0].Expression;
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var boundsLowering = SymbolicSemanticPipeline.LowerBuiltInElementAccessOutOfRangeCondition(
            elementAccess,
            context);
        if (boundsLowering is not { IsExact: true, Value: { } outOfRangeCondition }) return false;

        SymbolicTerm? subject = null;
        var indexLowering = SymbolicSemanticPipeline.LowerTerm(indexExpression, context);
        if (indexLowering is { IsExact: true, Value: { } index })
            subject = index;
        else
        {
            var receiverLowering = SymbolicSemanticPipeline.LowerTerm(elementAccess.Expression, context);
            if (receiverLowering is { IsExact: true, Value: { Kind: SmtValueKind.Reference } receiver })
                subject = receiver;
        }

        var preconditionKind = kind == SymbolicRuntimeHazardKind.ArgumentOutOfRange
            ? SymbolicExceptionPreconditionKind.ArgumentOutOfRange
            : SymbolicExceptionPreconditionKind.IndexOutOfRange;

        return TryCreateIrExceptionPreconditionTrigger(
            preconditionKind,
            subject,
            outOfRangeCondition,
            elementAccess,
            "ir.runtime-hazard.index.out-of-range",
            out trigger);
    }

    internal static bool TryCreateIrSafeAbsModuloLengthIndexTrigger(
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
        return TryCreateIrExceptionPreconditionTrigger(
            preconditionKind,
            null,
            new SymbolicConstantCondition(false),
            elementAccess,
            "ir.runtime-hazard.index.abs-modulo.same-length-unreachable",
            out trigger);
    }

    internal static bool TryCreateIrMultidimensionalArrayElementAccessOutOfRangeTrigger(
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
        return TryCreateIrExceptionPreconditionTrigger(
            preconditionKind,
            subject,
            new SymbolicNotCondition(inRangeCondition),
            elementAccess,
            "ir.runtime-hazard.index.multidimensional-out-of-range",
            out trigger);
    }

    internal static bool TryCreateIrArrayGetValueIndexOutOfRangeTrigger(
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
                CSharpSyntaxFacts.GetExpressionType(indexExpression, semanticModel, cancellationToken)?.SpecialType !=
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

        return TryCreateIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.IndexOutOfRange,
            subject,
            new SymbolicNotCondition(inRangeCondition),
            invocation,
            arrayType.Rank == 1
                ? "ir.runtime-hazard.array-get-value.index-out-of-range"
                : "ir.runtime-hazard.array-get-value.multidimensional-index-out-of-range",
            out trigger);
    }

    internal static bool TryCreateNegativeLengthTrigger(
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

    internal static bool TryCreateCheckedIntegralOutOfRangeTrigger(
        ExpressionSyntax expression,
        long minValue,
        long maxValue,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (!TryLowerExactIntegerTerm(expression, semanticModel, cancellationToken, out var value))
            return false;

        var lowerOverflow = CreateExactIntegerRelationCondition(
            value, SymbolicRelationOperator.LessThan, minValue, expression, provenance + ".below-min");
        var upperOverflow = CreateExactIntegerRelationCondition(
            value, SymbolicRelationOperator.GreaterThan, maxValue, expression, provenance + ".above-max");
        var outOfRange = new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            lowerOverflow,
            upperOverflow);

        return TryCreateIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            value,
            outOfRange,
            expression,
            provenance,
            out trigger);
    }

    internal static bool TryCreateCheckedSignedDivisionOverflowTrigger(
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
        if (!TryLowerExactTerm(leftExpression, SmtValueKind.Int, context, out var left) ||
            !TryLowerExactTerm(rightExpression, SmtValueKind.Int, context, out var right))
            return false;

        var overflowCondition = SymbolicIrLowerer.CreateSignedDivisionOverflowCondition(
            left,
            right,
            minValue,
            site,
            provenance);

        return TryCreateIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            left,
            overflowCondition,
            site,
            provenance,
            out trigger);
    }

    internal static bool TryCreateCheckedEqualityOverflowTrigger(
        SyntaxNode site,
        ExpressionSyntax expression,
        long overflowingValue,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (!TryLowerExactIntegerTerm(expression, semanticModel, cancellationToken, out var value))
            return false;

        var overflowCondition = CreateExactIntegerRelationCondition(
            value, SymbolicRelationOperator.Equal, overflowingValue, expression, provenance + ".operand");

        return TryCreateIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            value,
            overflowCondition,
            site,
            provenance,
            out trigger);
    }

    internal static bool TryCreateNullDereferenceTrigger(
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

        return TryCreateNullExceptionTrigger(
            receiver,
            SymbolicExceptionPreconditionKind.NullDereference,
            "ir.runtime-hazard.null-dereference",
            semanticModel,
            cancellationToken,
            out trigger);
    }

    internal static bool TryCreateUnboxNullTrigger(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        return TryCreateNullExceptionTrigger(
            expression,
            SymbolicExceptionPreconditionKind.UnboxNull,
            "ir.runtime-hazard.unbox-null",
            semanticModel,
            cancellationToken,
            out trigger);
    }

    internal static bool TryCreateArgumentNullTrigger(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        return TryCreateNullExceptionTrigger(
            expression,
            SymbolicExceptionPreconditionKind.ArgumentNull,
            "ir.runtime-hazard.argument-null",
            semanticModel,
            cancellationToken,
            out trigger);
    }

    internal static bool TryCreateNullExceptionTrigger(
        ExpressionSyntax expression,
        SymbolicExceptionPreconditionKind kind,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        if (TryCreateIrRelationalExceptionPreconditionTrigger(
                kind,
                expression,
                SymbolicRelationOperator.Equal,
                new SymbolicNullTerm(),
                provenance,
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            expression,
            kind,
            null,
            provenance + ".unsupported");
        return true;
    }

    internal static bool TryCreateNullableValueWithoutValueTrigger(
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
            return TryCreateIrExceptionPreconditionTrigger(
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

    internal static bool TryCreateRuntimeReferenceInvalidCastTrigger(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;

        if (SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(targetType, out var typeKey))
        {
            if (TryLowerExactTerm(
                    expression,
                    SmtValueKind.Reference,
                    semanticModel,
                    cancellationToken,
                    out var value))
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
                if (TryCreateIrExceptionPreconditionTrigger(
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

    internal static bool TryCreateExactRuntimeInvalidCastTrigger(
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
            TryCreateIrExceptionPreconditionTrigger(
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

    internal static bool TryCreateDynamicNullBindingTrigger(
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
            TryCreateIrExceptionPreconditionTrigger(
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

    internal static bool TryCreateInvalidCollectionCardinalityTrigger(
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
        return TryCreateIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.InvalidCollectionCardinality,
            count,
            condition,
            receiver,
            "ir.runtime-hazard.collection-cardinality",
            out trigger);
    }

    internal static bool TryCreateIrRelationalExceptionPreconditionTrigger(
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
        if (!TryLowerExactTerm(
                subjectExpression,
                triggeringValue.Kind,
                semanticModel,
                cancellationToken,
                out var subject))
            return false;

        var triggerCondition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                relation,
                subject,
                triggeringValue),
            subjectExpression,
            provenance + ".trigger"));
        return TryCreateIrExceptionPreconditionTrigger(
            kind,
            subject,
            triggerCondition,
            subjectExpression,
            provenance,
            out trigger);
    }

    internal static bool TryCreateIrExceptionPreconditionTrigger(
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

    internal static bool TryCreateOptionalReferenceSubject(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm? subject)
    {
        return TryLowerOptionalReference(
            expression,
            semanticModel,
            cancellationToken,
            out _,
            out subject,
            out _);
    }

    internal static bool TryCreateReferenceNullCondition(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out SymbolicCondition condition)
    {
        if (!TryLowerOptionalReference(
                expression,
                semanticModel,
                cancellationToken,
                out var normalizedExpression,
                out var term,
                out var isNull))
        {
            condition = null!;
            return false;
        }

        if (isNull)
        {
            condition = new SymbolicConstantCondition(true);
            return true;
        }

        condition = SymbolicIrLowerer.CreateReferenceNullCondition(
            term!,
            true,
            normalizedExpression,
            provenance);
        return true;
    }

    internal static bool TryLowerOptionalReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax normalizedExpression,
        out SymbolicTerm? term,
        out bool isNull)
    {
        normalizedExpression = UnwrapExpression(expression);
        isNull = normalizedExpression.IsKind(SyntaxKind.NullLiteralExpression) ||
                 (normalizedExpression is DefaultExpressionSyntax defaultExpression &&
                  IsReferenceLikeType(CSharpSyntaxFacts.GetExpressionType(defaultExpression, semanticModel, cancellationToken)));
        if (isNull)
        {
            term = null;
            return true;
        }

        if (TryLowerExactTerm(
                normalizedExpression,
                SmtValueKind.Reference,
                semanticModel,
                cancellationToken,
                out var exactTerm))
        {
            term = exactTerm;
            return true;
        }

        term = null;
        return false;
    }

    internal static bool TryLowerExactTerm(
        ExpressionSyntax expression,
        SmtValueKind expectedKind,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm term)
    {
        return TryLowerExactTerm(
            expression,
            expectedKind,
            new SymbolicLoweringContext(semanticModel, cancellationToken),
            out term);
    }

    internal static bool TryLowerExactIntegerTerm(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm term)
    {
        return TryLowerExactTerm(
            expression,
            SmtValueKind.Int,
            semanticModel,
            cancellationToken,
            out term);
    }

    internal static SymbolicFactCondition CreateExactIntegerRelationCondition(
        SymbolicTerm value,
        SymbolicRelationOperator relation,
        long constant,
        SyntaxNode source,
        string provenance)
    {
        return new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                relation,
                value,
                new SymbolicIntegerConstantTerm(constant)),
            source,
            provenance));
    }

    internal static bool TryLowerExactTerm(
        ExpressionSyntax expression,
        SmtValueKind expectedKind,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        if (lowering is { IsExact: true, Value: { } value } && value.Kind == expectedKind)
        {
            term = value;
            return true;
        }

        term = null!;
        return false;
    }
}
