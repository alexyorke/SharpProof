namespace SharpProof.Symbolic;

internal static class SymbolicComplexityResultProjector {
    internal static SymbolicComplexityResult Project(
        ResolvedMethodLikeTarget target,
        MethodAnalysisSummary summary,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var complexity = new SymbolicComplexityInfo(
            summary.Cost.ToBigOText(target.MethodSymbol!),
            summary.Cost.ToPublicKind(),
            summary.Cost.IsConservative,
            summary.Cost.IsUnknown,
            summary.Cost.IsRecursiveUnknown);

        return new SymbolicComplexityResult(
            complexity,
            DistinctDrivers(summary.Drivers),
            DistinctUnknownReasons(summary.UnknownReasons),
            DistinctCalleeSummaries(summary.CalleeSummaries));
    }

    private static IReadOnlyList<SymbolicComplexityDriverInfo> DistinctDrivers(
        IEnumerable<SymbolicComplexityDriverInfo> drivers) => drivers.Distinct().ToArray();

    private static IReadOnlyList<SymbolicComplexityUnknownReason> DistinctUnknownReasons(
        IEnumerable<SymbolicComplexityUnknownReason> reasons) {
        return reasons
            .Where(static reason => reason != SymbolicComplexityUnknownReason.None)
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<SymbolicComplexityCalleeInfo> DistinctCalleeSummaries(
        IEnumerable<SymbolicComplexityCalleeInfo> callees) => callees.Distinct().ToArray();
}
