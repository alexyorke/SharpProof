using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;
using static SharpProof.Symbolic.SymbolicRuntimeHazardIrTriggerFactory;
using static SharpProof.Symbolic.SymbolicRuntimeHazardSyntaxFacts;

namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeHazardTriggerFactory
{
    internal static RuntimeHazardTrigger CreateAggregateExceptionPreconditionTrigger(
        SyntaxNode site,
        SymbolicExceptionPreconditionKind kind,
        SymbolicTerm? subject,
        SymbolicCondition? triggerCondition,
        bool allTriggersAreExact,
        string provenance)
    {
        if (triggerCondition == null)
            return CreateUnsupportedExceptionPreconditionTrigger(
                site,
                kind,
                subject,
                provenance + ".unsupported");

        if (!allTriggersAreExact)
            return CreateUnsupportedExceptionPreconditionTrigger(
                site,
                kind,
                subject,
                provenance + ".unsupported");

        var precondition = new SymbolicFact(
            new SymbolicExceptionPreconditionAtom(kind, subject, triggerCondition),
            true,
            SymbolicFactConfidence.Exact,
            provenance,
            site.Span,
            null,
            provenance);
        if (!RuntimeHazardTrigger.TryCreate(precondition, out var trigger))
            throw new InvalidOperationException("Could not encode aggregate runtime-hazard precondition.");

        return trigger;
    }

    internal static bool TryGetExceptionPrecondition(
        RuntimeHazardTrigger trigger,
        SymbolicExceptionPreconditionKind kind,
        out SymbolicExceptionPreconditionAtom precondition)
    {
        if (trigger.Precondition.Atom is SymbolicExceptionPreconditionAtom candidate &&
            candidate.Kind == kind)
        {
            precondition = candidate;
            return true;
        }

        precondition = null!;
        return false;
    }

    internal static bool TryCreateSwitchExpressionNoMatchCandidate(
        SwitchExpressionSyntax switchExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        SymbolicCondition? anyArmSelected = null;
        foreach (var arm in switchExpression.Arms)
        {
            if (!SwitchPathConditionBuilder.TryCreateSwitchExpressionArmSymbolicCondition(
                    switchExpression.GoverningExpression,
                    arm,
                    semanticModel,
                    cancellationToken,
                    out var armCondition))
            {
                candidate = new RuntimeHazardCandidate(
                    switchExpression,
                    SymbolicRuntimeHazardKind.SwitchExpressionNoMatch,
                    CreateUnsupportedExceptionPreconditionTrigger(
                        switchExpression,
                        SymbolicExceptionPreconditionKind.SwitchExpressionNoMatch,
                        null,
                        "ir.runtime-hazard.switch-expression.no-match.unsupported"),
                    ExceptionTypes.SwitchExpressionException,
                    ExceptionCategories.DefiniteSwitchExpressionNoMatch);
                return true;
            }

            anyArmSelected = anyArmSelected == null
                ? armCondition
                : new SymbolicBinaryCondition(SymbolicConditionOperator.Or, anyArmSelected, armCondition);
        }

        if (anyArmSelected == null) return false;
        var triggerCondition = new SymbolicNotCondition(anyArmSelected);
        if (!TryCreateIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.SwitchExpressionNoMatch,
                null,
                triggerCondition,
                switchExpression,
                "ir.runtime-hazard.switch-expression.no-match",
                out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            switchExpression,
            SymbolicRuntimeHazardKind.SwitchExpressionNoMatch,
            trigger,
            ExceptionTypes.SwitchExpressionException,
            ExceptionCategories.DefiniteSwitchExpressionNoMatch);
        return true;
    }

    internal static RuntimeHazardTrigger CreateUnsupportedExceptionPreconditionTrigger(
        SyntaxNode site,
        SymbolicExceptionPreconditionKind kind,
        SymbolicTerm? subject,
        string provenance)
    {
        var unknownVariableName =
            "unsupported_typed_projection#" + site.SpanStart.ToString(CultureInfo.InvariantCulture) +
            "_" + site.Span.End.ToString(CultureInfo.InvariantCulture);
        var unsupportedTriggerFact = new SymbolicFact(
            new SymbolicTruthAtom(new SymbolicVariableTerm(unknownVariableName, SmtValueKind.Bool)),
            true,
            SymbolicFactConfidence.Exact,
            provenance + ".trigger",
            site.Span,
            null,
            provenance + ".trigger");
        var unsupportedPrecondition = new SymbolicFact(
            new SymbolicExceptionPreconditionAtom(
                kind,
                subject,
                new SymbolicFactCondition(unsupportedTriggerFact)),
            true,
            SymbolicFactConfidence.Unsupported,
            provenance,
            site.Span,
            null,
            provenance);
        if (!RuntimeHazardTrigger.TryCreate(unsupportedPrecondition, out var trigger))
            throw new InvalidOperationException("Could not encode unsupported runtime-hazard precondition.");

        return trigger;
    }

    internal static bool TryCreateCheckedIntegralBinaryOverflowTrigger(
        BinaryExpressionSyntax binaryExpression,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (IsSignedDivisionOverflowOperator(smtOperator) &&
            TryCreateCheckedSignedDivisionOverflowTrigger(
                binaryExpression,
                binaryExpression.Left,
                binaryExpression.Right,
                minValue,
                "ir.runtime-hazard.checked-integral.signed-division-overflow",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var inRangeLowering = SymbolicSemanticPipeline.LowerIntegerBinaryInRangeCondition(
            binaryExpression.Left,
            binaryExpression.Right,
            smtOperator,
            minValue,
            maxValue,
            binaryExpression,
            context);
        if (!IsSignedDivisionOverflowOperator(smtOperator) &&
            inRangeLowering is { IsExact: true, Value: { } inRangeCondition } &&
            TryCreateIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                new SymbolicNotCondition(inRangeCondition),
                binaryExpression,
                "ir.runtime-hazard.checked-integral.binary-overflow",
                out var irTrigger))
        {
            trigger = irTrigger;
            return true;
        }

        if (IsSignedDivisionOverflowOperator(smtOperator))
        {
            trigger = CreateUnsupportedExceptionPreconditionTrigger(
                binaryExpression,
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                "ir.runtime-hazard.checked-integral.signed-division-overflow.unsupported");
            return true;
        }

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            binaryExpression,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            null,
            "ir.runtime-hazard.checked-integral.binary-overflow.unsupported");
        return true;
    }

    internal static bool IsSignedDivisionOverflowOperator(SmtIntegerBinaryOperator smtOperator)
    {
        return smtOperator is SmtIntegerBinaryOperator.Divide or SmtIntegerBinaryOperator.Remainder;
    }

    internal static bool TryCreateCheckedIntegralUnaryOverflowTrigger(
        PrefixUnaryExpressionSyntax unaryExpression,
        long minValue,
        long maxValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (TryCreateCheckedEqualityOverflowTrigger(
                unaryExpression,
                unaryExpression.Operand,
                minValue,
                "ir.runtime-hazard.checked-integral.unary-minus-overflow",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            unaryExpression,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            null,
            "ir.runtime-hazard.checked-integral.unary-minus-overflow.unsupported");
        return true;
    }

    internal static bool TryCreateCheckedIntegralUpdateOverflowTrigger(
        ExpressionSyntax site,
        ExpressionSyntax operand,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        var overflowingOperand = smtOperator == SmtIntegerBinaryOperator.Add ? maxValue : minValue;
        if (TryCreateCheckedEqualityOverflowTrigger(
                site,
                operand,
                overflowingOperand,
                smtOperator == SmtIntegerBinaryOperator.Add
                    ? "ir.runtime-hazard.checked-integral.increment-overflow"
                    : "ir.runtime-hazard.checked-integral.decrement-overflow",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        var unsupportedProvenance = smtOperator == SmtIntegerBinaryOperator.Add
            ? "ir.runtime-hazard.checked-integral.increment-overflow.unsupported"
            : "ir.runtime-hazard.checked-integral.decrement-overflow.unsupported";
        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            site,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            null,
            unsupportedProvenance);
        return true;
    }

    internal static bool TryCreateCheckedIntegralCompoundAssignmentOverflowTrigger(
        AssignmentExpressionSyntax assignment,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (IsSignedDivisionOverflowOperator(smtOperator) &&
            TryCreateCheckedSignedDivisionOverflowTrigger(
                assignment,
                assignment.Left,
                assignment.Right,
                minValue,
                "ir.runtime-hazard.checked-integral.compound-signed-division-overflow",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        if (IsSignedDivisionOverflowOperator(smtOperator))
        {
            trigger = CreateUnsupportedExceptionPreconditionTrigger(
                assignment,
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                "ir.runtime-hazard.checked-integral.compound-signed-division-overflow.unsupported");
            return true;
        }

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var inRangeLowering = SymbolicSemanticPipeline.LowerIntegerBinaryInRangeCondition(
            assignment.Left,
            assignment.Right,
            smtOperator,
            minValue,
            maxValue,
            assignment,
            context);
        if (inRangeLowering is { IsExact: true, Value: { } inRangeCondition })
        {
            SymbolicTerm? subject = null;
            var leftLowering = SymbolicSemanticPipeline.LowerTerm(assignment.Left, context);
            if (leftLowering is { IsExact: true, Value: { } left }) subject = left;

            return TryCreateIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                subject,
                new SymbolicNotCondition(inRangeCondition),
                assignment,
                "ir.runtime-hazard.checked-integral.compound-assignment-overflow",
                out trigger);
        }

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            assignment,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            null,
            "ir.runtime-hazard.checked-integral.compound-assignment-overflow.unsupported");
        return true;
    }

    internal static bool TryCreateCheckedExplicitNumericConversionOverflowTrigger(
        CastExpressionSyntax castExpression,
        long minValue,
        long maxValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (TryCreateCheckedIntegralOutOfRangeTrigger(
                castExpression.Expression,
                minValue,
                maxValue,
                "ir.runtime-hazard.checked-conversion.overflow",
                semanticModel,
                cancellationToken,
                out var irTrigger))
        {
            trigger = irTrigger;
            return true;
        }

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            castExpression,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            null,
            "ir.runtime-hazard.checked-conversion.overflow.unsupported");
        return true;
    }

    internal static bool TryGetCheckedIntegralBinaryOperator(
        BinaryExpressionSyntax binaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtIntegerBinaryOperator smtOperator,
        out long minValue,
        out long maxValue)
    {
        smtOperator = default;
        minValue = default;
        maxValue = default;

        if (!TryGetCheckedIntegralRange(binaryExpression, semanticModel, cancellationToken, out minValue,
                out maxValue) ||
            semanticModel.GetOperation(binaryExpression, cancellationToken) is not IBinaryOperation
            {
                OperatorMethod: null
            } operation)
            return false;

        return TryGetOverflowRelevantBinaryOperator(
            binaryExpression.Kind(), operation.IsChecked, minValue, out smtOperator);
    }

    internal static bool TryGetCheckedIntegralUnaryOperator(
        PrefixUnaryExpressionSyntax unaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long minValue,
        out long maxValue)
    {
        minValue = default;
        maxValue = default;
        return unaryExpression.IsKind(SyntaxKind.UnaryMinusExpression) &&
               TryGetCheckedIntegralRange(unaryExpression, semanticModel, cancellationToken, out minValue,
                   out maxValue) &&
               semanticModel.GetOperation(unaryExpression, cancellationToken) is IUnaryOperation
               {
                   IsChecked: true,
                   OperatorMethod: null
               };
    }

    internal static bool TryGetCheckedIntegralIncrementOrDecrementOperator(
        ExpressionSyntax updateExpression,
        ExpressionSyntax operand,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtIntegerBinaryOperator smtOperator,
        out long minValue,
        out long maxValue)
    {
        smtOperator = default;
        minValue = default;
        maxValue = default;

        if (semanticModel.GetOperation(updateExpression, cancellationToken) is not IIncrementOrDecrementOperation
            {
                IsChecked: true,
                OperatorMethod: null
            } operation)
            return false;

        var operandType = operation.Target.Type ?? semanticModel.GetTypeInfo(operand, cancellationToken).Type;
        if (!TryGetBoundedIntegralRange(operandType, out minValue, out maxValue)) return false;

        if (!CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(updateExpression, out _, out var delta))
            return false;

        smtOperator = delta > 0 ? SmtIntegerBinaryOperator.Add : SmtIntegerBinaryOperator.Subtract;
        return true;
    }

    internal static bool TryGetCheckedIntegralCompoundAssignmentOperator(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtIntegerBinaryOperator smtOperator,
        out long minValue,
        out long maxValue)
    {
        smtOperator = default;
        minValue = default;
        maxValue = default;

        if (semanticModel.GetOperation(assignment, cancellationToken) is not ICompoundAssignmentOperation
            {
                OperatorMethod: null
            } operation)
            return false;

        var targetType = operation.Target.Type ?? semanticModel.GetTypeInfo(assignment.Left, cancellationToken).Type;
        if (!TryGetBoundedIntegralRange(targetType, out minValue, out maxValue)) return false;

        return CSharpSyntaxFacts.TryGetCompoundAssignmentBinaryKind(assignment.Kind(), out var binaryKind) &&
               TryGetOverflowRelevantBinaryOperator(binaryKind, operation.IsChecked, minValue, out smtOperator);
    }

    private static bool TryGetOverflowRelevantBinaryOperator(
        SyntaxKind binaryKind,
        bool isChecked,
        long minimum,
        out SmtIntegerBinaryOperator smtOperator)
    {
        smtOperator = default;
        if (!SymbolicOperatorLowerer.TryGetBinaryTermOperator(binaryKind, out var binaryOperator) ||
            (binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Subtract or
                SymbolicBinaryTermOperator.Multiply) && !isChecked ||
            (binaryOperator is SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder) &&
            minimum >= 0)
            return false;

        smtOperator = binaryOperator switch
        {
            SymbolicBinaryTermOperator.Add => SmtIntegerBinaryOperator.Add,
            SymbolicBinaryTermOperator.Subtract => SmtIntegerBinaryOperator.Subtract,
            SymbolicBinaryTermOperator.Multiply => SmtIntegerBinaryOperator.Multiply,
            SymbolicBinaryTermOperator.Divide => SmtIntegerBinaryOperator.Divide,
            SymbolicBinaryTermOperator.Remainder => SmtIntegerBinaryOperator.Remainder,
            _ => throw new ArgumentOutOfRangeException(nameof(binaryKind))
        };
        return true;
    }

    internal static bool TryGetCheckedExplicitNumericConversionRange(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long minValue,
        out long maxValue)
    {
        minValue = default;
        maxValue = default;
        if (semanticModel.GetOperation(castExpression, cancellationToken) is not IConversionOperation
            {
                IsChecked: true,
                Conversion:
                {
                    Exists: true,
                    IsIdentity: false,
                    IsImplicit: false,
                    IsNumeric: true,
                    IsUserDefined: false,
                    MethodSymbol: null
                }
            } ||
            !TryGetCheckedNumericConversionRange(
                SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression, semanticModel, cancellationToken),
                out minValue,
                out maxValue))
            return false;

        if (TryGetCheckedNumericConversionRange(
                SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression.Expression, semanticModel,
                    cancellationToken),
                out var sourceMinValue,
                out var sourceMaxValue) &&
            sourceMinValue >= minValue &&
            sourceMaxValue <= maxValue)
            return false;

        return true;
    }

    internal static bool TryGetCheckedIntegralRange(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long minValue,
        out long maxValue)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return TryGetCheckedIntegralRange(typeInfo.ConvertedType ?? typeInfo.Type, out minValue, out maxValue);
    }

    internal static bool TryGetCheckedIntegralRange(
        ITypeSymbol? typeSymbol,
        out long minValue,
        out long maxValue)
    {
        return SymbolicTypeFacts.TryGetCheckedIntegralRange(typeSymbol, out minValue, out maxValue);
    }

    internal static bool TryGetBoundedIntegralRange(
        ITypeSymbol? typeSymbol,
        out long minValue,
        out long maxValue)
    {
        return SymbolicTypeFacts.TryGetBoundedIntegralRange(typeSymbol, out minValue, out maxValue);
    }

    internal static bool TryGetCheckedNumericConversionRange(
        ITypeSymbol? typeSymbol,
        out long minValue,
        out long maxValue)
    {
        return SymbolicTypeFacts.TryGetCheckedNumericConversionRange(typeSymbol, out minValue, out maxValue);
    }

    internal static bool TryGetArrayElementStoreType(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IArrayTypeSymbol arrayType)
    {
        arrayType = null!;
        var argumentCount = elementAccess.ArgumentList.Arguments.Count;
        if (argumentCount == 0 ||
            CSharpSyntaxFacts.GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken) is not IArrayTypeSymbol
                candidate ||
            candidate.Rank != argumentCount)
            return false;

        arrayType = candidate;
        return true;
    }

    internal static bool TryCreateArrayStoreMismatchTrigger(
        AssignmentExpressionSyntax assignment,
        ElementAccessExpressionSyntax elementAccess,
        IArrayTypeSymbol declaredArrayType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        SymbolicTerm? subject = null;
        var receiverLowering = SymbolicSemanticPipeline.LowerTerm(elementAccess.Expression, context);
        if (receiverLowering is { IsExact: true, Value: { } receiver } &&
            receiver.Kind == SmtValueKind.Reference)
            subject = receiver;

        if (declaredArrayType.Rank != 1 ||
            elementAccess.ArgumentList.Arguments.Count != 1 ||
            subject == null ||
            SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(elementAccess, context) is not
                { IsExact: true, Value: { } inRangeCondition })
        {
            trigger = CreateUnsupportedExceptionPreconditionTrigger(
                assignment,
                SymbolicExceptionPreconditionKind.ArrayTypeMismatch,
                subject,
                "ir.runtime-hazard.array-type-mismatch.unsupported");
            return true;
        }

        SymbolicCondition mismatchCondition;
        if (TryCreateReferenceNullCondition(
                assignment.Right,
                semanticModel,
                cancellationToken,
                "ir.runtime-hazard.array-type-mismatch.assigned-null",
                out var assignedNullCondition) &&
            assignedNullCondition is SymbolicConstantCondition { Value: true })
        {
            mismatchCondition = new SymbolicConstantCondition(false);
        }
        else if (SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                     elementAccess.Expression,
                     assignment,
                     semanticModel,
                     cancellationToken,
                     out var exactRuntimeArrayType) &&
                 exactRuntimeArrayType is IArrayTypeSymbol exactArrayType &&
                 exactArrayType.Rank == 1 &&
                 IsReferenceType(exactArrayType.ElementType) &&
                 SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                     assignment.Right,
                     assignment,
                     semanticModel,
                     cancellationToken,
                     out var exactAssignedType))
        {
            mismatchCondition = new SymbolicConstantCondition(
                !SymbolicRuntimeTypeFacts.CanStoreExactRuntimeTypeInArrayElement(
                    exactAssignedType,
                    exactArrayType.ElementType,
                    semanticModel.Compilation));
        }
        else
        {
            trigger = CreateUnsupportedExceptionPreconditionTrigger(
                assignment,
                SymbolicExceptionPreconditionKind.ArrayTypeMismatch,
                subject,
                "ir.runtime-hazard.array-type-mismatch.unsupported");
            return true;
        }

        var receiverNotNull = SymbolicIrLowerer.CreateReferenceNullCondition(
            subject,
            false,
            elementAccess.Expression,
            "ir.runtime-hazard.array-type-mismatch.receiver-not-null");
        var triggerCondition = new SymbolicBinaryCondition(
            SymbolicConditionOperator.And,
            receiverNotNull,
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                inRangeCondition,
                mismatchCondition));
        if (TryCreateIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.ArrayTypeMismatch,
                subject,
                triggerCondition,
                assignment,
                "ir.runtime-hazard.array-type-mismatch",
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            assignment,
            SymbolicExceptionPreconditionKind.ArrayTypeMismatch,
            subject,
            "ir.runtime-hazard.array-type-mismatch.unsupported");
        return true;
    }
}
