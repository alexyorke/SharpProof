namespace SharpProof.Symbolic.Ir;

internal sealed record SymbolicOperationTransitionResult(
    SymbolicState State,
    SymbolicLoweringSupport Support,
    SymbolicUnknownReason UnknownReason,
    ImmutableArray<SymbolicLoweringProvenance> Provenance,
    SymbolicAnalysisTruncationInfo Truncation)
{
    internal bool IsExact => Support == SymbolicLoweringSupport.Exact;

    internal bool IsApproximate => Support == SymbolicLoweringSupport.Approximate;

    internal bool IsUnsupported => Support == SymbolicLoweringSupport.Unsupported;

    internal static SymbolicOperationTransitionResult Exact(
        SymbolicState state,
        IEnumerable<SymbolicLoweringProvenance> provenance,
        SymbolicAnalysisTruncationInfo? truncation = null)
    {
        return Create(
            state.Normalize(),
            SymbolicLoweringSupport.Exact,
            SymbolicUnknownReason.None,
            provenance,
            truncation);
    }


    internal static SymbolicOperationTransitionResult Unsupported(
        SymbolicState unchangedState,
        SymbolicUnknownReason unknownReason,
        IEnumerable<SymbolicLoweringProvenance> provenance,
        SymbolicAnalysisTruncationInfo? truncation = null)
    {
        if (unknownReason == SymbolicUnknownReason.None)
            throw new ArgumentException("Unsupported transitions require an unknown reason.", nameof(unknownReason));

        return Create(
            unchangedState,
            SymbolicLoweringSupport.Unsupported,
            unknownReason,
            provenance,
            truncation);
    }

    private static SymbolicOperationTransitionResult Create(
        SymbolicState state,
        SymbolicLoweringSupport support,
        SymbolicUnknownReason unknownReason,
        IEnumerable<SymbolicLoweringProvenance> provenance,
        SymbolicAnalysisTruncationInfo? truncation)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (provenance == null) throw new ArgumentNullException(nameof(provenance));

        var normalizedTruncation = truncation == null || !truncation.IsTruncated
            ? SymbolicAnalysisTruncationInfo.None
            : SymbolicAnalysisTruncationInfo.Combine(new[] { truncation });
        return new SymbolicOperationTransitionResult(
            state,
            support,
            unknownReason,
            provenance.ToImmutableArray(),
            normalizedTruncation);
    }
}
