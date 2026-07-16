using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic;

internal static class SymbolicExpressionStateTransfer
{
    internal static bool TryApplyCurrentExpressionCompletion(
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
            return true;
        }

        var frameworkLowering = SymbolicFrameworkPostconditionLowerer.LowerMemberNotNull(
            expression,
            semanticModel,
            cancellationToken);
        if (frameworkLowering is not { IsExact: true, Value: { } frameworkPlan })
            return false;

        SymbolicStateInvalidator.InvalidateNestedMutations(
            ref state,
            expression,
            semanticModel,
            cancellationToken);
        SymbolicNormalCompletionStateTransfer.ApplyConditions(
            ref state,
            frameworkPlan.AfterDoesNotReturnIf,
            expression,
            "ir.path.expression-completion.member-not-null");
        return true;
    }

    internal static void AddAssignmentExpressionStateFacts(
        ref SymbolicState state,
        AssignmentExpressionSyntax assignment,
        ExpressionStatementSyntax? containingStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
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
        SymbolicStateInvalidator.InvalidateNestedAssignmentMutations(
            ref state,
            assignment,
            semanticModel,
            cancellationToken);

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
            else
            {
                var transition = SymbolicOperationTransferAdapter.ApplyLowering(
                    state,
                    SymbolicOperationLowerer.LowerExplicitTargetAssignment(
                        assignment,
                        new SymbolicLoweringContext(semanticModel, cancellationToken)));
                if (transition.IsExact) state = transition.State;
            }
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
            var transition = SymbolicOperationTransferAdapter.ApplyCoalesceAssignment(
                state,
                assignedSymbol.OriginalDefinition,
                assignment.Right,
                semanticModel,
                cancellationToken,
                "ir.path.coalesce-assignment");
            if (transition.IsExact) state = transition.State;
        }
        else if (assignedSymbol is ILocalSymbol or IParameterSymbol)
            SymbolicAssignmentValueUpdater.TryApplyComputedUpdate(
                ref state,
                assignedSymbol.OriginalDefinition,
                assignment,
                semanticModel,
                cancellationToken,
                previousAssignedValueTerm);

        if (containingStatement != null)
            SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
                ref state,
                assignment.Right,
                containingStatement,
                assignedSymbol is not ILocalSymbol and not IParameterSymbol,
                semanticModel,
                cancellationToken);
    }

}
