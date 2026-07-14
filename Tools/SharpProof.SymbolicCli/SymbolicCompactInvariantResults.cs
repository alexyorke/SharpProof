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

public sealed class SymbolicCompactInvariantTargetSummary
{
    private SymbolicCompactInvariantTargetSummary(
        string target,
        string status,
        string statusReason,
        string reasonCode,
        string summary,
        int mustFactCount,
        IReadOnlyList<string> mustFacts,
        int maybeFactCount,
        IReadOnlyList<string> maybeFacts,
        int unknownFactCount,
        IReadOnlyList<string> unknownFacts,
        bool mustFactsTruncated,
        bool maybeFactsTruncated,
        bool unknownFactsTruncated)
    {
        Target = target ?? string.Empty;
        Status = status ?? string.Empty;
        StatusReason = statusReason ?? string.Empty;
        ReasonCode = reasonCode ?? string.Empty;
        Summary = summary ?? string.Empty;
        MustFactCount = mustFactCount;
        MustFacts = mustFacts ?? throw new ArgumentNullException(nameof(mustFacts));
        MaybeFactCount = maybeFactCount;
        MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
        UnknownFactCount = unknownFactCount;
        UnknownFacts = unknownFacts ?? throw new ArgumentNullException(nameof(unknownFacts));
        MustFactsTruncated = mustFactsTruncated;
        MaybeFactsTruncated = maybeFactsTruncated;
        UnknownFactsTruncated = unknownFactsTruncated;
    }

    public string Target { get; }

    public string Status { get; }

    public string StatusReason { get; }

    public string ReasonCode { get; }

    public string Summary { get; }

    public int MustFactCount { get; }

    public IReadOnlyList<string> MustFacts { get; }

    public int MaybeFactCount { get; }

    public IReadOnlyList<string> MaybeFacts { get; }

    public int UnknownFactCount { get; }

    public IReadOnlyList<string> UnknownFacts { get; }

    public bool MustFactsTruncated { get; }

    public bool MaybeFactsTruncated { get; }

    public bool UnknownFactsTruncated { get; }

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

public sealed class SymbolicCompactInvariantTargetPathSummary
{
    private SymbolicCompactInvariantTargetPathSummary(
        string target,
        int pathConditionCount,
        int smtConditionCount,
        int conservativeUnknownCount,
        int programPointCount,
        int reachableProgramPointCount,
        int proofTotalCount,
        int proofUnknownCount,
        int proofProvenTrueCount,
        int proofProvenFalseCount,
        int proofUnreachableCount,
        IReadOnlyList<string> conditions,
        bool conditionsTruncated,
        string statusReason,
        string reasonCode,
        string summary)
    {
        Target = target ?? string.Empty;
        PathConditionCount = pathConditionCount;
        SmtConditionCount = smtConditionCount;
        ConservativeUnknownCount = conservativeUnknownCount;
        ProgramPointCount = programPointCount;
        ReachableProgramPointCount = reachableProgramPointCount;
        ProofTotalCount = proofTotalCount;
        ProofUnknownCount = proofUnknownCount;
        ProofProvenTrueCount = proofProvenTrueCount;
        ProofProvenFalseCount = proofProvenFalseCount;
        ProofUnreachableCount = proofUnreachableCount;
        Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        ConditionsTruncated = conditionsTruncated;
        StatusReason = statusReason ?? string.Empty;
        ReasonCode = reasonCode ?? string.Empty;
        Summary = summary ?? string.Empty;
    }

    public string Target { get; }

    public int PathConditionCount { get; }

    public int SmtConditionCount { get; }

    public int ConservativeUnknownCount { get; }

    public int ProgramPointCount { get; }

    public int ReachableProgramPointCount { get; }

    public int ProofTotalCount { get; }

    public int ProofUnknownCount { get; }

    public int ProofProvenTrueCount { get; }

    public int ProofProvenFalseCount { get; }

    public int ProofUnreachableCount { get; }

    public IReadOnlyList<string> Conditions { get; }

    public bool ConditionsTruncated { get; }

    public string StatusReason { get; }

    public string ReasonCode { get; }

    public string Summary { get; }

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

public sealed class SymbolicCompactInvariantQueryDiagnostic
{
    private SymbolicCompactInvariantQueryDiagnostic(
        string code,
        string severity,
        string message,
        int count,
        int evidenceTotalCount,
        IReadOnlyList<string> evidence,
        bool evidenceTruncated)
    {
        Code = code ?? string.Empty;
        Severity = severity ?? string.Empty;
        Message = message ?? string.Empty;
        Count = count;
        EvidenceTotalCount = evidenceTotalCount;
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        EvidenceTruncated = evidenceTruncated;
    }

    public string Code { get; }

    public string Severity { get; }

    public string Message { get; }

    public int Count { get; }

    public int EvidenceTotalCount { get; }

    public IReadOnlyList<string> Evidence { get; }

    public bool EvidenceTruncated { get; }

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

public sealed class SymbolicCompactSmtDiagnostics
{
    private readonly SymbolicSmtDiagnosticsSnapshot _snapshot;

    private SymbolicCompactSmtDiagnostics(SymbolicSmtDiagnosticsSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public bool IsConfigured => _snapshot.IsConfigured;

    public string Mode => _snapshot.Mode.ToString();

    public bool IsEnabled => _snapshot.IsEnabled;

    public int QueryTimeoutMs => _snapshot.QueryTimeoutMs;

    public int MethodBudgetMs => _snapshot.MethodBudgetMs;

    public int MaxPathConditions => _snapshot.MaxPathConditions;

    public int MaxExpressionNodes => _snapshot.MaxExpressionNodes;

    public int ExecutedQueryCount => _snapshot.ExecutedQueryCount;

    public int CacheEntryCount => _snapshot.CacheEntryCount;

    public SmtAnalysisHealth Health => _snapshot.Health;

    public SmtSolverLifecycleOptions Lifecycle => _snapshot.Lifecycle;

    internal static SymbolicCompactSmtDiagnostics FromDiagnostics(SymbolicSmtDiagnostics diagnostics)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));

        return new SymbolicCompactSmtDiagnostics(diagnostics.Snapshot);
    }
}

public sealed class SymbolicCompactAnalysisSummary
{
    private SymbolicCompactAnalysisSummary(
        int programPointCount,
        int invariantConditionCount,
        int conservativeUnknownCount,
        int mustFactCount,
        int maybeFactCount,
        int unknownFactCount,
        string invariantStatus,
        string invariantStatusReason,
        string invariantSummary,
        int invariantDiagnosticCount,
        int totalPathConditionCount,
        int maxPathConditionCount,
        int reachabilityCheckedCount,
        int reachabilityKnownCount,
        int reachabilityUnknownCount,
        int reachabilityNotCheckedCount,
        int proofTotalCount,
        int proofResolvedCount,
        int proofUnknownCount,
        bool smtConfigured,
        bool smtEnabled,
        int smtExecutedQueryCount,
        int smtCacheEntryCount,
        int smtQueryTimeoutMs,
        int smtMethodBudgetMs,
        int smtMaxPathConditions,
        int smtMaxExpressionNodes,
        bool analysisTruncated)
    {
        ProgramPointCount = programPointCount;
        InvariantConditionCount = invariantConditionCount;
        ConservativeUnknownCount = conservativeUnknownCount;
        MustFactCount = mustFactCount;
        MaybeFactCount = maybeFactCount;
        UnknownFactCount = unknownFactCount;
        InvariantStatus = invariantStatus ?? string.Empty;
        InvariantStatusReason = invariantStatusReason ?? string.Empty;
        InvariantSummary = invariantSummary ?? string.Empty;
        InvariantDiagnosticCount = invariantDiagnosticCount;
        TotalPathConditionCount = totalPathConditionCount;
        MaxPathConditionCount = maxPathConditionCount;
        ReachabilityCheckedCount = reachabilityCheckedCount;
        ReachabilityKnownCount = reachabilityKnownCount;
        ReachabilityUnknownCount = reachabilityUnknownCount;
        ReachabilityNotCheckedCount = reachabilityNotCheckedCount;
        ProofTotalCount = proofTotalCount;
        ProofResolvedCount = proofResolvedCount;
        ProofUnknownCount = proofUnknownCount;
        SmtConfigured = smtConfigured;
        SmtEnabled = smtEnabled;
        SmtExecutedQueryCount = smtExecutedQueryCount;
        SmtCacheEntryCount = smtCacheEntryCount;
        SmtQueryTimeoutMs = smtQueryTimeoutMs;
        SmtMethodBudgetMs = smtMethodBudgetMs;
        SmtMaxPathConditions = smtMaxPathConditions;
        SmtMaxExpressionNodes = smtMaxExpressionNodes;
        AnalysisTruncated = analysisTruncated;
    }

    public int ProgramPointCount { get; }

    public int InvariantConditionCount { get; }

    public int ConservativeUnknownCount { get; }

    public int MustFactCount { get; }

    public int MaybeFactCount { get; }

    public int UnknownFactCount { get; }

    public string InvariantStatus { get; }

    public string InvariantStatusReason { get; }

    public string InvariantSummary { get; }

    public int InvariantDiagnosticCount { get; }

    public int TotalPathConditionCount { get; }

    public int MaxPathConditionCount { get; }

    public int ReachabilityCheckedCount { get; }

    public int ReachabilityKnownCount { get; }

    public int ReachabilityUnknownCount { get; }

    public int ReachabilityNotCheckedCount { get; }

    public int ProofTotalCount { get; }

    public int ProofResolvedCount { get; }

    public int ProofUnknownCount { get; }

    public bool SmtConfigured { get; }

    public bool SmtEnabled { get; }

    public int SmtExecutedQueryCount { get; }

    public int SmtCacheEntryCount { get; }

    public int SmtQueryTimeoutMs { get; }

    public int SmtMethodBudgetMs { get; }

    public int SmtMaxPathConditions { get; }

    public int SmtMaxExpressionNodes { get; }

    public bool AnalysisTruncated { get; }

    public bool HasUnresolvedAnalysis =>
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
        if (invariantQuery == null) throw new ArgumentNullException(nameof(invariantQuery));

        if (programPointSummary == null) throw new ArgumentNullException(nameof(programPointSummary));

        if (smtDiagnostics == null) throw new ArgumentNullException(nameof(smtDiagnostics));

        if (analysisTruncation == null) throw new ArgumentNullException(nameof(analysisTruncation));

        var reachability = programPointSummary.Reachability;
        var proofOutcomes = programPointSummary.ProofOutcomes;
        var reachabilityCheckedCount =
            reachability.ReachableCount +
            reachability.UnreachableCount +
            reachability.UnknownCount;
        var reachabilityKnownCount =
            reachability.ReachableCount +
            reachability.UnreachableCount;
        var proofResolvedCount =
            proofOutcomes.ProvenTrueCount +
            proofOutcomes.ProvenFalseCount +
            proofOutcomes.UnreachableCount;

        return new SymbolicCompactAnalysisSummary(
            programPointSummary.ProgramPointCount,
            invariantQuery.MustFactCount + invariantQuery.UnknownFactCount,
            invariantQuery.UnknownFactCount,
            invariantQuery.MustFactCount,
            invariantQuery.MaybeFactCount,
            invariantQuery.UnknownFactCount,
            invariantQuery.Status,
            invariantQuery.StatusReason,
            invariantQuery.Summary,
            invariantQuery.DiagnosticCount,
            programPointSummary.TotalPathConditionCount,
            programPointSummary.MaxPathConditionCount,
            reachabilityCheckedCount,
            reachabilityKnownCount,
            reachability.UnknownCount,
            reachability.NotCheckedCount,
            proofOutcomes.TotalCount,
            proofResolvedCount,
            proofOutcomes.UnknownCount,
            smtDiagnostics.IsConfigured,
            smtDiagnostics.IsEnabled,
            smtDiagnostics.ExecutedQueryCount,
            smtDiagnostics.CacheEntryCount,
            smtDiagnostics.QueryTimeoutMs,
            smtDiagnostics.MethodBudgetMs,
            smtDiagnostics.MaxPathConditions,
            smtDiagnostics.MaxExpressionNodes,
            analysisTruncation.IsTruncated);
    }
}

public sealed class SymbolicCompactOutputTruncation
{
    public SymbolicCompactOutputTruncation(
        bool lines,
        bool programPoints,
        bool facts,
        bool conditions,
        bool proofs)
    {
        Lines = lines;
        ProgramPoints = programPoints;
        Facts = facts;
        Conditions = conditions;
        Proofs = proofs;
    }

    public bool Lines { get; }

    public bool ProgramPoints { get; }

    public bool Facts { get; }

    public bool Conditions { get; }

    public bool Proofs { get; }

    public bool IsTruncated =>
        Lines ||
        ProgramPoints ||
        Facts ||
        Conditions ||
        Proofs;

    internal static SymbolicCompactOutputTruncation FromInvariant(SymbolicCompactInvariantSummary invariant)
    {
        return new SymbolicCompactOutputTruncation(
            false,
            false,
            invariant.RawFactsTruncated,
            invariant.ConditionsTruncated ||
            invariant.TargetsTruncated ||
            (invariant.MergedPathFacts != null && invariant.MergedPathFacts.IsTruncated),
            false);
    }

    internal static SymbolicCompactOutputTruncation Combine(
        IEnumerable<SymbolicCompactOutputTruncation> truncations)
    {
        if (truncations == null) throw new ArgumentNullException(nameof(truncations));

        var lines = false;
        var programPoints = false;
        var facts = false;
        var conditions = false;
        var proofs = false;
        foreach (var truncation in truncations)
        {
            if (truncation == null) continue;

            lines |= truncation.Lines;
            programPoints |= truncation.ProgramPoints;
            facts |= truncation.Facts;
            conditions |= truncation.Conditions;
            proofs |= truncation.Proofs;
        }

        return new SymbolicCompactOutputTruncation(lines, programPoints, facts, conditions, proofs);
    }

    internal static SymbolicCompactOutputTruncation Combine(
        params SymbolicCompactOutputTruncation[] truncations)
    {
        return Combine((IEnumerable<SymbolicCompactOutputTruncation>)truncations);
    }
}
