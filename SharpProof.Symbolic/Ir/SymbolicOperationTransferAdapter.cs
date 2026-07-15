using Microsoft.CodeAnalysis;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicOperationTransferAdapter
{
    internal static SymbolicOperationTransitionResult Apply(
        SymbolicState state,
        IOperation operation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getTargetVersion = null,
        Func<ISymbol, int>? getValueVersion = null,
        int sequence = 0)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        var targetContext = new SymbolicLoweringContext(
            semanticModel,
            cancellationToken,
            getTargetVersion);
        var valueContext = new SymbolicLoweringContext(
            semanticModel,
            cancellationToken,
            getValueVersion);
        var lowering = SymbolicOperationLowerer.Lower(
            operation,
            targetContext,
            valueContext,
            sequence);
        return ApplyLowering(state, lowering);
    }

    internal static SymbolicOperationTransitionResult ApplyAssignment(
        SymbolicState state,
        ISymbol targetSymbol,
        IOperation valueOperation,
        SyntaxNode source,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getTargetVersion = null,
        Func<ISymbol, int>? getValueVersion = null,
        int sequence = 0,
        string provenance = "operation-lowering.assignment")
    {
        var targetContext = new SymbolicLoweringContext(
            semanticModel,
            cancellationToken,
            getTargetVersion);
        var valueContext = new SymbolicLoweringContext(
            semanticModel,
            cancellationToken,
            getValueVersion);
        var lowering = SymbolicOperationLowerer.LowerSimpleAssignment(
            targetSymbol,
            valueOperation,
            source,
            targetContext,
            valueContext,
            sequence,
            provenance);
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

    private static SymbolicOperationTransitionResult ApplyLowering(
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
