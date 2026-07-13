using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic;

internal static class SymbolicExpressionStateTransfer
{
    internal static void AddCompletedExpressionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is AssignmentExpressionSyntax assignment)
        {
            AddAssignmentExpressionStateFacts(
                ref state,
                assignment,
                null,
                semanticModel,
                cancellationToken);
            return;
        }

        SymbolicStateInvalidator.InvalidateNestedMutations(
            ref state,
            expression,
            semanticModel,
            cancellationToken);
        SymbolicNormalCompletionStateTransfer.AddTopLevelMemberNotNullNormalCompletionStateFacts(
            ref state,
            expression,
            semanticModel,
            cancellationToken);
    }

    internal static void AddAssignmentExpressionStateFacts(
        ref SymbolicState state,
        AssignmentExpressionSyntax assignment,
        ExpressionStatementSyntax? containingStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (SymbolicAssignmentStateTransfer.TryHandleTupleDeconstructionDeclarationState(ref state, assignment, semanticModel, cancellationToken))
            return;

        if (SymbolicAssignmentStateTransfer.TryHandleTupleAssignmentState(ref state, assignment, semanticModel, cancellationToken)) return;

        var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
        if (assignedSymbol != null)
            assignedSymbol = SymbolicStateInvalidator.NormalizeMutatedSymbol(assignedSymbol);

        SymbolicTerm? previousAssignedValueTerm = null;
        if (assignedSymbol is ILocalSymbol or IParameterSymbol &&
            SymbolicStateValueFacts.TryGetCurrentValue(
                state,
                assignedSymbol.OriginalDefinition,
                out var previousStateValueTerm))
            previousAssignedValueTerm = previousStateValueTerm;

        var coalesceAssignmentIsKnownNoOp = assignedSymbol is ILocalSymbol or IParameterSymbol &&
                                            assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                                            (SymbolicStateValueFacts.IsKnownNonNullReference(
                                                 state,
                                                 assignedSymbol.OriginalDefinition) ||
                                             SymbolicStateValueFacts.IsKnownNullableHasValue(
                                                 state,
                                                 assignedSymbol.OriginalDefinition));
        var coalesceAssignmentIsKnownNullableNoValue = assignedSymbol is ILocalSymbol or IParameterSymbol &&
                                                       assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                                                       SymbolicStateValueFacts.IsKnownNullableNoValue(state,
                                                           assignedSymbol.OriginalDefinition);
        var coalesceAssignmentIsKnownNullReference = assignedSymbol is ILocalSymbol or IParameterSymbol &&
                                                     assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                                                     SymbolicStateValueFacts.IsKnownNullReference(state,
                                                         assignedSymbol.OriginalDefinition);

        if (coalesceAssignmentIsKnownNoOp) return;

        SymbolicStateInvalidator.InvalidateMutationTarget(
            ref state,
            assignment.Left,
            semanticModel,
            cancellationToken);
        SymbolicStateInvalidator.InvalidateNestedMutations(
            ref state,
            assignment.Left,
            semanticModel,
            cancellationToken);
        SymbolicStateInvalidator.InvalidateNestedMutations(
            ref state,
            assignment.Right,
            semanticModel,
            cancellationToken);

        if (assignedSymbol is IFieldSymbol or IPropertySymbol &&
            SymbolicStateInvalidator.IsCurrentInstanceMemberReference(
                assignment.Left,
                semanticModel,
                cancellationToken))
            state = SymbolicStateValueFacts.RemoveImplicitThisMemberReferences(state, assignedSymbol.Name);

        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                SymbolicAssignmentStateTransfer.AddAssignedValueStateFacts(
                    ref state,
                    assignedSymbol.OriginalDefinition,
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    "ir.path.prior-statement",
                    previousAssignedValueTerm);
            else if (assignedSymbol is IFieldSymbol or IPropertySymbol &&
                     SymbolicStateInvalidator.IsCurrentInstanceMemberReference(
                         assignment.Left,
                         semanticModel,
                         cancellationToken) &&
                     SymbolicNormalCompletionStateTransfer.TryCreateImplicitThisMemberTerm(assignedSymbol, out var memberTerm))
                SymbolicAssignmentStateTransfer.AddAssignedCurrentInstanceMemberStateFacts(
                    ref state,
                    memberTerm,
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    "ir.path.prior-statement");
        }
        else if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                 assignedSymbol is ILocalSymbol or IParameterSymbol &&
                 (coalesceAssignmentIsKnownNullableNoValue || coalesceAssignmentIsKnownNullReference))
        {
            SymbolicAssignmentStateTransfer.AddAssignedValueStateFacts(
                ref state,
                assignedSymbol.OriginalDefinition,
                assignment.Right,
                semanticModel,
                cancellationToken,
                "ir.path.prior-statement.coalesce-assignment",
                previousAssignedValueTerm);
        }
        else if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                 assignedSymbol is ILocalSymbol or IParameterSymbol)
        {
            AddCoalesceAssignmentStateFacts(
                ref state,
                assignedSymbol.OriginalDefinition,
                assignment.Right,
                semanticModel,
                cancellationToken);
        }
        else if (assignedSymbol is ILocalSymbol or IParameterSymbol &&
                 previousAssignedValueTerm != null &&
                 SymbolicAssignmentValueUpdater.TryCreateCompoundAssignment(
                     previousAssignedValueTerm,
                     assignment,
                     semanticModel,
                     cancellationToken,
                     assignedSymbol.OriginalDefinition,
                     out var compoundAssignmentValueTerm) &&
                 TryCreateSymbolTerm(assignedSymbol.OriginalDefinition, out var targetTerm) &&
                 targetTerm.Kind == SmtValueKind.Int &&
                 !SymbolicIrReferenceScanner.ContainsVariableOrMember(
                     compoundAssignmentValueTerm,
                     SymbolicFactFactory.GetSmtVariableName(assignedSymbol.OriginalDefinition)))
        {
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetTerm,
                compoundAssignmentValueTerm,
                assignment,
                "ir.path.prior-statement.compound-assignment");
        }

        SymbolicAssignmentStateTransfer.AddElementAssignmentStateFact(
            ref state,
            assignment,
            semanticModel,
            cancellationToken);

        if (containingStatement != null)
            SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
                ref state,
                assignment.Right,
                containingStatement,
                assignedSymbol is not ILocalSymbol and not IParameterSymbol,
                semanticModel,
                cancellationToken);
    }

    private static void AddCoalesceAssignmentStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax rightExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(rightExpression) is ThrowExpressionSyntax)
        {
            if (SymbolicAssignmentStateTransfer.TryCreateNullableSymbolTerms(assignedSymbol, out var completedHasValue, out _))
            {
                state = state.AddPathCondition(new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicTruthAtom(completedHasValue),
                    rightExpression,
                    "ir.path.coalesce-assignment.throw-completion-has-value",
                    assignedSymbol)));
                return;
            }

            if (TryCreateSymbolTerm(assignedSymbol, out var completedReference) &&
                completedReference.Kind == SmtValueKind.Reference)
                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.NotEqual,
                    completedReference,
                    new SymbolicNullTerm(),
                    rightExpression,
                    "ir.path.coalesce-assignment.throw-completion-non-null");

            return;
        }

        if (SymbolicAssignmentStateTransfer.TryCreateNullableSymbolTerms(assignedSymbol, out var targetHasValue, out var targetValue))
        {
            SymbolicTerm? rightHasValue = null;
            var hasValueLowering = SymbolicSemanticPipeline.LowerNullableHasValueTerm(rightExpression, context);
            if (hasValueLowering is { IsExact: true, Value: { } nullableRightHasValue })
                rightHasValue = nullableRightHasValue;
            else if (SymbolicSemanticPipeline.LowerTerm(rightExpression, context) is
                     { IsExact: true, Value: { } wrappedRightValue } &&
                     wrappedRightValue.Kind == targetValue.Kind)
                rightHasValue = new SymbolicBooleanConstantTerm(true);

            if (rightHasValue == null) return;

            if (rightHasValue is SymbolicBooleanConstantTerm { Value: true })
            {
                var fact = SymbolicFact.Exact(
                    new SymbolicTruthAtom(targetHasValue),
                    rightExpression,
                    "ir.path.coalesce-assignment.nullable-has-value",
                    assignedSymbol);
                state = state.AddPathCondition(new SymbolicFactCondition(fact));
            }
            else
            {
                var targetHasValueFact = SymbolicFact.Exact(
                    new SymbolicTruthAtom(targetHasValue),
                    rightExpression,
                    "ir.path.coalesce-assignment.target-has-value",
                    assignedSymbol);
                var rightHasNoValueFact = SymbolicFact.Exact(
                    new SymbolicTruthAtom(rightHasValue),
                    rightExpression,
                    "ir.path.coalesce-assignment.right-has-value");
                state = state.AddPathCondition(new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    new SymbolicFactCondition(targetHasValueFact),
                    new SymbolicNotCondition(new SymbolicFactCondition(rightHasNoValueFact))));
            }

            return;
        }

        if (!TryCreateSymbolTerm(assignedSymbol, out var target) ||
            target.Kind != SmtValueKind.Reference)
            return;

        if (SymbolicAssignmentStateTransfer.IsDefinitelyNonNullReferenceValue(rightExpression, semanticModel, cancellationToken))
        {
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm(),
                rightExpression,
                "ir.path.coalesce-assignment.non-null");
            return;
        }

        var rightLowering = SymbolicSemanticPipeline.LowerReferenceTerm(rightExpression, context);
        if (rightLowering is not { IsExact: true, Value: { } right }) return;

        var targetNonNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm()),
            rightExpression,
            "ir.path.coalesce-assignment.target-non-null",
            assignedSymbol));
        var targetEqualsRight = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(SymbolicRelationOperator.Equal, target, right),
            rightExpression,
            "ir.path.coalesce-assignment.target-equals-right",
            assignedSymbol));
        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            targetNonNull,
            targetEqualsRight));
    }

}
