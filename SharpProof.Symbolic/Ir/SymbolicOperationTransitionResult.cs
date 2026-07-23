namespace SharpProof.Symbolic.Ir;

internal sealed record SymbolicOperationTransitionResult(
    SymbolicState State,
    bool IsExact) {
    internal static SymbolicOperationTransitionResult Exact(SymbolicState state) =>
        new(state.Normalize(), true);
    internal static SymbolicOperationTransitionResult Unsupported(SymbolicState unchangedState) =>
        new(unchangedState, false);
}
