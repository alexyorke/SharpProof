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
