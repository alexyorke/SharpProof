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
        if (lowering is { IsExact: true, Value: { } operations })
            return SymbolicOperationTransferKernel.Apply(state, operations);

        return SymbolicOperationTransitionResult.Unsupported(
            state,
            lowering.UnknownReason == SymbolicUnknownReason.None
                ? SymbolicUnknownReason.UnsupportedIrEncoding
                : lowering.UnknownReason,
            lowering.Provenance);
    }
}
