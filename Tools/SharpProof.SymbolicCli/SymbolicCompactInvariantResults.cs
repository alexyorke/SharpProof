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
    private readonly SymbolicInvariantTargetPathSummary _summary;

    private SymbolicCompactInvariantTargetPathSummary(
        SymbolicInvariantTargetPathSummary summary,
        IReadOnlyList<string> conditions,
        bool conditionsTruncated)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        ConditionsTruncated = conditionsTruncated;
    }

    public string Target => _summary.Target;

    public int PathConditionCount => _summary.PathConditionCount;

    public int SmtConditionCount => _summary.SmtConditionCount;

    public int ConservativeUnknownCount => _summary.ConservativeUnknownCount;

    public int ProgramPointCount => _summary.ProgramPointCount;

    public int ReachableProgramPointCount => _summary.ReachableProgramPointCount;

    public int ProofTotalCount => _summary.ProofTotalCount;

    public int ProofUnknownCount => _summary.ProofUnknownCount;

    public int ProofProvenTrueCount => _summary.ProofProvenTrueCount;

    public int ProofProvenFalseCount => _summary.ProofProvenFalseCount;

    public int ProofUnreachableCount => _summary.ProofUnreachableCount;

    public IReadOnlyList<string> Conditions { get; }

    public bool ConditionsTruncated { get; }

    public string StatusReason => _summary.StatusReason;

    public string ReasonCode => _summary.ReasonCode;

    public string Summary => _summary.Summary;

    internal static SymbolicCompactInvariantTargetPathSummary FromSummary(
        SymbolicInvariantTargetPathSummary summary,
        SymbolicCompactQueryOptions options)
    {
        var conditions = SymbolicCompactProjection.Take(summary.Conditions, options.MaxConditions);
        return new SymbolicCompactInvariantTargetPathSummary(
            summary,
            conditions,
            summary.ConditionsTruncated || summary.Conditions.Count > conditions.Count);
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
    private readonly SymbolicAnalysisTruncationInfo _analysisTruncation;
    private readonly SymbolicCompactInvariantQueryView _invariantQuery;
    private readonly SymbolicProgramPointSummary _programPointSummary;
    private readonly SymbolicCompactSmtDiagnostics _smtDiagnostics;

    private SymbolicCompactAnalysisSummary(
        SymbolicCompactInvariantQueryView invariantQuery,
        SymbolicProgramPointSummary programPointSummary,
        SymbolicCompactSmtDiagnostics smtDiagnostics,
        SymbolicAnalysisTruncationInfo analysisTruncation)
    {
        _invariantQuery = invariantQuery ?? throw new ArgumentNullException(nameof(invariantQuery));
        _programPointSummary = programPointSummary ?? throw new ArgumentNullException(nameof(programPointSummary));
        _smtDiagnostics = smtDiagnostics ?? throw new ArgumentNullException(nameof(smtDiagnostics));
        _analysisTruncation = analysisTruncation ?? throw new ArgumentNullException(nameof(analysisTruncation));
    }

    public int ProgramPointCount => _programPointSummary.ProgramPointCount;

    public int InvariantConditionCount => MustFactCount + UnknownFactCount;

    public int ConservativeUnknownCount => UnknownFactCount;

    public int MustFactCount => _invariantQuery.MustFactCount;

    public int MaybeFactCount => _invariantQuery.MaybeFactCount;

    public int UnknownFactCount => _invariantQuery.UnknownFactCount;

    public string InvariantStatus => _invariantQuery.Status;

    public string InvariantStatusReason => _invariantQuery.StatusReason;

    public string InvariantSummary => _invariantQuery.Summary;

    public int InvariantDiagnosticCount => _invariantQuery.DiagnosticCount;

    public int TotalPathConditionCount => _programPointSummary.TotalPathConditionCount;

    public int MaxPathConditionCount => _programPointSummary.MaxPathConditionCount;

    public int ReachabilityCheckedCount =>
        _programPointSummary.Reachability.ReachableCount +
        _programPointSummary.Reachability.UnreachableCount +
        _programPointSummary.Reachability.UnknownCount;

    public int ReachabilityKnownCount =>
        _programPointSummary.Reachability.ReachableCount +
        _programPointSummary.Reachability.UnreachableCount;

    public int ReachabilityUnknownCount => _programPointSummary.Reachability.UnknownCount;

    public int ReachabilityNotCheckedCount => _programPointSummary.Reachability.NotCheckedCount;

    public int ProofTotalCount => _programPointSummary.ProofOutcomes.TotalCount;

    public int ProofResolvedCount =>
        _programPointSummary.ProofOutcomes.ProvenTrueCount +
        _programPointSummary.ProofOutcomes.ProvenFalseCount +
        _programPointSummary.ProofOutcomes.UnreachableCount;

    public int ProofUnknownCount => _programPointSummary.ProofOutcomes.UnknownCount;

    public bool SmtConfigured => _smtDiagnostics.IsConfigured;

    public bool SmtEnabled => _smtDiagnostics.IsEnabled;

    public int SmtExecutedQueryCount => _smtDiagnostics.ExecutedQueryCount;

    public int SmtCacheEntryCount => _smtDiagnostics.CacheEntryCount;

    public int SmtQueryTimeoutMs => _smtDiagnostics.QueryTimeoutMs;

    public int SmtMethodBudgetMs => _smtDiagnostics.MethodBudgetMs;

    public int SmtMaxPathConditions => _smtDiagnostics.MaxPathConditions;

    public int SmtMaxExpressionNodes => _smtDiagnostics.MaxExpressionNodes;

    public bool AnalysisTruncated => _analysisTruncation.IsTruncated;

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

        return new SymbolicCompactAnalysisSummary(
            invariantQuery,
            programPointSummary,
            smtDiagnostics,
            analysisTruncation);
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
