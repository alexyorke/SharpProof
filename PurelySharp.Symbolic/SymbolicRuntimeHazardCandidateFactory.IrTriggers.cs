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

            if (SymbolicIrFormulaEncoder.TryEncode(precondition, out var formula))
            {
                trigger = new RuntimeHazardTrigger(formula, precondition);
                return true;
            }

            trigger = default;
            return false;
        }

        private static bool TryCreateDivideByZeroTrigger(
            ExpressionSyntax divisor,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardTrigger trigger)
        {
            if (TryCreateIrExceptionPreconditionTrigger(
                    SymbolicExceptionPreconditionKind.DivideByZero,
                    divisor,
                    new SymbolicIntegerConstantTerm(0),
                    "ir.runtime-hazard.divide-by-zero",
                    semanticModel,
                    cancellationToken,
                    out trigger))
            {
                return true;
            }

            if (TryTranslateZeroCondition(divisor, semanticModel, cancellationToken, out var formula))
            {
                trigger = new RuntimeHazardTrigger(formula);
                return true;
            }

            trigger = default;
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
            {
                return true;
            }

            if (!CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                trigger = default;
                return false;
            }

            trigger = new RuntimeHazardTrigger(new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula));
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
                trigger = new RuntimeHazardTrigger(formula);
                return true;
            }

            trigger = default;
            return false;
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

        private static bool TryCreateNullDereferenceTrigger(
            ExpressionSyntax receiver,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardTrigger trigger)
        {
            if (IsStableIrReferenceSubject(receiver, semanticModel, cancellationToken) &&
                TryCreateIrRelationalExceptionPreconditionTrigger(
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
                trigger = new RuntimeHazardTrigger(formula);
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
            if (IsStableIrReferenceSubject(expression, semanticModel, cancellationToken) &&
                TryCreateIrRelationalExceptionPreconditionTrigger(
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
                trigger = new RuntimeHazardTrigger(formula);
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
            if (IsStableIrReferenceSubject(expression, semanticModel, cancellationToken) &&
                TryCreateIrRelationalExceptionPreconditionTrigger(
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
                trigger = new RuntimeHazardTrigger(formula);
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

            if (CSharpSmtFormulaTranslator.TryTranslateNullableHasValue(
                    nullableExpression,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula))
            {
                trigger = new RuntimeHazardTrigger(new SmtUnaryFormula(SmtUnaryOperator.Not, hasValueFormula));
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

            if (!CSharpSmtFormulaTranslator.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion: null) ||
                valueFormula is not { Kind: SmtValueKind.Reference } ||
                !CSharpSmtFormulaTranslator.TryCreateRuntimeTypeTestFormula(valueFormula, targetType, out var runtimeTypeTest))
            {
                return false;
            }

            trigger = new RuntimeHazardTrigger(Conjoin(
                CreateNonNullTrigger(expression, expression, semanticModel, cancellationToken),
                new SmtUnaryFormula(SmtUnaryOperator.Not, runtimeTypeTest)));
            return true;
        }

        private static bool IsStableIrReferenceSubject(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (expression is ThisExpressionSyntax)
            {
                return true;
            }

            if (expression is IdentifierNameSyntax)
            {
                var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
                return symbol is ILocalSymbol or IParameterSymbol;
            }

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
            SymbolicTerm subject,
            SymbolicCondition triggerCondition,
            SyntaxNode site,
            string provenance,
            out RuntimeHazardTrigger trigger)
        {
            var precondition = SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(kind, subject, triggerCondition),
                site,
                provenance);

            if (SymbolicIrFormulaEncoder.TryEncode(precondition, out var formula))
            {
                trigger = new RuntimeHazardTrigger(formula, precondition);
                return true;
            }

            trigger = default;
            return false;
        }

        private static bool TryCreateReferenceNullCondition(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            string provenance,
            out SymbolicCondition condition,
            out SmtFormula trigger)
        {
            expression = UnwrapExpression(expression);
            if (expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                condition = new SymbolicConstantCondition(true);
                trigger = new SmtBooleanConstant(true);
                return true;
            }

            if (expression is DefaultExpressionSyntax defaultExpression &&
                IsReferenceLikeType(GetExpressionType(defaultExpression, semanticModel, cancellationToken)))
            {
                condition = new SymbolicConstantCondition(true);
                trigger = new SmtBooleanConstant(true);
                return true;
            }

            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (!SymbolicIrLowerer.TryLowerTerm(expression, context, out var term) ||
                term.Kind != SmtValueKind.Reference)
            {
                condition = null!;
                trigger = null!;
                return false;
            }

            condition = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    term,
                    new SymbolicNullTerm()),
                expression,
                provenance));
            return SymbolicIrFormulaEncoder.TryEncode(condition, out trigger);
        }
    }
}
