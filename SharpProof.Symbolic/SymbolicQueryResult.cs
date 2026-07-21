namespace SharpProof.Symbolic;

internal sealed class SymbolicQueryResult(
    IReadOnlyList<SymbolicProgramPointResult> programPoints,
    string mergedInvariantText) {
    public IReadOnlyList<SymbolicProgramPointResult> ProgramPoints { get; } =
        programPoints ?? throw new ArgumentNullException(nameof(programPoints));

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; } =
        SymbolicAnalysisTruncationInfo.Combine(programPoints.Select(static point => point.AnalysisTruncation));

    internal string MergedInvariantText { get; } =
        mergedInvariantText ?? throw new ArgumentNullException(nameof(mergedInvariantText));

    public IReadOnlyList<SymbolicInputWitness> ReachabilityWitnesses { get; } =
        programPoints.Select(static point => point.ReachabilityWitness).ToArray();

    public SymbolicInputDomainSummary InputDomainSummary { get; } = SymbolicInputWitnessFactory.MergeAlternatives(
        programPoints.Select(static point => point.ReachabilityWitness).ToArray());

    internal static SymbolicQueryResult From(SymbolicProgramPointResult point) {
        if (point == null) throw new ArgumentNullException(nameof(point));

        return From(new[] { point });
    }

    internal static SymbolicQueryResult From(IReadOnlyList<SymbolicProgramPointResult> points) {
        if (points == null) throw new ArgumentNullException(nameof(points));

        return new SymbolicQueryResult(points, SymbolicMergedPathFactMerger.MergeInvariantText(points));
    }
}
