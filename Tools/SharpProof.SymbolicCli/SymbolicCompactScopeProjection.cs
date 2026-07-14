namespace SharpProof.Symbolic;

internal sealed record SymbolicCompactScopeProjection(
    SymbolicCompactInvariantSummary ObservedInvariant,
    SymbolicCompactInvariantSummary ConservativeInvariant,
    SymbolicCompactInvariantQueryView InvariantQuery,
    SymbolicReachabilitySummary Reachability,
    SymbolicProgramPointSummary ProgramPointSummary,
    IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs,
    IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints,
    SymbolicCompactSmtDiagnostics SmtDiagnostics,
    SymbolicCompactOutputTruncation Truncation)
{
    internal static SymbolicCompactScopeProjection Create(
        SymbolicInvariantResult observedInvariant,
        IReadOnlyList<string> observedFacts,
        SymbolicInvariantResult conservativeInvariant,
        SymbolicMergedPathFacts? mergedPathFacts,
        SymbolicInvariantQueryView invariantQuery,
        SymbolicReachabilitySummary reachability,
        SymbolicProgramPointSummary programPointSummary,
        IReadOnlyList<SymbolicConditionProofSummary> conditionProofSummaries,
        IReadOnlyList<SymbolicProgramPointResult> sourceProgramPoints,
        SymbolicSmtDiagnostics smtDiagnostics,
        SymbolicCompactQueryOptions options,
        int maxProgramPoints)
    {
        var compactObservedInvariant = SymbolicCompactInvariantSummary.FromObservedFacts(
            observedInvariant,
            observedFacts,
            options);
        var compactConservativeInvariant = SymbolicCompactInvariantSummary.FromInvariant(
            conservativeInvariant,
            mergedPathFacts,
            options);
        var programPoints = SymbolicCompactProjection
            .Take(sourceProgramPoints, maxProgramPoints)
            .Select(point => SymbolicCompactProgramPointResult.FromResult(point, options))
            .ToArray();
        var filteredProofs = SymbolicInvariantTargetFilter.ApplyToProofSummaries(
            conditionProofSummaries,
            options.InvariantTargets);
        var conditionProofs = SymbolicCompactProjection.Take(filteredProofs, options.MaxProofs);
        var truncation = SymbolicCompactOutputTruncation.Combine(
            new SymbolicCompactOutputTruncation(
                false,
                sourceProgramPoints.Count > programPoints.Length,
                false,
                false,
                filteredProofs.Count > options.MaxProofs),
            SymbolicCompactOutputTruncation.FromInvariant(compactObservedInvariant),
            SymbolicCompactOutputTruncation.FromInvariant(compactConservativeInvariant),
            SymbolicCompactOutputTruncation.Combine(programPoints.Select(static point => point.Truncation)));

        return new SymbolicCompactScopeProjection(
            compactObservedInvariant,
            compactConservativeInvariant,
            SymbolicCompactInvariantQueryView.FromQueryView(invariantQuery, options),
            reachability,
            programPointSummary,
            conditionProofs,
            programPoints,
            SymbolicCompactSmtDiagnostics.FromDiagnostics(smtDiagnostics),
            truncation);
    }
}
