using System.Text.Json;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed record SymbolicCompactInvariantTargetSummary(
    JsonElement Json,
    string Target,
    bool IsTruncated) : ISymbolicRawJsonProjection
{
    internal static SymbolicCompactInvariantTargetSummary FromSummary(
        SymbolicInvariantTargetSummary summary,
        SymbolicCompactQueryOptions options)
    {
        var mustFacts = SymbolicCompactProjection.Take(summary.MustFacts, options.MaxConditions);
        var maybeFacts = SymbolicCompactProjection.Take(summary.MaybeFacts, options.MaxConditions);
        var unknownFacts = SymbolicCompactProjection.Take(summary.UnknownFacts, options.MaxConditions);
        var mustTruncated = summary.MustFactCount > options.MaxConditions;
        var maybeTruncated = summary.MaybeFactCount > options.MaxConditions;
        var unknownTruncated = summary.UnknownFactCount > options.MaxConditions;
        return new SymbolicCompactInvariantTargetSummary(
            SymbolicOrderedJson.Object(
                ("target", summary.Target),
                ("status", summary.Status.ToString()),
                ("statusReason", summary.StatusReason),
                ("reasonCode", summary.ReasonCode),
                ("summary", summary.Summary),
                ("mustFactCount", summary.MustFactCount),
                ("mustFacts", mustFacts),
                ("maybeFactCount", summary.MaybeFactCount),
                ("maybeFacts", maybeFacts),
                ("unknownFactCount", summary.UnknownFactCount),
                ("unknownFacts", unknownFacts),
                ("mustFactsTruncated", mustTruncated),
                ("maybeFactsTruncated", maybeTruncated),
                ("unknownFactsTruncated", unknownTruncated)),
            summary.Target,
            mustTruncated || maybeTruncated || unknownTruncated);
    }
}

internal sealed record SymbolicCompactInvariantTargetPathSummary(
    JsonElement Json,
    string Target,
    bool ConditionsTruncated) : ISymbolicRawJsonProjection
{
    internal static SymbolicCompactInvariantTargetPathSummary FromSummary(
        SymbolicInvariantTargetPathSummary summary,
        SymbolicCompactQueryOptions options)
    {
        var conditions = SymbolicCompactProjection.Take(summary.Conditions, options.MaxConditions);
        var truncated = summary.ConditionsTruncated || summary.Conditions.Count > conditions.Count;
        return new SymbolicCompactInvariantTargetPathSummary(
            SymbolicOrderedJson.Object(
                ("target", summary.Target),
                ("pathConditionCount", summary.PathConditionCount),
                ("smtConditionCount", summary.SmtConditionCount),
                ("conservativeUnknownCount", summary.ConservativeUnknownCount),
                ("programPointCount", summary.ProgramPointCount),
                ("reachableProgramPointCount", summary.ReachableProgramPointCount),
                ("proofTotalCount", summary.ProofTotalCount),
                ("proofUnknownCount", summary.ProofUnknownCount),
                ("proofProvenTrueCount", summary.ProofProvenTrueCount),
                ("proofProvenFalseCount", summary.ProofProvenFalseCount),
                ("proofUnreachableCount", summary.ProofUnreachableCount),
                ("conditions", conditions),
                ("conditionsTruncated", truncated),
                ("statusReason", summary.StatusReason),
                ("reasonCode", summary.ReasonCode),
                ("summary", summary.Summary)),
            summary.Target,
            truncated);
    }
}

internal sealed record SymbolicCompactInvariantQueryDiagnostic(
    JsonElement Json,
    string Code,
    string Message,
    bool EvidenceTruncated) : ISymbolicRawJsonProjection
{
    internal static SymbolicCompactInvariantQueryDiagnostic FromDiagnostic(
        SymbolicInvariantQueryDiagnostic diagnostic,
        SymbolicCompactQueryOptions options)
    {
        var evidence = SymbolicCompactProjection.Take(diagnostic.Evidence, options.MaxConditions);
        var truncated = diagnostic.EvidenceTruncated || diagnostic.Evidence.Count > options.MaxConditions;
        return new SymbolicCompactInvariantQueryDiagnostic(
            SymbolicOrderedJson.Object(
                ("code", diagnostic.Code),
                ("severity", diagnostic.Severity),
                ("message", diagnostic.Message),
                ("count", diagnostic.Count),
                ("evidenceTotalCount", diagnostic.EvidenceTotalCount),
                ("evidence", evidence),
                ("evidenceTruncated", truncated)),
            diagnostic.Code,
            diagnostic.Message,
            truncated);
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

internal sealed record SymbolicCompactAnalysisSummary(
    JsonElement Json,
    bool HasUnresolvedAnalysis,
    int TotalPathConditionCount,
    int MaxPathConditionCount,
    int ProofTotalCount,
    int ProofUnknownCount,
    int ConservativeUnknownCount,
    int ReachabilityUnknownCount,
    int ReachabilityNotCheckedCount) : ISymbolicRawJsonProjection
{

    internal static SymbolicCompactAnalysisSummary From(
        SymbolicCompactInvariantQueryView invariantQuery,
        SymbolicProgramPointSummary programPointSummary,
        SymbolicCompactSmtDiagnostics smtDiagnostics,
        SymbolicAnalysisTruncationInfo analysisTruncation)
    {
        var reachability = programPointSummary.Reachability;
        var proofs = programPointSummary.ProofOutcomes;
        var invariantConditionCount = invariantQuery.MustFactCount + invariantQuery.UnknownFactCount;
        var checkedCount = reachability.ReachableCount + reachability.UnreachableCount + reachability.UnknownCount;
        var knownCount = reachability.ReachableCount + reachability.UnreachableCount;
        var resolvedProofs = proofs.ProvenTrueCount + proofs.ProvenFalseCount + proofs.UnreachableCount;
        var unresolved = invariantQuery.UnknownFactCount != 0 || reachability.UnknownCount != 0 ||
                         reachability.NotCheckedCount != 0 || proofs.UnknownCount != 0 ||
                         analysisTruncation.IsTruncated;
        return new SymbolicCompactAnalysisSummary(
            SymbolicOrderedJson.Object(
                ("programPointCount", programPointSummary.ProgramPointCount),
                ("invariantConditionCount", invariantConditionCount),
                ("conservativeUnknownCount", invariantQuery.UnknownFactCount),
                ("mustFactCount", invariantQuery.MustFactCount),
                ("maybeFactCount", invariantQuery.MaybeFactCount),
                ("unknownFactCount", invariantQuery.UnknownFactCount),
                ("invariantStatus", invariantQuery.Status),
                ("invariantStatusReason", invariantQuery.StatusReason),
                ("invariantSummary", invariantQuery.Summary),
                ("invariantDiagnosticCount", invariantQuery.DiagnosticCount),
                ("totalPathConditionCount", programPointSummary.TotalPathConditionCount),
                ("maxPathConditionCount", programPointSummary.MaxPathConditionCount),
                ("reachabilityCheckedCount", checkedCount),
                ("reachabilityKnownCount", knownCount),
                ("reachabilityUnknownCount", reachability.UnknownCount),
                ("reachabilityNotCheckedCount", reachability.NotCheckedCount),
                ("proofTotalCount", proofs.TotalCount),
                ("proofResolvedCount", resolvedProofs),
                ("proofUnknownCount", proofs.UnknownCount),
                ("smtConfigured", smtDiagnostics.IsConfigured),
                ("smtEnabled", smtDiagnostics.IsEnabled),
                ("smtExecutedQueryCount", smtDiagnostics.ExecutedQueryCount),
                ("smtCacheEntryCount", smtDiagnostics.CacheEntryCount),
                ("smtQueryTimeoutMs", smtDiagnostics.QueryTimeoutMs),
                ("smtMethodBudgetMs", smtDiagnostics.MethodBudgetMs),
                ("smtMaxPathConditions", smtDiagnostics.MaxPathConditions),
                ("smtMaxExpressionNodes", smtDiagnostics.MaxExpressionNodes),
                ("analysisTruncated", analysisTruncation.IsTruncated),
                ("hasUnresolvedAnalysis", unresolved)),
            unresolved,
            programPointSummary.TotalPathConditionCount,
            programPointSummary.MaxPathConditionCount,
            proofs.TotalCount,
            proofs.UnknownCount,
            invariantQuery.UnknownFactCount,
            reachability.UnknownCount,
            reachability.NotCheckedCount);
    }
}

internal sealed record SymbolicCompactOutputTruncation(
    bool Lines, bool ProgramPoints, bool Facts, bool Conditions, bool Proofs) : ISymbolicRawJsonProjection
{
    public JsonElement Json => SymbolicOrderedJson.Object(
        ("lines", Lines), ("programPoints", ProgramPoints), ("facts", Facts),
        ("conditions", Conditions), ("proofs", Proofs), ("isTruncated", IsTruncated));
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
        var values = truncations.Where(static value => value != null).ToArray();
        return new SymbolicCompactOutputTruncation(
            values.Any(static value => value.Lines), values.Any(static value => value.ProgramPoints),
            values.Any(static value => value.Facts), values.Any(static value => value.Conditions),
            values.Any(static value => value.Proofs));
    }

    internal static SymbolicCompactOutputTruncation Combine(
        params SymbolicCompactOutputTruncation[] truncations) => Combine(truncations.AsEnumerable());
}
