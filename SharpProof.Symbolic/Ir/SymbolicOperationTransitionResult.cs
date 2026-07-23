namespace SharpProof.Symbolic.Ir;

internal sealed record SymbolicOperationTransitionResult(
    SymbolicState State,
    bool IsExact,
    ImmutableArray<SymbolicLoweringProvenance> Provenance) {
    internal static SymbolicOperationTransitionResult Exact(
        SymbolicState state,
        IEnumerable<SymbolicLoweringProvenance> provenance) =>
        new(state.Normalize(), true, [.. provenance]);

    internal static SymbolicOperationTransitionResult Unsupported(
        SymbolicState unchangedState,
        SymbolicUnknownReason unknownReason,
        IEnumerable<SymbolicLoweringProvenance> provenance) {
        if (unknownReason == SymbolicUnknownReason.None)
            throw new ArgumentException("Unsupported transitions require an unknown reason.", nameof(unknownReason));
        return new(unchangedState, false, [.. provenance]);
    }
}
