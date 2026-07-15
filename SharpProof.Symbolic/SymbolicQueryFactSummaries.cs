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

internal readonly struct SymbolicBoundedProjection<T>
{
    public SymbolicBoundedProjection(IReadOnlyList<T> items, int totalCount)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        if (totalCount < items.Count)
            throw new ArgumentOutOfRangeException(nameof(totalCount));

        TotalCount = totalCount;
    }

    public IReadOnlyList<T> Items { get; }

    public int TotalCount { get; }

    public int OmittedCount => TotalCount - Items.Count;

    public bool IsTruncated => OmittedCount != 0;
}

public sealed class SymbolicConservativeUnknownDiagnostic
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

public sealed class SymbolicMergedPathFacts
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
            SymbolicInvariantService.FormatMergedInvariantFacts(mergedFacts),
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

public sealed class SymbolicSourceQueryFilter
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

public sealed class SymbolicReachabilitySummary
{
    public SymbolicReachabilitySummary(
        int notCheckedCount,
        int unknownCount,
        int reachableCount,
        int unreachableCount)
    {
        NotCheckedCount = notCheckedCount;
        UnknownCount = unknownCount;
        ReachableCount = reachableCount;
        UnreachableCount = unreachableCount;
    }

    public int NotCheckedCount { get; }

    public int UnknownCount { get; }

    public int ReachableCount { get; }

    public int UnreachableCount { get; }

    public static SymbolicReachabilitySummary FromProgramPoints(
        IEnumerable<SymbolicProgramPointResult> programPoints)
    {
        if (programPoints == null) throw new ArgumentNullException(nameof(programPoints));

        var notCheckedCount = 0;
        var unknownCount = 0;
        var reachableCount = 0;
        var unreachableCount = 0;
        foreach (var point in programPoints)
            switch (point.Reachability)
            {
                case SymbolicReachability.NotChecked:
                    notCheckedCount++;
                    break;
                case SymbolicReachability.Unknown:
                    unknownCount++;
                    break;
                case SymbolicReachability.Reachable:
                    reachableCount++;
                    break;
                case SymbolicReachability.Unreachable:
                    unreachableCount++;
                    break;
            }

        return new SymbolicReachabilitySummary(
            notCheckedCount,
            unknownCount,
            reachableCount,
            unreachableCount);
    }
}

public sealed class SymbolicProofOutcomeSummary
{
    public SymbolicProofOutcomeSummary(
        int totalCount,
        int unknownCount,
        int provenTrueCount,
        int provenFalseCount,
        int unreachableCount)
    {
        TotalCount = totalCount;
        UnknownCount = unknownCount;
        ProvenTrueCount = provenTrueCount;
        ProvenFalseCount = provenFalseCount;
        UnreachableCount = unreachableCount;
    }

    public int TotalCount { get; }

    public int UnknownCount { get; }

    public int ProvenTrueCount { get; }

    public int ProvenFalseCount { get; }

    public int UnreachableCount { get; }

    public static SymbolicProofOutcomeSummary FromProofs(
        IEnumerable<SymbolicConditionProofResult> proofs)
    {
        if (proofs == null) throw new ArgumentNullException(nameof(proofs));

        var totalCount = 0;
        var unknownCount = 0;
        var provenTrueCount = 0;
        var provenFalseCount = 0;
        var unreachableCount = 0;
        foreach (var proof in proofs)
        {
            totalCount++;
            switch (proof.TruthValue)
            {
                case SymbolicTruthValue.Unknown:
                    unknownCount++;
                    break;
                case SymbolicTruthValue.ProvenTrue:
                    provenTrueCount++;
                    break;
                case SymbolicTruthValue.ProvenFalse:
                    provenFalseCount++;
                    break;
                case SymbolicTruthValue.Unreachable:
                    unreachableCount++;
                    break;
            }
        }

        return new SymbolicProofOutcomeSummary(
            totalCount,
            unknownCount,
            provenTrueCount,
            provenFalseCount,
            unreachableCount);
    }
}

public sealed class SymbolicProgramPointSummary
{
    public SymbolicProgramPointSummary(
        int programPointCount,
        int totalPathConditionCount,
        int maxPathConditionCount,
        SymbolicReachabilitySummary reachability,
        SymbolicProofOutcomeSummary proofOutcomes)
    {
        ProgramPointCount = programPointCount;
        TotalPathConditionCount = totalPathConditionCount;
        MaxPathConditionCount = maxPathConditionCount;
        Reachability = reachability ?? throw new ArgumentNullException(nameof(reachability));
        ProofOutcomes = proofOutcomes ?? throw new ArgumentNullException(nameof(proofOutcomes));
    }

    public int ProgramPointCount { get; }

    public int TotalPathConditionCount { get; }

    public int MaxPathConditionCount { get; }

    public SymbolicReachabilitySummary Reachability { get; }

    public SymbolicProofOutcomeSummary ProofOutcomes { get; }

    public static SymbolicProgramPointSummary FromProgramPoints(
        IEnumerable<SymbolicProgramPointResult> programPoints)
    {
        if (programPoints == null) throw new ArgumentNullException(nameof(programPoints));

        var points = programPoints.ToArray();
        var totalPathConditionCount = 0;
        var maxPathConditionCount = 0;
        foreach (var point in points)
        {
            var pathConditionCount = point.PathConditionCount;
            totalPathConditionCount += pathConditionCount;
            if (pathConditionCount > maxPathConditionCount) maxPathConditionCount = pathConditionCount;
        }

        return new SymbolicProgramPointSummary(
            points.Length,
            totalPathConditionCount,
            maxPathConditionCount,
            SymbolicReachabilitySummary.FromProgramPoints(points),
            SymbolicProofOutcomeSummary.FromProofs(points.SelectMany(static point => point.ConditionProofs)));
    }
}

public sealed class SymbolicConditionProofSummary
{
    public SymbolicConditionProofSummary(
        string condition,
        int unknownCount,
        int provenTrueCount,
        int provenFalseCount,
        int unreachableCount,
        int? totalCount = null,
        IReadOnlyList<SymbolicConditionProofReasonSummary>? reasons = null,
        string? target = null,
        string? formulaKind = null,
        string? valueKind = null,
        string? formulaText = null,
        bool isSolverBacked = false)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Target = target ?? string.Empty;
        FormulaKind = formulaKind ?? "Unknown";
        ValueKind = valueKind ?? "Unknown";
        FormulaText = string.IsNullOrWhiteSpace(formulaText) ? Condition : formulaText!;
        IsSolverBacked = isSolverBacked;
        DisplayKind = FormulaKind;
        UnknownCount = unknownCount;
        ProvenTrueCount = provenTrueCount;
        ProvenFalseCount = provenFalseCount;
        UnreachableCount = unreachableCount;
        TotalCount = totalCount ?? unknownCount + provenTrueCount + provenFalseCount + unreachableCount;
        Reasons = reasons ?? Array.Empty<SymbolicConditionProofReasonSummary>();
        ReachableCount = TotalCount - UnreachableCount;
        ResolvedCount = ProvenTrueCount + ProvenFalseCount + UnreachableCount;
        Status = ResolveStatus(TotalCount, ReachableCount, UnknownCount, ProvenTrueCount, ProvenFalseCount,
            UnreachableCount);
        Summary = CreateSummary(Status);
        var unknownReason = Status == SymbolicConditionProofSummaryStatus.Unknown
            ? Reasons.FirstOrDefault(static item => item.TruthValue == SymbolicTruthValue.Unknown)?.Reason ??
              string.Empty
            : null;
        Proof = SymbolicProofProjection
            .FromSolverBackedResult(MapProofStatus(Status), IsSolverBacked, unknownReason)
            .CreateInfo(Summary, false, null, Target, FormulaText, FormulaKind);
    }

    public string Condition { get; }

    public string Target { get; }

    public string DisplayKind { get; }

    internal string FormulaKind { get; }

    public string ValueKind { get; }

    internal string FormulaText { get; }

    internal bool IsSolverBacked { get; }

    public int TotalCount { get; }

    public int UnknownCount { get; }

    public int ProvenTrueCount { get; }

    public int ProvenFalseCount { get; }

    public int UnreachableCount { get; }

    public int ReachableCount { get; }

    public int ResolvedCount { get; }

    public SymbolicConditionProofSummaryStatus Status { get; }

    public string Summary { get; }

    public SymbolicProofInfo Proof { get; }

    public bool HoldsOnAllReachablePoints => Status == SymbolicConditionProofSummaryStatus.AlwaysTrue;

    public bool RefutedOnAllReachablePoints => Status == SymbolicConditionProofSummaryStatus.AlwaysFalse;

    public bool HasMixedReachableOutcomes => Status == SymbolicConditionProofSummaryStatus.Mixed;

    public IReadOnlyList<SymbolicConditionProofReasonSummary> Reasons { get; }

    public static IReadOnlyList<SymbolicConditionProofSummary> FromProgramPoints(
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
        var unknownCount = 0;
        var provenTrueCount = 0;
        var provenFalseCount = 0;
        var unreachableCount = 0;
        foreach (var proof in proofArray)
            switch (proof.TruthValue)
            {
                case SymbolicTruthValue.Unknown:
                    unknownCount++;
                    break;
                case SymbolicTruthValue.ProvenTrue:
                    provenTrueCount++;
                    break;
                case SymbolicTruthValue.ProvenFalse:
                    provenFalseCount++;
                    break;
                case SymbolicTruthValue.Unreachable:
                    unreachableCount++;
                    break;
            }

        var metadataProof = proofArray.FirstOrDefault(static proof => proof.IsSolverBacked) ??
                            proofArray.FirstOrDefault();
        return new SymbolicConditionProofSummary(
            condition,
            unknownCount,
            provenTrueCount,
            provenFalseCount,
            unreachableCount,
            reasons: proofArray
                .GroupBy(
                    static proof => new ProofReasonKey(proof.TruthValue, proof.Reason),
                    ProofReasonKeyComparer.Instance)
                .OrderBy(static group => group.Key.TruthValue)
                .ThenBy(static group => group.Key.Reason, StringComparer.Ordinal)
                .Select(static group => new SymbolicConditionProofReasonSummary(
                    group.Key.TruthValue,
                    group.Key.Reason,
                    group.Count()))
                .ToArray(),
            target: metadataProof?.Target,
            formulaKind: metadataProof?.FormulaKind,
            valueKind: metadataProof?.ValueKind,
            formulaText: metadataProof?.FormulaText,
            isSolverBacked: metadataProof?.IsSolverBacked ?? false);
    }

    private static SymbolicConditionProofSummaryStatus ResolveStatus(
        int totalCount,
        int reachableCount,
        int unknownCount,
        int provenTrueCount,
        int provenFalseCount,
        int unreachableCount)
    {
        if (totalCount == 0) return SymbolicConditionProofSummaryStatus.None;

        if (unreachableCount == totalCount) return SymbolicConditionProofSummaryStatus.UnreachableOnly;

        if (unknownCount != 0) return SymbolicConditionProofSummaryStatus.Unknown;

        if (provenFalseCount == 0 && provenTrueCount == reachableCount)
            return SymbolicConditionProofSummaryStatus.AlwaysTrue;

        if (provenTrueCount == 0 && provenFalseCount == reachableCount)
            return SymbolicConditionProofSummaryStatus.AlwaysFalse;

        return SymbolicConditionProofSummaryStatus.Mixed;
    }

    private static string CreateSummary(SymbolicConditionProofSummaryStatus status)
    {
        switch (status)
        {
            case SymbolicConditionProofSummaryStatus.None:
                return "No implication proof results were requested for this condition.";
            case SymbolicConditionProofSummaryStatus.UnreachableOnly:
                return "Every candidate program point for this condition was unreachable.";
            case SymbolicConditionProofSummaryStatus.AlwaysTrue:
                return "The condition is proven true at every reachable candidate program point.";
            case SymbolicConditionProofSummaryStatus.AlwaysFalse:
                return "The condition is proven false at every reachable candidate program point.";
            case SymbolicConditionProofSummaryStatus.Mixed:
                return "The condition has both true and false reachable proof outcomes.";
            default:
                return "The condition has at least one unresolved reachable proof outcome.";
        }
    }

    private static SymbolicProofStatus MapProofStatus(SymbolicConditionProofSummaryStatus status)
    {
        return status switch
        {
            SymbolicConditionProofSummaryStatus.AlwaysTrue => SymbolicProofStatus.ProvenTrue,
            SymbolicConditionProofSummaryStatus.AlwaysFalse => SymbolicProofStatus.ProvenFalse,
            SymbolicConditionProofSummaryStatus.UnreachableOnly => SymbolicProofStatus.Unreachable,
            _ => SymbolicProofStatus.Unknown
        };
    }

    private readonly struct ProofReasonKey(SymbolicTruthValue truthValue, string? reason)
    {
        public SymbolicTruthValue TruthValue { get; } = truthValue;

        public string Reason { get; } = reason ?? string.Empty;
    }

    private sealed class ProofReasonKeyComparer : IEqualityComparer<ProofReasonKey>
    {
        public static readonly ProofReasonKeyComparer Instance = new();

        public bool Equals(ProofReasonKey x, ProofReasonKey y)
        {
            return x.TruthValue == y.TruthValue &&
                   string.Equals(x.Reason, y.Reason, StringComparison.Ordinal);
        }

        public int GetHashCode(ProofReasonKey obj)
        {
            unchecked
            {
                return ((int)obj.TruthValue * 397) ^ StringComparer.Ordinal.GetHashCode(obj.Reason);
            }
        }
    }
}

public sealed class SymbolicConditionProofReasonSummary
{
    public SymbolicConditionProofReasonSummary(
        SymbolicTruthValue truthValue,
        string reason,
        int count)
    {
        TruthValue = truthValue;
        Reason = reason ?? string.Empty;
        Count = count;
    }

    public SymbolicTruthValue TruthValue { get; }

    public string Reason { get; }

    public int Count { get; }

    public string GetDisplayReason()
    {
        return SymbolicReasonDisplay.Format(Reason);
    }
}

public enum SymbolicConditionProofSummaryStatus
{
    None,
    UnreachableOnly,
    AlwaysTrue,
    AlwaysFalse,
    Mixed,
    Unknown
}
