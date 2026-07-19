using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal static class SymbolicCompactProjection
{
    public static SymbolicBoundedProjection<T> Project<T>(IReadOnlyList<T> values, int maxCount)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));

        ValidateMaxCount(maxCount);
        var items = maxCount == 0
            ? Array.Empty<T>()
            : values.Take(maxCount).ToArray();
        return new SymbolicBoundedProjection<T>(items, values.Count);
    }

    public static IReadOnlyList<T> Take<T>(IEnumerable<T> values, int maxCount)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));

        if (maxCount == 0) return Array.Empty<T>();

        ValidateMaxCount(maxCount);

        return values.Take(maxCount).ToArray();
    }

    private static void ValidateMaxCount(int maxCount)
    {
        if (maxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Compact output limits cannot be negative.");
    }
}

internal readonly struct SymbolicBoundedProjection<T>(IReadOnlyList<T> items, int totalCount)
{
    public IReadOnlyList<T> Items { get; } = items ?? throw new ArgumentNullException(nameof(items));
    public int TotalCount { get; } = totalCount >= items.Count
        ? totalCount
        : throw new ArgumentOutOfRangeException(nameof(totalCount));

    public int OmittedCount => TotalCount - Items.Count;

    public bool IsTruncated => OmittedCount != 0;
}

internal sealed class SymbolicConservativeUnknownDiagnostic
{
    public SymbolicConservativeUnknownDiagnostic(
        string target,
        string unknownText,
        string reason,
        IReadOnlyList<string> maybeFacts,
        int candidateProgramPointCount,
        int unreachableProgramPointCount)
    {
        Target = string.IsNullOrWhiteSpace(target) ? "path" : target;
        UnknownText = string.IsNullOrWhiteSpace(unknownText)
            ? SymbolicMergedPathFacts.FormatConservativeUnknown(Target)
            : unknownText;
        Reason = string.IsNullOrWhiteSpace(reason)
            ? "not_common_to_all_candidate_program_points"
            : reason;
        MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
        CandidateProgramPointCount = candidateProgramPointCount;
        UnreachableProgramPointCount = unreachableProgramPointCount;
    }

    public string Target { get; }

    public string UnknownText { get; }

    public string Reason { get; }

    public IReadOnlyList<string> MaybeFacts { get; }

    public int MaybeFactCount => MaybeFacts.Count;

    public int CandidateProgramPointCount { get; }

    public int UnreachableProgramPointCount { get; }

    public string GetDisplayReason()
    {
        return SymbolicReasonDisplay.Format(Reason);
    }
}

internal sealed class SymbolicMergedPathFacts
{
    private SymbolicMergedPathFacts(
        IReadOnlyList<string> alwaysFacts,
        IReadOnlyList<string> maybeFacts,
        IReadOnlyList<string> conservativeUnknowns,
        IReadOnlyList<SymbolicConservativeUnknownDiagnostic> conservativeUnknownDiagnostics,
        IReadOnlyList<string> mergedFacts,
        string mergedInvariantText,
        int candidateProgramPointCount,
        int unreachableProgramPointCount,
        bool isUnreachable)
    {
        AlwaysFacts = alwaysFacts ?? throw new ArgumentNullException(nameof(alwaysFacts));
        MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
        ConservativeUnknowns = conservativeUnknowns ?? throw new ArgumentNullException(nameof(conservativeUnknowns));
        ConservativeUnknownDiagnostics = conservativeUnknownDiagnostics ??
                                         throw new ArgumentNullException(nameof(conservativeUnknownDiagnostics));
        MergedFacts = mergedFacts ?? throw new ArgumentNullException(nameof(mergedFacts));
        MergedInvariantText = mergedInvariantText ?? throw new ArgumentNullException(nameof(mergedInvariantText));
        CandidateProgramPointCount = candidateProgramPointCount;
        UnreachableProgramPointCount = unreachableProgramPointCount;
        IsUnreachable = isUnreachable;
    }

    public IReadOnlyList<string> AlwaysFacts { get; }

    public IReadOnlyList<string> MaybeFacts { get; }

    public IReadOnlyList<string> ConservativeUnknowns { get; }

    public IReadOnlyList<SymbolicConservativeUnknownDiagnostic> ConservativeUnknownDiagnostics { get; }

    public int ConservativeUnknownCount => ConservativeUnknowns.Count;

    public IReadOnlyList<string> MergedFacts { get; }

    public string MergedInvariantText { get; }

    public int CandidateProgramPointCount { get; }

    public int UnreachableProgramPointCount { get; }

    public bool IsUnreachable { get; }

    public static SymbolicMergedPathFacts FromProgramPoints(
        IEnumerable<SymbolicProgramPointResult> programPoints)
    {
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
        foreach (var point in candidatePoints)
        {
            var conditionSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var condition in point.Invariant.Conditions)
            {
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
        int unreachableProgramPointCount)
    {
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<SymbolicConservativeUnknownDiagnostic>();
        foreach (var condition in maybeConditions)
        {
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

    internal static string FormatConservativeUnknown(string target)
    {
        return "unknown(" + (string.IsNullOrWhiteSpace(target) ? "path" : target) + ")";
    }
}

internal sealed class SymbolicSourceQueryFilter
{
    public static readonly SymbolicSourceQueryFilter Empty = new();

    public SymbolicSourceQueryFilter(
        IEnumerable<string>? nodeKinds = null,
        bool requireFacts = false,
        IEnumerable<SymbolicReachability>? reachability = null,
        IEnumerable<string>? methodNames = null,
        bool requirePathConditions = false,
        IEnumerable<string>? conditionTargets = null,
        IEnumerable<string>? conditionTexts = null,
        IEnumerable<string>? conditionTextContains = null,
        IEnumerable<string>? methodNameContains = null,
        IEnumerable<int>? lines = null,
        int? lineStart = null,
        int? lineEnd = null,
        IEnumerable<string>? programPointKinds = null,
        bool requireProofs = false,
        IEnumerable<SymbolicTruthValue>? proofOutcomes = null,
        IEnumerable<string>? proofConditions = null,
        IEnumerable<string>? proofConditionContains = null)
    {
        NodeKinds = nodeKinds?
            .Where(static kind => !string.IsNullOrWhiteSpace(kind))
            .Select(static kind => kind.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
        RequireFacts = requireFacts;
        Reachability = reachability?
            .Distinct()
            .ToArray() ?? Array.Empty<SymbolicReachability>();
        MethodNames = NormalizeStrings(methodNames, StringComparer.OrdinalIgnoreCase);
        RequirePathConditions = requirePathConditions;
        ConditionTargets = NormalizeStrings(conditionTargets, StringComparer.OrdinalIgnoreCase);
        ConditionTexts = NormalizeStrings(conditionTexts, StringComparer.Ordinal);
        ConditionTextContains = NormalizeStrings(conditionTextContains, StringComparer.OrdinalIgnoreCase);
        MethodNameContains = NormalizeStrings(methodNameContains, StringComparer.OrdinalIgnoreCase);
        Lines = NormalizePositiveIntegers(lines, nameof(lines));
        LineStart = ValidatePositiveLine(lineStart, nameof(lineStart));
        LineEnd = ValidatePositiveLine(lineEnd, nameof(lineEnd));
        if (LineStart.HasValue && LineEnd.HasValue && LineStart.Value > LineEnd.Value)
            throw new ArgumentException("LineStart cannot be greater than LineEnd.", nameof(lineStart));

        ProgramPointKinds = NormalizeProgramPointKinds(programPointKinds);
        RequireProofs = requireProofs;
        ProofOutcomes = proofOutcomes?
            .Distinct()
            .ToArray() ?? Array.Empty<SymbolicTruthValue>();
        ProofConditions = NormalizeStrings(proofConditions, StringComparer.Ordinal);
        ProofConditionContains = NormalizeStrings(proofConditionContains, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> NodeKinds { get; }

    public bool RequireFacts { get; }

    public IReadOnlyList<SymbolicReachability> Reachability { get; }

    public IReadOnlyList<string> MethodNames { get; }

    public bool RequirePathConditions { get; }

    public IReadOnlyList<string> ConditionTargets { get; }

    public IReadOnlyList<string> ConditionTexts { get; }

    public IReadOnlyList<string> ConditionTextContains { get; }

    public IReadOnlyList<string> MethodNameContains { get; }

    public IReadOnlyList<int> Lines { get; }

    public int? LineStart { get; }

    public int? LineEnd { get; }

    public IReadOnlyList<string> ProgramPointKinds { get; }

    public bool RequireProofs { get; }

    public IReadOnlyList<SymbolicTruthValue> ProofOutcomes { get; }

    public IReadOnlyList<string> ProofConditions { get; }

    public IReadOnlyList<string> ProofConditionContains { get; }

    public bool IsEmpty =>
        NodeKinds.Count == 0 &&
        !RequireFacts &&
        Reachability.Count == 0 &&
        MethodNames.Count == 0 &&
        !RequirePathConditions &&
        ConditionTargets.Count == 0 &&
        ConditionTexts.Count == 0 &&
        ConditionTextContains.Count == 0 &&
        MethodNameContains.Count == 0 &&
        Lines.Count == 0 &&
        !LineStart.HasValue &&
        !LineEnd.HasValue &&
        ProgramPointKinds.Count == 0 &&
        !RequireProofs &&
        ProofOutcomes.Count == 0 &&
        ProofConditions.Count == 0 &&
        ProofConditionContains.Count == 0;

    public bool Matches(SymbolicProgramPointResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        if (RequireFacts && result.Facts.Count == 0) return false;

        if (NodeKinds.Count != 0 &&
            !NodeKinds.Any(kind => string.Equals(kind, result.NodeKind, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (ProgramPointKinds.Count != 0 &&
            !ProgramPointKinds.Any(kind =>
                string.Equals(kind, result.ProgramPointKind, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (Lines.Count != 0 && !Lines.Contains(result.Line)) return false;

        if (LineStart.HasValue && result.Line < LineStart.Value) return false;

        if (LineEnd.HasValue && result.Line > LineEnd.Value) return false;

        if (Reachability.Count != 0 && !Reachability.Contains(result.Reachability)) return false;

        if (MethodNames.Count != 0 &&
            (string.IsNullOrWhiteSpace(result.MethodName) ||
             !MethodNames.Any(methodName =>
                 string.Equals(methodName, result.MethodName, StringComparison.OrdinalIgnoreCase))))
            return false;

        var resultMethodName = result.MethodName;
        if (MethodNameContains.Count != 0 &&
            (string.IsNullOrWhiteSpace(resultMethodName) ||
             !MethodNameContains.Any(text => resultMethodName!.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)))
            return false;

        if (RequirePathConditions && result.PathConditionCount == 0) return false;

        if (ConditionTargets.Count != 0 &&
            !result.Invariant.Conditions.Any(condition =>
                ConditionTargets.Any(target =>
                    string.Equals(target, condition.Target, StringComparison.OrdinalIgnoreCase))))
            return false;

        if (ConditionTexts.Count != 0 &&
            !result.Invariant.Conditions.Any(condition =>
                ConditionTexts.Any(text => string.Equals(text, condition.Text, StringComparison.Ordinal))))
            return false;

        if (ConditionTextContains.Count != 0 &&
            !result.Invariant.Conditions.Any(condition =>
                ConditionTextContains.Any(text =>
                    condition.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)))
            return false;

        if (RequireProofs && result.ConditionProofs.Count == 0) return false;

        if (ProofOutcomes.Count != 0 &&
            !result.ConditionProofs.Any(proof => ProofOutcomes.Contains(proof.TruthValue)))
            return false;

        if (ProofConditions.Count != 0 &&
            !result.ConditionProofs.Any(proof =>
                ProofConditions.Any(condition => string.Equals(condition, proof.Condition, StringComparison.Ordinal))))
            return false;

        if (ProofConditionContains.Count != 0 &&
            !result.ConditionProofs.Any(proof =>
                ProofConditionContains.Any(text =>
                    proof.Condition.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)))
            return false;

        return true;
    }

    private static IReadOnlyList<string> NormalizeStrings(
        IEnumerable<string>? values,
        StringComparer comparer)
    {
        return values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(comparer)
            .ToArray() ?? Array.Empty<string>();
    }

    private static IReadOnlyList<string> NormalizeProgramPointKinds(IEnumerable<string>? values)
    {
        return values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => NormalizeProgramPointKindFilter(value.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
    }

    private static string NormalizeProgramPointKindFilter(string value)
    {
        return SymbolicProgramPointKinds.TryNormalizeKnownKind(value, out var normalizedKind)
            ? normalizedKind
            : value;
    }

    private static IReadOnlyList<int> NormalizePositiveIntegers(IEnumerable<int>? values, string paramName)
    {
        if (values == null) return Array.Empty<int>();

        var normalized = new SortedSet<int>();
        foreach (var value in values)
        {
            if (value < 1) throw new ArgumentOutOfRangeException(paramName, "Line filters must be 1 or greater.");

            normalized.Add(value);
        }

        return normalized.ToArray();
    }

    private static int? ValidatePositiveLine(int? value, string paramName)
    {
        if (value.HasValue && value.Value < 1)
            throw new ArgumentOutOfRangeException(paramName, "Line filters must be 1 or greater.");

        return value;
    }
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
    int ProofUnreachableCount)
{
    internal static SymbolicQueryMetrics FromProgramPoints(IEnumerable<SymbolicProgramPointResult> programPoints)
    {
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

internal static class SymbolicConditionProofProjection
{
    internal static IReadOnlyList<SymbolicConditionProofSummary> FromProgramPoints(
        IEnumerable<SymbolicProgramPointResult> programPoints)
    {
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
        IEnumerable<SymbolicConditionProofResult> proofs)
    {
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
        var proof = SymbolicProofProjection.FromSolverBackedResult(
                SymbolicProofProjection.MapStatus(status),
                metadataProof?.IsSolverBacked ?? false,
                reasons.FirstOrDefault(static reason => reason.TruthValue == SymbolicTruthValue.Unknown)?.Reason)
            .CreateInfo(summary, false, null, target, formulaText, formulaKind);
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

    internal static string CreateSummary(SymbolicConditionProofSummaryStatus status) => status switch
    {
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

internal enum SymbolicConditionProofSummaryStatus
{
    None,
    UnreachableOnly,
    AlwaysTrue,
    AlwaysFalse,
    Mixed,
    Unknown
}
