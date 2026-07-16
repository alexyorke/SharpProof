using System.Text.Json.Serialization;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed record SymbolicCompactInvariantTargetSummary(
    string Target,
    string Status,
    string StatusReason,
    string ReasonCode,
    string Summary,
    int MustFactCount,
    IReadOnlyList<string> MustFacts,
    int MaybeFactCount,
    IReadOnlyList<string> MaybeFacts,
    int UnknownFactCount,
    IReadOnlyList<string> UnknownFacts,
    bool MustFactsTruncated,
    bool MaybeFactsTruncated,
    bool UnknownFactsTruncated)
{
    internal bool IsTruncated => MustFactsTruncated || MaybeFactsTruncated || UnknownFactsTruncated;

    internal static SymbolicCompactInvariantTargetSummary FromSummary(
        SymbolicInvariantTargetSummary summary,
        SymbolicCompactQueryOptions options)
    {
        return new SymbolicCompactInvariantTargetSummary(
            summary.Target,
            summary.Status.ToString(),
            summary.StatusReason,
            summary.ReasonCode,
            summary.Summary,
            summary.MustFactCount,
            SymbolicCompactProjection.Take(summary.MustFacts, options.MaxConditions),
            summary.MaybeFactCount,
            SymbolicCompactProjection.Take(summary.MaybeFacts, options.MaxConditions),
            summary.UnknownFactCount,
            SymbolicCompactProjection.Take(summary.UnknownFacts, options.MaxConditions),
            summary.MustFactCount > options.MaxConditions,
            summary.MaybeFactCount > options.MaxConditions,
            summary.UnknownFactCount > options.MaxConditions);
    }
}

internal sealed record SymbolicCompactInvariantTargetPathSummary(
    string Target,
    int PathConditionCount,
    int SmtConditionCount,
    int ConservativeUnknownCount,
    int ProgramPointCount,
    int ReachableProgramPointCount,
    int ProofTotalCount,
    int ProofUnknownCount,
    int ProofProvenTrueCount,
    int ProofProvenFalseCount,
    int ProofUnreachableCount,
    IReadOnlyList<string> Conditions,
    bool ConditionsTruncated,
    string StatusReason,
    string ReasonCode,
    string Summary)
{
    internal static SymbolicCompactInvariantTargetPathSummary FromSummary(
        SymbolicInvariantTargetPathSummary summary,
        SymbolicCompactQueryOptions options)
    {
        var conditions = SymbolicCompactProjection.Take(summary.Conditions, options.MaxConditions);
        return new SymbolicCompactInvariantTargetPathSummary(
            summary.Target,
            summary.PathConditionCount,
            summary.SmtConditionCount,
            summary.ConservativeUnknownCount,
            summary.ProgramPointCount,
            summary.ReachableProgramPointCount,
            summary.ProofTotalCount,
            summary.ProofUnknownCount,
            summary.ProofProvenTrueCount,
            summary.ProofProvenFalseCount,
            summary.ProofUnreachableCount,
            conditions,
            summary.ConditionsTruncated || summary.Conditions.Count > conditions.Count,
            summary.StatusReason,
            summary.ReasonCode,
            summary.Summary);
    }
}

internal sealed record SymbolicCompactInvariantQueryDiagnostic(
    string Code,
    string Severity,
    string Message,
    int Count,
    int EvidenceTotalCount,
    IReadOnlyList<string> Evidence,
    bool EvidenceTruncated)
{
    internal static SymbolicCompactInvariantQueryDiagnostic FromDiagnostic(
        SymbolicInvariantQueryDiagnostic diagnostic,
        SymbolicCompactQueryOptions options)
    {
        return new SymbolicCompactInvariantQueryDiagnostic(
            diagnostic.Code,
            diagnostic.Severity,
            diagnostic.Message,
            diagnostic.Count,
            diagnostic.EvidenceTotalCount,
            SymbolicCompactProjection.Take(diagnostic.Evidence, options.MaxConditions),
            diagnostic.EvidenceTruncated || diagnostic.Evidence.Count > options.MaxConditions);
    }
}

internal sealed class SymbolicCompactSmtDiagnostics(SymbolicSmtDiagnosticsSnapshot snapshot)
{
    public bool IsConfigured => snapshot.IsConfigured;
    public string Mode => snapshot.Mode.ToString();
    public bool IsEnabled => snapshot.IsEnabled;
    public int QueryTimeoutMs => snapshot.QueryTimeoutMs;
    public int MethodBudgetMs => snapshot.MethodBudgetMs;
    public int MaxPathConditions => snapshot.MaxPathConditions;
    public int MaxExpressionNodes => snapshot.MaxExpressionNodes;
    public int ExecutedQueryCount => snapshot.ExecutedQueryCount;
    public int CacheEntryCount => snapshot.CacheEntryCount;
    public SmtAnalysisHealth Health => snapshot.Health;
    public SmtSolverLifecycleOptions Lifecycle => snapshot.Lifecycle;

    internal static SymbolicCompactSmtDiagnostics FromDiagnostics(SymbolicSmtDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new SymbolicCompactSmtDiagnostics(diagnostics.Snapshot);
    }
}

internal abstract class SymbolicSmtDiagnosticsProjectionBase(SymbolicCompactSmtDiagnostics smtDiagnostics)
{
    [JsonPropertyOrder(100)] public bool SmtConfigured => smtDiagnostics.IsConfigured;
    [JsonPropertyOrder(101)] public bool SmtEnabled => smtDiagnostics.IsEnabled;
    [JsonPropertyOrder(102)] public int SmtExecutedQueryCount => smtDiagnostics.ExecutedQueryCount;
    [JsonPropertyOrder(103)] public int SmtCacheEntryCount => smtDiagnostics.CacheEntryCount;
    [JsonPropertyOrder(104)] public int SmtQueryTimeoutMs => smtDiagnostics.QueryTimeoutMs;
    [JsonPropertyOrder(105)] public int SmtMethodBudgetMs => smtDiagnostics.MethodBudgetMs;
    [JsonPropertyOrder(106)] public int SmtMaxPathConditions => smtDiagnostics.MaxPathConditions;
    [JsonPropertyOrder(107)] public int SmtMaxExpressionNodes => smtDiagnostics.MaxExpressionNodes;
}

internal sealed class SymbolicCompactAnalysisSummary(
    SymbolicCompactInvariantQueryView invariantQuery,
    SymbolicProgramPointSummary programPointSummary,
    SymbolicCompactSmtDiagnostics smtDiagnostics,
    SymbolicAnalysisTruncationInfo analysisTruncation)
    : SymbolicSmtDiagnosticsProjectionBase(smtDiagnostics)
{
    public int ProgramPointCount => programPointSummary.ProgramPointCount;
    public int InvariantConditionCount => MustFactCount + UnknownFactCount;
    public int ConservativeUnknownCount => UnknownFactCount;
    public int MustFactCount => invariantQuery.MustFactCount;
    public int MaybeFactCount => invariantQuery.MaybeFactCount;
    public int UnknownFactCount => invariantQuery.UnknownFactCount;
    public string InvariantStatus => invariantQuery.Status;
    public string InvariantStatusReason => invariantQuery.StatusReason;
    public string InvariantSummary => invariantQuery.Summary;
    public int InvariantDiagnosticCount => invariantQuery.DiagnosticCount;
    public int TotalPathConditionCount => programPointSummary.TotalPathConditionCount;
    public int MaxPathConditionCount => programPointSummary.MaxPathConditionCount;
    public int ReachabilityCheckedCount =>
        programPointSummary.Reachability.ReachableCount +
        programPointSummary.Reachability.UnreachableCount +
        programPointSummary.Reachability.UnknownCount;
    public int ReachabilityKnownCount =>
        programPointSummary.Reachability.ReachableCount + programPointSummary.Reachability.UnreachableCount;
    public int ReachabilityUnknownCount => programPointSummary.Reachability.UnknownCount;
    public int ReachabilityNotCheckedCount => programPointSummary.Reachability.NotCheckedCount;
    public int ProofTotalCount => programPointSummary.ProofOutcomes.TotalCount;
    public int ProofResolvedCount =>
        programPointSummary.ProofOutcomes.ProvenTrueCount +
        programPointSummary.ProofOutcomes.ProvenFalseCount +
        programPointSummary.ProofOutcomes.UnreachableCount;
    public int ProofUnknownCount => programPointSummary.ProofOutcomes.UnknownCount;

    [JsonPropertyOrder(108)] public bool AnalysisTruncated => analysisTruncation.IsTruncated;
    [JsonPropertyOrder(109)] public bool HasUnresolvedAnalysis =>
        ConservativeUnknownCount != 0 ||
        ReachabilityUnknownCount != 0 ||
        ReachabilityNotCheckedCount != 0 ||
        ProofUnknownCount != 0 ||
        AnalysisTruncated;

    internal static SymbolicCompactAnalysisSummary From(
        SymbolicCompactInvariantQueryView invariantQuery,
        SymbolicProgramPointSummary programPointSummary,
        SymbolicCompactSmtDiagnostics smtDiagnostics,
        SymbolicAnalysisTruncationInfo analysisTruncation)
    {
        return new SymbolicCompactAnalysisSummary(invariantQuery, programPointSummary, smtDiagnostics,
            analysisTruncation);
    }
}

internal sealed class SymbolicCompactOutputTruncation(
    bool lines,
    bool programPoints,
    bool facts,
    bool conditions,
    bool proofs)
{
    public bool Lines { get; } = lines;
    public bool ProgramPoints { get; } = programPoints;
    public bool Facts { get; } = facts;
    public bool Conditions { get; } = conditions;
    public bool Proofs { get; } = proofs;
    public bool IsTruncated => Lines || ProgramPoints || Facts || Conditions || Proofs;

    internal static SymbolicCompactOutputTruncation FromInvariant(SymbolicCompactInvariantSummary invariant)
    {
        return new SymbolicCompactOutputTruncation(
            false,
            false,
            invariant.RawFactsTruncated,
            invariant.ConditionsTruncated || invariant.TargetsTruncated || invariant.MergedPathFactsTruncated,
            false);
    }

    internal static SymbolicCompactOutputTruncation Combine(
        IEnumerable<SymbolicCompactOutputTruncation> truncations)
    {
        ArgumentNullException.ThrowIfNull(truncations);
        var result = new bool[5];
        foreach (var truncation in truncations)
        {
            if (truncation == null) continue;
            result[0] |= truncation.Lines;
            result[1] |= truncation.ProgramPoints;
            result[2] |= truncation.Facts;
            result[3] |= truncation.Conditions;
            result[4] |= truncation.Proofs;
        }

        return new SymbolicCompactOutputTruncation(result[0], result[1], result[2], result[3], result[4]);
    }

    internal static SymbolicCompactOutputTruncation Combine(
        params SymbolicCompactOutputTruncation[] truncations) => Combine(truncations.AsEnumerable());
}
