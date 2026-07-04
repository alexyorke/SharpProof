using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    internal sealed partial class SymbolicRuntimeHazardQueryService
    {
        private static bool TryCreateDirectThrowTrigger(
            SyntaxNode throwNode,
            out RuntimeHazardTrigger trigger)
        {
            var precondition = SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(
                    SymbolicExceptionPreconditionKind.DirectThrow,
                    Subject: null,
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
            {
                return true;
            }

            if (TryTranslateZeroCondition(divisor, semanticModel, cancellationToken, out var formula))
            {
                trigger = CreateFormulaBackedExceptionPreconditionTrigger(
                    divisor,
                    SymbolicExceptionPreconditionKind.DivideByZero,
                    subject: null,
                    formula,
                    "ir.runtime-hazard.divide-by-zero.formula-fallback");
                return true;
            }

            trigger = default;
            return false;
        }

        private static bool TryCreateNumericZeroCondition(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            string provenance,
            out SymbolicTerm? subject,
            out SymbolicCondition condition)
        {
            expression = UnwrapExpression(expression);
            if (semanticModel.GetConstantValue(expression, cancellationToken) is { HasValue: true } constant)
            {
                if (IsIntegralOrDecimalZero(constant.Value))
                {
                    subject = null;
                    condition = new SymbolicConstantCondition(true);
                    return true;
                }

                if (constant.Value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal)
                {
                    subject = null;
                    condition = new SymbolicConstantCondition(false);
                    return true;
                }
            }

            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var term) &&
                term.Kind == SmtValueKind.Int)
            {
                subject = term;
                condition = new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicRelationAtom(
                        SymbolicRelationOperator.Equal,
                        term,
                        new SymbolicIntegerConstantTerm(0)),
                    expression,
                    provenance));
                return true;
            }

            if (TryCreateDecimalZeroComparableTerm(expression, semanticModel, cancellationToken, out var decimalTerm))
            {
                subject = decimalTerm;
                condition = new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicRelationAtom(
                        SymbolicRelationOperator.Equal,
                        decimalTerm,
                        new SymbolicIntegerConstantTerm(0)),
                    expression,
                    provenance));
                return true;
            }

            subject = null;
            condition = null!;
            return false;
        }

        private static bool TryCreateDecimalZeroComparableTerm(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SymbolicTerm term)
        {
            expression = UnwrapExpression(expression);
            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is not ILocalSymbol and not IParameterSymbol ||
                semanticModel.GetTypeInfo(expression, cancellationToken).Type?.SpecialType != SpecialType.System_Decimal)
            {
                term = null!;
                return false;
            }

            term = new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(symbol), SmtValueKind.Int);
            return true;
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
            {
                return true;
            }

            if (!SymbolicReachabilityService.TryCreateBuiltInElementAccessInRangeCondition(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                trigger = default;
                return false;
            }

            var preconditionKind = kind == SymbolicRuntimeHazardKind.ArgumentOutOfRange
                ? SymbolicExceptionPreconditionKind.ArgumentOutOfRange
                : SymbolicExceptionPreconditionKind.IndexOutOfRange;
            trigger = CreateFormulaBackedExceptionPreconditionTrigger(
                elementAccess,
                preconditionKind,
                subject: null,
                new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula),
                "ir.runtime-hazard.index.out-of-range.formula-fallback");
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
            if (elementAccess.ArgumentList.Arguments.Count != 1 ||
                IsBuiltInRangeAccessArgument(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken))
            {
                return false;
            }

            var indexExpression = elementAccess.ArgumentList.Arguments[0].Expression;
            var indexType = GetExpressionType(indexExpression, semanticModel, cancellationToken);
            if (indexType?.SpecialType != SpecialType.System_Int32)
            {
                return false;
            }

            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (!SymbolicIrLowerer.TryLowerTerm(indexExpression, context, out var index) ||
                index.Kind != SmtValueKind.Int ||
                !TryCreateIrElementAccessLengthTerm(elementAccess, semanticModel, cancellationToken, context, out var length))
            {
                return false;
            }

            var inRangeCondition = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    index,
                    length,
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
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

        private static bool TryCreateIrElementAccessLengthTerm(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SymbolicLoweringContext context,
            out SymbolicTerm length)
        {
            length = null!;
            var receiverType = GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken);
            if (!SymbolicIrLowerer.TryLowerTerm(elementAccess.Expression, context, out var receiver))
            {
                return false;
            }

            if (receiverType?.SpecialType == SpecialType.System_String)
            {
                length = receiver.Kind == SmtValueKind.String
                    ? new SymbolicLengthTerm(receiver)
                    : receiver.Kind == SmtValueKind.Reference
                        ? new SymbolicLengthTerm(new SymbolicStringContentTerm(receiver))
                        : null!;
                return length != null;
            }

            if (receiverType is IArrayTypeSymbol { Rank: 1 } ||
                IsBuiltInSpanType(receiverType))
            {
                if (receiver.Kind != SmtValueKind.Reference)
                {
                    return false;
                }

                length = new SymbolicLengthTerm(receiver);
                return true;
            }

            if (IsCountBackedIntIndexerElementAccess(elementAccess, semanticModel, cancellationToken))
            {
                if (receiver.Kind != SmtValueKind.Reference)
                {
                    return false;
                }

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
            if (arrayType.Rank != 1 ||
                invocationOperation.Arguments.Length != 1 ||
                !TryGetInvocationArgumentExpression(invocationOperation, parameterIndex: 0, out var indexExpression))
            {
                return false;
            }

            var indexType = GetExpressionType(indexExpression, semanticModel, cancellationToken);
            if (indexType?.SpecialType != SpecialType.System_Int32)
            {
                return false;
            }

            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (!SymbolicIrLowerer.TryLowerTerm(indexExpression, context, out var index) ||
                index.Kind != SmtValueKind.Int ||
                !SymbolicIrLowerer.TryLowerTerm(receiverExpression, context, out var receiver) ||
                receiver.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var inRangeCondition = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    index,
                    new SymbolicLengthTerm(receiver),
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
                invocation,
                "ir.runtime-hazard.array-get-value.bounds.in-range"));

            return TryEncodeIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.IndexOutOfRange,
                index,
                new SymbolicNotCondition(inRangeCondition),
                invocation,
                "ir.runtime-hazard.array-get-value.index-out-of-range",
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
            {
                return true;
            }

            if (TryTranslateNegativeCondition(lengthExpression, semanticModel, cancellationToken, out var formula))
            {
                trigger = CreateIrPreferredFormulaBackedExceptionPreconditionTrigger(
                    lengthExpression,
                    kind,
                    subject: null,
                    formula,
                    provenance + ".translated",
                    provenance + ".formula-fallback");
                return true;
            }

            trigger = default;
            return false;
        }

        private static bool TryCreateIrExceptionPreconditionTriggerFromFormula(
            SyntaxNode site,
            SymbolicExceptionPreconditionKind kind,
            SmtFormula formula,
            string provenance,
            out RuntimeHazardTrigger trigger)
        {
            trigger = default;
            if (!SymbolicSmtFormulaLowerer.TryLowerCondition(
                    formula,
                    site,
                    provenance + ".trigger",
                    provenance + ".trigger",
                    out var condition))
            {
                return false;
            }

            return TryEncodeIrExceptionPreconditionTrigger(
                kind,
                subject: null,
                condition,
                site,
                provenance,
                out trigger);
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
            if (!SymbolicIrLowerer.TryLowerTerm(expression, context, out var value) ||
                value.Kind != SmtValueKind.Int)
            {
                return false;
            }

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
            if (!SymbolicIrLowerer.TryLowerTerm(leftExpression, context, out var left) ||
                left.Kind != SmtValueKind.Int ||
                !SymbolicIrLowerer.TryLowerTerm(rightExpression, context, out var right) ||
                right.Kind != SmtValueKind.Int)
            {
                return false;
            }

            var leftIsMinValue = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    left,
                    new SymbolicIntegerConstantTerm(minValue)),
                leftExpression,
                provenance + ".left-min"));
            var rightIsMinusOne = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    right,
                    new SymbolicIntegerConstantTerm(-1)),
                rightExpression,
                provenance + ".right-minus-one"));
            var overflowCondition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                leftIsMinValue,
                rightIsMinusOne);

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
            if (!SymbolicIrLowerer.TryLowerTerm(expression, context, out var value) ||
                value.Kind != SmtValueKind.Int)
            {
                return false;
            }

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
            if (TryCreateIrRelationalExceptionPreconditionTrigger(
                    SymbolicExceptionPreconditionKind.NullDereference,
                    receiver,
                    SymbolicRelationOperator.Equal,
                    new SymbolicNullTerm(),
                    "ir.runtime-hazard.null-dereference",
                    semanticModel,
                    cancellationToken,
                    out trigger))
            {
                return true;
            }

            if (TryTranslateNullCondition(receiver, semanticModel, cancellationToken, out var formula))
            {
                trigger = CreateIrPreferredFormulaBackedExceptionPreconditionTrigger(
                    receiver,
                    SymbolicExceptionPreconditionKind.NullDereference,
                    subject: null,
                    formula,
                    "ir.runtime-hazard.null-dereference.translated",
                    "ir.runtime-hazard.null-dereference.formula-fallback");
                return true;
            }

            trigger = default;
            return false;
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
            {
                return true;
            }

            if (TryTranslateNullCondition(expression, semanticModel, cancellationToken, out var formula))
            {
                trigger = CreateIrPreferredFormulaBackedExceptionPreconditionTrigger(
                    expression,
                    SymbolicExceptionPreconditionKind.UnboxNull,
                    subject: null,
                    formula,
                    "ir.runtime-hazard.unbox-null.translated",
                    "ir.runtime-hazard.unbox-null.formula-fallback");
                return true;
            }

            trigger = default;
            return false;
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
            {
                return true;
            }

            if (TryTranslateNullCondition(expression, semanticModel, cancellationToken, out var formula))
            {
                trigger = CreateIrPreferredFormulaBackedExceptionPreconditionTrigger(
                    expression,
                    SymbolicExceptionPreconditionKind.ArgumentNull,
                    subject: null,
                    formula,
                    "ir.runtime-hazard.argument-null.translated",
                    "ir.runtime-hazard.argument-null.formula-fallback");
                return true;
            }

            trigger = default;
            return false;
        }

        private static bool TryCreateNullableValueWithoutValueTrigger(
            ExpressionSyntax nullableExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardTrigger trigger)
        {
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (SymbolicIrLowerer.TryLowerNullableHasValueTerm(nullableExpression, context, out var hasValueTerm) &&
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

            if (SymbolicReachabilityService.TryCreateNullableHasValueCondition(
                    nullableExpression,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula))
            {
                trigger = CreateIrPreferredFormulaBackedExceptionPreconditionTrigger(
                    nullableExpression,
                    SymbolicExceptionPreconditionKind.NullableValueWithoutValue,
                    subject: null,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, hasValueFormula),
                    "ir.runtime-hazard.nullable-value.without-value.translated",
                    "ir.runtime-hazard.nullable-value.without-value.formula-fallback");
                return true;
            }

            trigger = default;
            return false;
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
                if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var value) &&
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
                    {
                        return true;
                    }
                }
            }

            if (!SymbolicReachabilityService.TryCreateRuntimeTypeTestCondition(
                    expression,
                    targetType,
                    semanticModel,
                    cancellationToken,
                    out var runtimeTypeTest))
            {
                return false;
            }

            var triggerFormula = Conjoin(
                CreateNonNullTrigger(expression, expression, semanticModel, cancellationToken),
                new SmtUnaryFormula(SmtUnaryOperator.Not, runtimeTypeTest));
            trigger = CreateIrPreferredFormulaBackedExceptionPreconditionTrigger(
                expression,
                SymbolicExceptionPreconditionKind.InvalidCast,
                subject: null,
                triggerFormula,
                "ir.runtime-hazard.invalid-cast.translated",
                "ir.runtime-hazard.invalid-cast.formula-fallback");
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
            {
                return true;
            }

            if (TryTranslateNullCondition(receiver, semanticModel, cancellationToken, out var formula))
            {
                trigger = CreateIrPreferredFormulaBackedExceptionPreconditionTrigger(
                    receiver,
                    SymbolicExceptionPreconditionKind.DynamicNullBinding,
                    subject: null,
                    formula,
                    "ir.runtime-hazard.dynamic-null-binding.translated",
                    "ir.runtime-hazard.dynamic-null-binding.formula-fallback");
                return true;
            }

            trigger = default;
            return false;
        }

        private static bool TryCreateIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind kind,
            ExpressionSyntax subjectExpression,
            SymbolicTerm triggeringValue,
            string provenance,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardTrigger trigger)
        {
            return TryCreateIrRelationalExceptionPreconditionTrigger(
                kind,
                subjectExpression,
                SymbolicRelationOperator.Equal,
                triggeringValue,
                provenance,
                semanticModel,
                cancellationToken,
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
            if (!SymbolicIrLowerer.TryLowerTerm(subjectExpression, context, out var subject) ||
                subject.Kind != triggeringValue.Kind)
            {
                return false;
            }

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
                expression is DefaultExpressionSyntax defaultExpression &&
                IsReferenceLikeType(GetExpressionType(defaultExpression, semanticModel, cancellationToken)))
            {
                subject = null;
                return true;
            }

            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var term) &&
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
            if (!SymbolicIrLowerer.TryLowerTerm(expression, context, out var term) ||
                term.Kind != SmtValueKind.Reference)
            {
                condition = null!;
                return false;
            }

            condition = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    term,
                    new SymbolicNullTerm()),
                expression,
                provenance));
            return true;
        }
    }
}
