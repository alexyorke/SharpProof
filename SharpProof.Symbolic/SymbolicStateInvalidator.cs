namespace SharpProof.Symbolic;

internal readonly record struct SymbolicMutationInvalidationStep(
    ImmutableArray<SymbolicInvalidationTarget> Targets,
    Microsoft.CodeAnalysis.Text.TextSpan SourceSpan,
    string Provenance);

internal sealed record SymbolicNestedMutationInvalidationPlan(
    ImmutableArray<SymbolicMutationInvalidationStep> Steps,
    bool HasUnsupportedMutation);

internal static class SymbolicStateInvalidator {
    internal static void InvalidateNestedAssignmentMutations(
        ref SymbolicState state,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        InvalidateNestedMutations(ref state, assignment.Left, semanticModel, cancellationToken);
        InvalidateNestedMutations(ref state, assignment.Right, semanticModel, cancellationToken);
    }

    internal static void InvalidateNestedMutations(
        ref SymbolicState state,
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        state = ApplyNestedMutationInvalidations(
            state,
            LowerNestedMutations(root, semanticModel, cancellationToken));
    }

    internal static SymbolicNestedMutationInvalidationPlan LowerNestedMutations(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        SymbolicMutationInventory.Create(root, semanticModel, cancellationToken).ToInvalidationPlan();

    internal static SymbolicState ApplyNestedMutationInvalidations(
        SymbolicState state,
        SymbolicNestedMutationInvalidationPlan plan) {
        foreach (var step in plan.Steps)
            state = SymbolicOperationTransferKernel.Invalidate(
                state,
                step.Targets,
                step.SourceSpan,
                step.Provenance).State;
        return state;
    }

    internal static void InvalidateMutationTarget(
        ref SymbolicState state,
        ExpressionSyntax mutatedExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var invalidations = SymbolicMutationInventory.LowerTargetInvalidations(
            mutatedExpression,
            semanticModel,
            cancellationToken);
        if (!invalidations.IsDefaultOrEmpty)
            state = SymbolicOperationTransferKernel.Invalidate(
                state,
                invalidations,
                mutatedExpression.Span,
                "operation-transfer.mutation-invalidation").State;
    }

    internal static void InvalidateSymbol(ref SymbolicState state, ISymbol symbol, SyntaxNode source) {
        state = SymbolicOperationTransferKernel.Invalidate(
            state,
            ImmutableArray.Create(new SymbolicInvalidationTarget(
                SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition))),
            source.Span,
            "operation-transfer.reference-invalidation").State;
    }

    internal static bool IsCurrentInstanceMemberReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is IdentifierNameSyntax &&
            SymbolicMutationInventory.GetMutatedSymbol(expression, semanticModel, cancellationToken) is
                { IsStatic: false } and (IFieldSymbol or IPropertySymbol))
            return true;
        return expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
    }

}
