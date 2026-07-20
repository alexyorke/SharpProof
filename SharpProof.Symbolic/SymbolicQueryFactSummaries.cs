using System.Text.Json.Serialization;

namespace SharpProof.Symbolic;

internal static class SymbolicCompactProjection {
    public static SymbolicBoundedProjection<T> Project<T>(IReadOnlyList<T> values, int maxCount) {
        if (values == null) throw new ArgumentNullException(nameof(values));

        ValidateMaxCount(maxCount);
        var items = maxCount == 0
            ? Array.Empty<T>()
            : values.Take(maxCount).ToArray();
        return new SymbolicBoundedProjection<T>(items, values.Count);
    }

    public static IReadOnlyList<T> Take<T>(IEnumerable<T> values, int maxCount) {
        if (values == null) throw new ArgumentNullException(nameof(values));

        if (maxCount == 0) return Array.Empty<T>();

        ValidateMaxCount(maxCount);

        return values.Take(maxCount).ToArray();
    }

    private static void ValidateMaxCount(int maxCount) {
        if (maxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Compact output limits cannot be negative.");
    }
}

internal readonly struct SymbolicBoundedProjection<T>(IReadOnlyList<T> items, int totalCount) {
    public IReadOnlyList<T> Items { get; } = items ?? throw new ArgumentNullException(nameof(items));
    public int TotalCount { get; } = totalCount >= items.Count
        ? totalCount
        : throw new ArgumentOutOfRangeException(nameof(totalCount));

    public int OmittedCount => TotalCount - Items.Count;

    public bool IsTruncated => OmittedCount != 0;
}

internal sealed record SymbolicConservativeUnknownDiagnostic(
    [property: JsonPropertyOrder(0)] string Target,
    [property: JsonPropertyOrder(1)] string UnknownText,
    [property: JsonPropertyOrder(2)] string Reason,
    [property: JsonPropertyOrder(3)] IReadOnlyList<string> MaybeFacts,
    [property: JsonPropertyOrder(5)] int CandidateProgramPointCount,
    [property: JsonPropertyOrder(6)] int UnreachableProgramPointCount) {
    [JsonPropertyOrder(4)]
    public int MaybeFactCount => MaybeFacts.Count;

    public string GetDisplayReason() =>
        SymbolicReasonDisplay.Format(Reason);
}

internal sealed record SymbolicMergedPathFacts(
    [property: JsonPropertyOrder(0)] IReadOnlyList<string> AlwaysFacts,
    [property: JsonPropertyOrder(1)] IReadOnlyList<string> MaybeFacts,
    [property: JsonPropertyOrder(2)] IReadOnlyList<string> ConservativeUnknowns,
    [property: JsonPropertyOrder(3)] IReadOnlyList<SymbolicConservativeUnknownDiagnostic> ConservativeUnknownDiagnostics,
    [property: JsonPropertyOrder(5)] IReadOnlyList<string> MergedFacts,
    [property: JsonPropertyOrder(6)] string MergedInvariantText,
    [property: JsonPropertyOrder(7)] int CandidateProgramPointCount,
    [property: JsonPropertyOrder(8)] int UnreachableProgramPointCount,
    [property: JsonPropertyOrder(9)] bool IsUnreachable) {
    [JsonPropertyOrder(4)]
    public int ConservativeUnknownCount => ConservativeUnknowns.Count;
    public static SymbolicMergedPathFacts FromProgramPoints(
        IEnumerable<SymbolicProgramPointResult> programPoints) {
        if (programPoints == null) throw new ArgumentNullException(nameof(programPoints));

        var points = programPoints.ToArray();
        if (points.Length == 0)
            return new SymbolicMergedPathFacts(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<SymbolicConservativeUnknownDiagnostic>(),
                Array.Empty<string>(),
                "true",
                0,
                0,
                false);

        var candidatePoints = points
            .Where(static point => point.Reachability != SymbolicReachability.Unreachable)
            .ToArray();
        var unreachableProgramPointCount = points.Length - candidatePoints.Length;
        if (candidatePoints.Length == 0)
            return new SymbolicMergedPathFacts(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<SymbolicConservativeUnknownDiagnostic>(),
                new[] { "false" },
                "false",
                0,
                unreachableProgramPointCount,
                true);

        var seenConditionTexts = new HashSet<string>(StringComparer.Ordinal);
        var orderedConditions = new List<SymbolicInvariantCondition>();
        var conditionSets = new List<HashSet<string>>();
        foreach (var point in candidatePoints) {
            var conditionSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var condition in point.Invariant.Conditions) {
                if (string.IsNullOrWhiteSpace(condition.Text)) continue;

                if (conditionSet.Add(condition.Text) &&
                    seenConditionTexts.Add(condition.Text))
                    orderedConditions.Add(condition);
            }

            conditionSets.Add(conditionSet);
        }

        var commonTexts = new HashSet<string>(conditionSets[0], StringComparer.Ordinal);
        for (var index = 1; index < conditionSets.Count; index++) commonTexts.IntersectWith(conditionSets[index]);

        var alwaysFacts = orderedConditions
            .Where(condition => commonTexts.Contains(condition.Text))
            .Select(static condition => condition.Text)
            .ToArray();
        var maybeConditions = orderedConditions
            .Where(condition => !commonTexts.Contains(condition.Text))
            .ToArray();
        var maybeFacts = maybeConditions
            .Select(static condition => condition.Text)
            .ToArray();
        var conservativeUnknownDiagnostics = CreateConservativeUnknownDiagnostics(
            maybeConditions,
            candidatePoints.Length,
            unreachableProgramPointCount);
        var conservativeUnknowns = conservativeUnknownDiagnostics
            .Select(static diagnostic => diagnostic.UnknownText)
            .ToArray();
        var mergedFacts = alwaysFacts
            .Concat(conservativeUnknowns)
            .ToArray();

        return new SymbolicMergedPathFacts(
            alwaysFacts,
            maybeFacts,
            conservativeUnknowns,
            conservativeUnknownDiagnostics,
            mergedFacts,
            SymbolicInvariantFactSummary.FormatMergedInvariantFacts(mergedFacts),
            candidatePoints.Length,
            unreachableProgramPointCount,
            false);
    }

    private static IReadOnlyList<SymbolicConservativeUnknownDiagnostic> CreateConservativeUnknownDiagnostics(
        IReadOnlyList<SymbolicInvariantCondition> maybeConditions,
        int candidateProgramPointCount,
        int unreachableProgramPointCount) {
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<SymbolicConservativeUnknownDiagnostic>();
        foreach (var condition in maybeConditions) {
            var target = string.IsNullOrWhiteSpace(condition.Target)
                ? "path"
                : condition.Target;
            if (seenTargets.Add(target))
                diagnostics.Add(new SymbolicConservativeUnknownDiagnostic(
                    target,
                    FormatConservativeUnknown(target),
                    "not_common_to_all_candidate_program_points",
                    maybeConditions
                        .Where(candidate => string.Equals(
                            string.IsNullOrWhiteSpace(candidate.Target) ? "path" : candidate.Target,
                            target,
                            StringComparison.Ordinal))
                        .Select(static candidate => candidate.Text)
                        .ToArray(),
                    candidateProgramPointCount,
                    unreachableProgramPointCount));
        }

        return diagnostics;
    }

    internal static string FormatConservativeUnknown(string target) =>
        "unknown(" + (string.IsNullOrWhiteSpace(target) ? "path" : target) + ")";
}

internal readonly record struct SymbolicQueryMetrics(
    int ProgramPointCount,
    int TotalPathConditionCount,
    int MaxPathConditionCount,
    int ReachabilityNotCheckedCount,
    int ReachabilityUnknownCount,
    int ReachableCount,
    int UnreachableCount,
    int ProofTotalCount,
    int ProofUnknownCount,
    int ProofProvenTrueCount,
    int ProofProvenFalseCount,
    int ProofUnreachableCount) {
    internal static SymbolicQueryMetrics FromProgramPoints(IEnumerable<SymbolicProgramPointResult> programPoints) {
        if (programPoints == null) throw new ArgumentNullException(nameof(programPoints));

        var points = programPoints.ToArray();
        var proofs = points.SelectMany(static point => point.ConditionProofs).ToArray();

        return new SymbolicQueryMetrics(
            points.Length,
            points.Sum(static point => point.PathConditionCount),
            points.Length == 0 ? 0 : points.Max(static point => point.PathConditionCount),
            points.Count(static point => point.Reachability == SymbolicReachability.NotChecked),
            points.Count(static point => point.Reachability == SymbolicReachability.Unknown),
            points.Count(static point => point.Reachability == SymbolicReachability.Reachable),
            points.Count(static point => point.Reachability == SymbolicReachability.Unreachable),
            proofs.Length,
            proofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.Unknown),
            proofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.ProvenTrue),
            proofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.ProvenFalse),
            proofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.Unreachable));
    }
}

internal sealed record SymbolicReachabilitySummary(
    int NotCheckedCount,
    int UnknownCount,
    int ReachableCount,
    int UnreachableCount);

internal sealed record SymbolicProofOutcomeSummary(
    int TotalCount,
    int UnknownCount,
    int ProvenTrueCount,
    int ProvenFalseCount,
    int UnreachableCount);

internal sealed record SymbolicProgramPointSummary(
    int ProgramPointCount,
    int TotalPathConditionCount,
    int MaxPathConditionCount,
    SymbolicReachabilitySummary Reachability,
    SymbolicProofOutcomeSummary ProofOutcomes);

internal sealed record SymbolicConditionProofReason(SymbolicTruthValue TruthValue, string Reason, int Count);

internal sealed record SymbolicConditionProofSummary(
    string Condition,
    string Target,
    string DisplayKind,
    string ValueKind,
    int TotalCount,
    int UnknownCount,
    int ProvenTrueCount,
    int ProvenFalseCount,
    int UnreachableCount,
    int ReachableCount,
    int ResolvedCount,
    SymbolicConditionProofSummaryStatus Status,
    string Summary,
    SymbolicProofInfo Proof,
    bool HoldsOnAllReachablePoints,
    bool RefutedOnAllReachablePoints,
    bool HasMixedReachableOutcomes,
    IReadOnlyList<SymbolicConditionProofReason> Reasons);

internal static class SymbolicConditionProofProjection {
    internal static IReadOnlyList<SymbolicConditionProofSummary> FromProgramPoints(
        IEnumerable<SymbolicProgramPointResult> programPoints) {
        if (programPoints == null) throw new ArgumentNullException(nameof(programPoints));

        return programPoints
            .SelectMany(static point => point.ConditionProofs)
            .GroupBy(static proof => proof.Condition, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => Create(group.Key, group))
            .ToArray();
    }

    private static SymbolicConditionProofSummary Create(
        string condition,
        IEnumerable<SymbolicConditionProofResult> proofs) {
        var proofArray = proofs.ToArray();
        var unknownCount = proofArray.Count(static proof => proof.TruthValue == SymbolicTruthValue.Unknown);
        var provenTrueCount = proofArray.Count(static proof => proof.TruthValue == SymbolicTruthValue.ProvenTrue);
        var provenFalseCount = proofArray.Count(static proof => proof.TruthValue == SymbolicTruthValue.ProvenFalse);
        var unreachableCount = proofArray.Count(static proof => proof.TruthValue == SymbolicTruthValue.Unreachable);

        var metadataProof = proofArray.FirstOrDefault(static proof => proof.IsSolverBacked) ??
                            proofArray.FirstOrDefault();
        var reachableCount = proofArray.Length - unreachableCount;
        var status = ResolveStatus(
            proofArray.Length,
            reachableCount,
            unknownCount,
            provenTrueCount,
            provenFalseCount,
            unreachableCount);
        var reasons = proofArray.GroupBy(static proof => (proof.TruthValue, proof.Reason))
            .OrderBy(static group => group.Key.TruthValue)
            .ThenBy(static group => group.Key.Reason, StringComparer.Ordinal)
            .Select(static group => new SymbolicConditionProofReason(
                group.Key.TruthValue,
                group.Key.Reason,
                group.Count()))
            .ToArray();
        var summary = CreateSummary(status);
        var target = metadataProof?.Target ?? string.Empty;
        var formulaKind = metadataProof?.DisplayKind ?? "Unknown";
        var formulaText = metadataProof?.FormulaText ?? condition;
        var proof = SymbolicProofInfo.Project(
            SymbolicProofInfo.MapStatus(status),
            metadataProof?.IsSolverBacked ?? false,
            summary,
            false,
            null,
            target,
            formulaText,
            formulaKind,
            reasons.FirstOrDefault(static reason => reason.TruthValue == SymbolicTruthValue.Unknown)?.Reason);
        return new SymbolicConditionProofSummary(
            condition,
            target,
            formulaKind,
            metadataProof?.ValueKind ?? "Unknown",
            proofArray.Length,
            unknownCount,
            provenTrueCount,
            provenFalseCount,
            unreachableCount,
            reachableCount,
            provenTrueCount + provenFalseCount + unreachableCount,
            status,
            summary,
            proof,
            status == SymbolicConditionProofSummaryStatus.AlwaysTrue,
            status == SymbolicConditionProofSummaryStatus.AlwaysFalse,
            status == SymbolicConditionProofSummaryStatus.Mixed,
            reasons);
    }

    internal static SymbolicConditionProofSummaryStatus ResolveStatus(
        int totalCount,
        int reachableCount,
        int unknownCount,
        int provenTrueCount,
        int provenFalseCount,
        int unreachableCount) => totalCount == 0
        ? SymbolicConditionProofSummaryStatus.None
        : unreachableCount == totalCount
            ? SymbolicConditionProofSummaryStatus.UnreachableOnly
            : unknownCount != 0
                ? SymbolicConditionProofSummaryStatus.Unknown
                : provenFalseCount == 0 && provenTrueCount == reachableCount
                    ? SymbolicConditionProofSummaryStatus.AlwaysTrue
                    : provenTrueCount == 0 && provenFalseCount == reachableCount
                        ? SymbolicConditionProofSummaryStatus.AlwaysFalse
                        : SymbolicConditionProofSummaryStatus.Mixed;

    internal static string CreateSummary(SymbolicConditionProofSummaryStatus status) => status switch {
        SymbolicConditionProofSummaryStatus.None => "No implication proof results were requested for this condition.",
        SymbolicConditionProofSummaryStatus.UnreachableOnly =>
            "Every candidate program point for this condition was unreachable.",
        SymbolicConditionProofSummaryStatus.AlwaysTrue =>
            "The condition is proven true at every reachable candidate program point.",
        SymbolicConditionProofSummaryStatus.AlwaysFalse =>
            "The condition is proven false at every reachable candidate program point.",
        SymbolicConditionProofSummaryStatus.Mixed =>
            "The condition has both true and false reachable proof outcomes.",
        _ => "The condition has at least one unresolved reachable proof outcome."
    };

}

internal enum SymbolicConditionProofSummaryStatus {
    None,
    UnreachableOnly,
    AlwaysTrue,
    AlwaysFalse,
    Mixed,
    Unknown
}
