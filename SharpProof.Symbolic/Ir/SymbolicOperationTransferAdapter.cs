using Microsoft.CodeAnalysis;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicOperationTransferAdapter
{
    internal static SymbolicOperationTransitionResult ApplyAssignment(
        SymbolicState state,
        ISymbol targetSymbol,
        SyntaxNode valueSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getTargetVersion = null,
        Func<ISymbol, int>? getValueVersion = null,
        int sequence = 0,
        string provenance = "operation-lowering.assignment",
        string? bindingProvenance = null,
        string? evidenceKey = null,
        string? asExpressionProvenanceRoot = null,
        SymbolicAssignmentPostconditionProfile postconditionProfile = SymbolicAssignmentPostconditionProfile.Analyzer,
        SymbolicTerm? preInvalidationTargetValue = null)
    {
        var targetContext = new SymbolicLoweringContext(
            semanticModel,
            cancellationToken,
            getTargetVersion);
        var valueContext = new SymbolicLoweringContext(
            semanticModel,
            cancellationToken,
            getValueVersion,
            symbolSubstitutions: preInvalidationTargetValue == null
                ? null
                : new Dictionary<ISymbol, SymbolicTerm>(1, SymbolEqualityComparer.Default)
                    { [targetSymbol.OriginalDefinition] = preInvalidationTargetValue });
        var lowering = SymbolicOperationLowerer.LowerSimpleAssignment(
            targetSymbol,
            valueSyntax,
            targetContext,
            valueContext,
            sequence,
            provenance,
            bindingProvenance,
            evidenceKey,
            asExpressionProvenanceRoot,
            postconditionProfile);
        return ApplyLowering(state, lowering);
    }

    internal static SymbolicOperationTransitionResult ApplyComputedUpdate(
        SymbolicState state,
        ISymbol targetSymbol,
        SymbolicTerm sourceTerm,
        SyntaxNode source,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicComputedUpdateKind updateKind,
        bool isChecked,
        string provenance)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicOperationLowerer.LowerComputedUpdate(
            targetSymbol,
            sourceTerm,
            source,
            context,
            updateKind,
            isChecked,
            sequence: 0,
            provenance);
        return ApplyLowering(state, lowering);
    }

    internal static SymbolicOperationTransitionResult ApplyCoalesceAssignment(
        SymbolicState state,
        ISymbol targetSymbol,
        Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax rightExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        return ApplyLowering(
            state,
            SymbolicOperationLowerer.LowerCoalesceAssignment(
                targetSymbol,
                rightExpression,
                context,
                sequence: 0,
                provenance));
    }

    internal static SymbolicOperationTransitionResult ApplyBindings(
        SymbolicState state,
        System.Collections.Immutable.ImmutableArray<SymbolicAssignmentBinding> bindings,
        SyntaxNode source,
        SymbolicAssignmentOperationKind assignmentKind,
        string provenance)
    {
        var operation = new SymbolicAssignmentOperation(
            bindings,
            System.Collections.Immutable.ImmutableArray<SymbolicCondition>.Empty,
            assignmentKind,
            IsChecked: false,
            new SymbolicOperationOrigin(source.Span, 0, provenance));
        return SymbolicOperationTransferKernel.Apply(
            state,
            SymbolicOperationSequence.Single(operation));
    }

    internal static SymbolicOperationTransitionResult ApplyLowering(
        SymbolicState state,
        SymbolicLoweringResult<SymbolicOperationSequence> lowering)
    {
        return lowering is { IsExact: true, Value: { } operations }
            ? SymbolicOperationTransferKernel.Apply(state, operations)
            : SymbolicOperationTransitionResult.Unsupported(
                state,
                lowering.UnknownReason == SymbolicUnknownReason.None
                    ? SymbolicUnknownReason.UnsupportedIrEncoding
                    : lowering.UnknownReason,
                lowering.Provenance);
    }
}
