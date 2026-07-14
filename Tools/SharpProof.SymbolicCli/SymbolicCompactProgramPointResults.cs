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

public sealed class SymbolicCompactLineResult
{
    private SymbolicCompactLineResult(
        string filePath,
        int line,
        int programPointCount,
        SymbolicCompactInvariantSummary observedInvariant,
        SymbolicCompactInvariantSummary conservativeInvariant,
        SymbolicCompactInvariantQueryView invariantQuery,
        SymbolicReachabilitySummary reachability,
        SymbolicProgramPointSummary programPointSummary,
        IReadOnlyList<SymbolicConditionProofSummary> conditionProofs,
        IReadOnlyList<SymbolicCompactProgramPointResult> programPoints,
        SymbolicCompactSmtDiagnostics smtDiagnostics,
        SymbolicCompactOutputTruncation truncation)
    {
        FilePath = filePath ?? string.Empty;
        Line = line;
        ProgramPointCount = programPointCount;
        ObservedInvariant = observedInvariant ?? throw new ArgumentNullException(nameof(observedInvariant));
        ConservativeInvariant = conservativeInvariant ?? throw new ArgumentNullException(nameof(conservativeInvariant));
        InvariantQuery = invariantQuery ?? throw new ArgumentNullException(nameof(invariantQuery));
        MergedInvariantText = ConservativeInvariant.Text;
        Reachability = reachability ?? throw new ArgumentNullException(nameof(reachability));
        ProgramPointSummary = programPointSummary ?? throw new ArgumentNullException(nameof(programPointSummary));
        ProofOutcomes = ProgramPointSummary.ProofOutcomes;
        ConditionProofs = conditionProofs ?? throw new ArgumentNullException(nameof(conditionProofs));
        ProgramPoints = programPoints ?? throw new ArgumentNullException(nameof(programPoints));
        SmtDiagnostics = smtDiagnostics ?? throw new ArgumentNullException(nameof(smtDiagnostics));
        Truncation = truncation ?? throw new ArgumentNullException(nameof(truncation));
    }

    public string FilePath { get; }

    public int Line { get; }

    public int ProgramPointCount { get; }

    public SymbolicCompactInvariantSummary ObservedInvariant { get; }

    public SymbolicCompactInvariantSummary ConservativeInvariant { get; }

    public SymbolicCompactInvariantQueryView InvariantQuery { get; }

    public string MergedInvariantText { get; }

    public SymbolicReachabilitySummary Reachability { get; }

    public SymbolicProgramPointSummary ProgramPointSummary { get; }

    public SymbolicProofOutcomeSummary ProofOutcomes { get; }

    public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs { get; }

    public IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints { get; }

    public SymbolicCompactSmtDiagnostics SmtDiagnostics { get; }

    public SymbolicCompactOutputTruncation Truncation { get; }

    internal static SymbolicCompactLineResult FromResult(
        SymbolicLineQueryResult result,
        SymbolicCompactQueryOptions options,
        int maxProgramPoints)
    {
        var projection = SymbolicCompactScopeProjection.Create(
            result.ObservedInvariant,
            result.Facts,
            result.MergedInvariant,
            result.MergedPathFacts,
            result.InvariantQuery,
            result.ProgramPointSummary.Reachability,
            result.ProgramPointSummary,
            SymbolicConditionProofSummary.FromProgramPoints(result.ProgramPoints),
            result.ProgramPoints,
            result.SmtDiagnostics,
            options,
            maxProgramPoints);

        return new SymbolicCompactLineResult(
            result.FilePath,
            result.Line,
            result.ProgramPoints.Count,
            projection.ObservedInvariant,
            projection.ConservativeInvariant,
            projection.InvariantQuery,
            projection.Reachability,
            projection.ProgramPointSummary,
            projection.ConditionProofs,
            projection.ProgramPoints,
            projection.SmtDiagnostics,
            projection.Truncation);
    }
}

public sealed class SymbolicCompactProgramPointResult
{
    private SymbolicCompactProgramPointResult(
        string filePath,
        int line,
        int column,
        int position,
        int nodeSpanStart,
        int nodeSpanEnd,
        int nodeSpanLength,
        int nodeStartLine,
        int nodeStartColumn,
        int nodeEndLine,
        int nodeEndColumn,
        string nodeKind,
        string? methodName,
        string programPointKind,
        int factCount,
        IReadOnlyList<string> facts,
        IReadOnlyList<SymbolicFactInfo> symbolicFacts,
        SymbolicCompactInvariantSummary observedInvariant,
        SymbolicCompactInvariantSummary conservativeInvariant,
        SymbolicCompactInvariantQueryView invariantQuery,
        int pathConditionCount,
        IReadOnlyList<SymbolicInvariantCondition> pathConditions,
        string reachability,
        string reachabilityReason,
        IReadOnlyList<SymbolicConditionProofResult> conditionProofs,
        SymbolicProofOutcomeSummary proofOutcomes,
        SymbolicCompactSmtDiagnostics smtDiagnostics,
        SymbolicCompactOutputTruncation truncation,
        int? requestedLine = null,
        int? requestedColumn = null,
        int? requestedPosition = null,
        int? requestedPositionDistance = null,
        bool? containsRequestedPosition = null)
    {
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
        Position = position;
        RequestedLine = requestedLine;
        RequestedColumn = requestedColumn;
        RequestedPosition = requestedPosition;
        RequestedPositionDistance = requestedPositionDistance;
        ContainsRequestedPosition = containsRequestedPosition;
        NodeSpanStart = nodeSpanStart;
        NodeSpanEnd = nodeSpanEnd;
        NodeSpanLength = nodeSpanLength;
        NodeStartLine = nodeStartLine;
        NodeStartColumn = nodeStartColumn;
        NodeEndLine = nodeEndLine;
        NodeEndColumn = nodeEndColumn;
        NodeKind = nodeKind ?? string.Empty;
        MethodName = string.IsNullOrWhiteSpace(methodName) ? null : methodName;
        ProgramPointKind = SymbolicProgramPointKinds.Normalize(programPointKind, nodeKind);
        FactCount = factCount;
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        SymbolicFacts = symbolicFacts ?? throw new ArgumentNullException(nameof(symbolicFacts));
        ObservedInvariant = observedInvariant ?? throw new ArgumentNullException(nameof(observedInvariant));
        ConservativeInvariant = conservativeInvariant ?? throw new ArgumentNullException(nameof(conservativeInvariant));
        InvariantQuery = invariantQuery ?? throw new ArgumentNullException(nameof(invariantQuery));
        MergedInvariantText = ConservativeInvariant.Text;
        PathConditionCount = pathConditionCount;
        InvariantConditions = pathConditions ?? throw new ArgumentNullException(nameof(pathConditions));
        Reachability = reachability ?? string.Empty;
        ReachabilityReason = reachabilityReason ?? string.Empty;
        ConditionProofs = conditionProofs ?? throw new ArgumentNullException(nameof(conditionProofs));
        ProofOutcomes = proofOutcomes ?? throw new ArgumentNullException(nameof(proofOutcomes));
        SmtDiagnostics = smtDiagnostics ?? throw new ArgumentNullException(nameof(smtDiagnostics));
        Truncation = truncation ?? throw new ArgumentNullException(nameof(truncation));
    }

    public string FilePath { get; }

    public int Line { get; }

    public int Column { get; }

    public int Position { get; }

    public int? RequestedLine { get; }

    public int? RequestedColumn { get; }

    public int? RequestedPosition { get; }

    public int? RequestedPositionDistance { get; }

    public bool? ContainsRequestedPosition { get; }

    public int NodeSpanStart { get; }

    public int NodeSpanEnd { get; }

    public int NodeSpanLength { get; }

    public int NodeStartLine { get; }

    public int NodeStartColumn { get; }

    public int NodeEndLine { get; }

    public int NodeEndColumn { get; }

    public string NodeKind { get; }

    public string? MethodName { get; }

    public string ProgramPointKind { get; }

    public int FactCount { get; }

    public IReadOnlyList<string> Facts { get; }

    public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; }

    public SymbolicCompactInvariantSummary ObservedInvariant { get; }

    public SymbolicCompactInvariantSummary ConservativeInvariant { get; }

    public SymbolicCompactInvariantQueryView InvariantQuery { get; }

    public string MergedInvariantText { get; }

    public int PathConditionCount { get; }

    public IReadOnlyList<SymbolicInvariantCondition> InvariantConditions { get; }

    internal IReadOnlyList<SymbolicInvariantCondition> PathConditions => InvariantConditions;

    public string Reachability { get; }

    public string ReachabilityReason { get; }

    public IReadOnlyList<SymbolicConditionProofResult> ConditionProofs { get; }

    public SymbolicProofOutcomeSummary ProofOutcomes { get; }

    public SymbolicCompactSmtDiagnostics SmtDiagnostics { get; }

    public SymbolicCompactOutputTruncation Truncation { get; }

    internal static SymbolicCompactProgramPointResult FromResult(
        SymbolicProgramPointResult result,
        SymbolicCompactQueryOptions options)
    {
        var observedInvariant = SymbolicCompactInvariantSummary.FromObservedFacts(
            SymbolicInvariantResult.FromFacts(result.Facts),
            result.Facts,
            options);
        var conservativeInvariant = SymbolicCompactInvariantSummary.FromInvariant(
            result.Invariant,
            null,
            options);
        var focusedPathConditions = SymbolicInvariantTargetFilter.ApplyToConditions(
            result.Invariant.Conditions,
            options.InvariantTargets);
        var focusedFacts = options.HasInvariantTargetFilter
            ? focusedPathConditions
                .Select(static condition => condition.Text)
                .Where(static fact => !string.IsNullOrWhiteSpace(fact))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : result.Facts;
        var focusedConditionProofs = SymbolicInvariantTargetFilter.ApplyToProofResults(
            result.ConditionProofs,
            options.InvariantTargets);
        var facts = SymbolicCompactProjection.Take(focusedFacts, options.MaxFacts);
        var symbolicFacts = SymbolicCompactProjection.Take(result.SymbolicFacts, options.MaxFacts);
        var pathConditions = SymbolicCompactProjection.Take(focusedPathConditions, options.MaxConditions);
        var conditionProofs = SymbolicCompactProjection.Take(focusedConditionProofs, options.MaxProofs);
        var truncation = SymbolicCompactOutputTruncation.Combine(
            new SymbolicCompactOutputTruncation(
                false,
                false,
                focusedFacts.Count > facts.Count ||
                result.SymbolicFacts.Count > symbolicFacts.Count,
                focusedPathConditions.Count > pathConditions.Count,
                focusedConditionProofs.Count > conditionProofs.Count),
            SymbolicCompactOutputTruncation.FromInvariant(observedInvariant),
            SymbolicCompactOutputTruncation.FromInvariant(conservativeInvariant));

        return new SymbolicCompactProgramPointResult(
            result.FilePath,
            result.Line,
            result.Column,
            result.Position,
            result.NodeSpanStart,
            result.NodeSpanEnd,
            result.NodeSpanLength,
            result.NodeStartLine,
            result.NodeStartColumn,
            result.NodeEndLine,
            result.NodeEndColumn,
            result.NodeKind,
            result.MethodName,
            result.ProgramPointKind,
            focusedFacts.Count,
            facts,
            symbolicFacts,
            observedInvariant,
            conservativeInvariant,
            SymbolicCompactInvariantQueryView.FromQueryView(result.InvariantQuery, options),
            focusedPathConditions.Count,
            pathConditions,
            result.Reachability.ToString(),
            result.ReachabilityReason,
            conditionProofs,
            result.ProofOutcomes,
            SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics),
            truncation,
            result.RequestedLine,
            result.RequestedColumn,
            result.RequestedPosition,
            result.RequestedPositionDistance,
            result.ContainsRequestedPosition);
    }
}

public sealed class SymbolicCompactInvariantSummary
{
    private SymbolicCompactInvariantSummary(
        string mergeKind,
        string text,
        int conditionCount,
        IReadOnlyList<string> conditions,
        int targetCount,
        IReadOnlyList<string> targets,
        int rawFactCount,
        IReadOnlyList<string> rawFacts,
        int conservativeUnknownCount,
        SymbolicCompactMergedPathFacts? mergedPathFacts,
        bool conditionsTruncated,
        bool targetsTruncated,
        bool rawFactsTruncated)
    {
        MergeKind = mergeKind ?? string.Empty;
        Text = text ?? string.Empty;
        ConditionCount = conditionCount;
        Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        TargetCount = targetCount;
        Targets = targets ?? throw new ArgumentNullException(nameof(targets));
        RawFactCount = rawFactCount;
        RawFacts = rawFacts ?? throw new ArgumentNullException(nameof(rawFacts));
        ConservativeUnknownCount = conservativeUnknownCount;
        HasConservativeUnknowns = conservativeUnknownCount != 0;
        MergedPathFacts = mergedPathFacts;
        ConditionsTruncated = conditionsTruncated;
        TargetsTruncated = targetsTruncated;
        RawFactsTruncated = rawFactsTruncated;
    }

    public string MergeKind { get; }

    public string Text { get; }

    public int ConditionCount { get; }

    public IReadOnlyList<string> Conditions { get; }

    public int TargetCount { get; }

    public IReadOnlyList<string> Targets { get; }

    public int RawFactCount { get; }

    public IReadOnlyList<string> RawFacts { get; }

    public int ConservativeUnknownCount { get; }

    public bool HasConservativeUnknowns { get; }

    public SymbolicCompactMergedPathFacts? MergedPathFacts { get; }

    public bool ConditionsTruncated { get; }

    public bool TargetsTruncated { get; }

    public bool RawFactsTruncated { get; }

    internal static SymbolicCompactInvariantSummary FromObservedFacts(
        SymbolicInvariantResult invariant,
        IReadOnlyList<string> rawFacts,
        SymbolicCompactQueryOptions options)
    {
        return Create(invariant, rawFacts, null, options);
    }

    internal static SymbolicCompactInvariantSummary FromInvariant(
        SymbolicInvariantResult invariant,
        SymbolicMergedPathFacts? mergedPathFacts,
        SymbolicCompactQueryOptions options)
    {
        return Create(invariant, Array.Empty<string>(), mergedPathFacts, options);
    }

    private static SymbolicCompactInvariantSummary Create(
        SymbolicInvariantResult invariant,
        IReadOnlyList<string> rawFacts,
        SymbolicMergedPathFacts? mergedPathFacts,
        SymbolicCompactQueryOptions options)
    {
        var conditions = invariant.Conditions
            .Select(static condition => condition.Text)
            .ToArray();
        var targets = GetDistinctTargets(invariant);
        var conditionProjection = SymbolicCompactProjection.Project(conditions, options.MaxConditions);
        var targetProjection = SymbolicCompactProjection.Project(targets, options.MaxConditions);
        var rawFactProjection = SymbolicCompactProjection.Project(rawFacts, options.MaxFacts);
        return new SymbolicCompactInvariantSummary(
            invariant.MergeKind.ToString(),
            invariant.MergedInvariantText,
            invariant.ConditionCount,
            conditionProjection.Items,
            targetProjection.TotalCount,
            targetProjection.Items,
            rawFactProjection.TotalCount,
            rawFactProjection.Items,
            invariant.ConservativeUnknownCount,
            mergedPathFacts == null
                ? null
                : SymbolicCompactMergedPathFacts.FromMergedPathFacts(mergedPathFacts, options),
            conditionProjection.IsTruncated,
            targetProjection.IsTruncated,
            rawFactProjection.IsTruncated);
    }

    private static string[] GetDistinctTargets(SymbolicInvariantResult invariant)
    {
        return invariant.Conditions
            .Select(static condition => condition.Target)
            .Where(static target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed class SymbolicCompactMergedPathFacts
{
    private SymbolicCompactMergedPathFacts(
        int alwaysFactCount,
        IReadOnlyList<string> alwaysFacts,
        int maybeFactCount,
        IReadOnlyList<string> maybeFacts,
        int conservativeUnknownCount,
        IReadOnlyList<string> conservativeUnknowns,
        IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> conservativeUnknownDiagnostics,
        int candidateProgramPointCount,
        int unreachableProgramPointCount,
        bool isUnreachable,
        bool alwaysFactsTruncated,
        bool maybeFactsTruncated,
        bool conservativeUnknownsTruncated,
        bool conservativeUnknownDiagnosticsTruncated)
    {
        AlwaysFactCount = alwaysFactCount;
        AlwaysFacts = alwaysFacts ?? throw new ArgumentNullException(nameof(alwaysFacts));
        MaybeFactCount = maybeFactCount;
        MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
        ConservativeUnknownCount = conservativeUnknownCount;
        ConservativeUnknowns = conservativeUnknowns ?? throw new ArgumentNullException(nameof(conservativeUnknowns));
        ConservativeUnknownDiagnostics = conservativeUnknownDiagnostics ??
                                         throw new ArgumentNullException(nameof(conservativeUnknownDiagnostics));
        CandidateProgramPointCount = candidateProgramPointCount;
        UnreachableProgramPointCount = unreachableProgramPointCount;
        IsUnreachable = isUnreachable;
        AlwaysFactsTruncated = alwaysFactsTruncated;
        MaybeFactsTruncated = maybeFactsTruncated;
        ConservativeUnknownsTruncated = conservativeUnknownsTruncated;
        ConservativeUnknownDiagnosticsTruncated = conservativeUnknownDiagnosticsTruncated;
    }

    public int AlwaysFactCount { get; }

    public IReadOnlyList<string> AlwaysFacts { get; }

    public int MaybeFactCount { get; }

    public IReadOnlyList<string> MaybeFacts { get; }

    public int ConservativeUnknownCount { get; }

    public IReadOnlyList<string> ConservativeUnknowns { get; }

    public IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> ConservativeUnknownDiagnostics { get; }

    public int CandidateProgramPointCount { get; }

    public int UnreachableProgramPointCount { get; }

    public bool IsUnreachable { get; }

    public bool AlwaysFactsTruncated { get; }

    public bool MaybeFactsTruncated { get; }

    public bool ConservativeUnknownsTruncated { get; }

    public bool ConservativeUnknownDiagnosticsTruncated { get; }

    internal bool IsTruncated =>
        AlwaysFactsTruncated ||
        MaybeFactsTruncated ||
        ConservativeUnknownsTruncated ||
        ConservativeUnknownDiagnosticsTruncated ||
        ConservativeUnknownDiagnostics.Any(static diagnostic => diagnostic.MaybeFactsTruncated);

    internal static SymbolicCompactMergedPathFacts FromMergedPathFacts(
        SymbolicMergedPathFacts facts,
        SymbolicCompactQueryOptions options)
    {
        var conservativeUnknownDiagnostics = SymbolicCompactProjection
            .Take(facts.ConservativeUnknownDiagnostics, options.MaxConditions)
            .Select(diagnostic => SymbolicCompactConservativeUnknownDiagnostic.FromDiagnostic(diagnostic, options))
            .ToArray();
        return new SymbolicCompactMergedPathFacts(
            facts.AlwaysFacts.Count,
            SymbolicCompactProjection.Take(facts.AlwaysFacts, options.MaxConditions),
            facts.MaybeFacts.Count,
            SymbolicCompactProjection.Take(facts.MaybeFacts, options.MaxConditions),
            facts.ConservativeUnknowns.Count,
            SymbolicCompactProjection.Take(facts.ConservativeUnknowns, options.MaxConditions),
            conservativeUnknownDiagnostics,
            facts.CandidateProgramPointCount,
            facts.UnreachableProgramPointCount,
            facts.IsUnreachable,
            facts.AlwaysFacts.Count > options.MaxConditions,
            facts.MaybeFacts.Count > options.MaxConditions,
            facts.ConservativeUnknowns.Count > options.MaxConditions,
            facts.ConservativeUnknownDiagnostics.Count > options.MaxConditions);
    }
}

public sealed class SymbolicCompactConservativeUnknownDiagnostic
{
    private SymbolicCompactConservativeUnknownDiagnostic(
        string target,
        string unknownText,
        string reason,
        int maybeFactCount,
        IReadOnlyList<string> maybeFacts,
        int candidateProgramPointCount,
        int unreachableProgramPointCount,
        bool maybeFactsTruncated)
    {
        Target = target ?? string.Empty;
        UnknownText = unknownText ?? string.Empty;
        Reason = reason ?? string.Empty;
        MaybeFactCount = maybeFactCount;
        MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
        CandidateProgramPointCount = candidateProgramPointCount;
        UnreachableProgramPointCount = unreachableProgramPointCount;
        MaybeFactsTruncated = maybeFactsTruncated;
    }

    public string Target { get; }

    public string UnknownText { get; }

    public string Reason { get; }

    public int MaybeFactCount { get; }

    public IReadOnlyList<string> MaybeFacts { get; }

    public int CandidateProgramPointCount { get; }

    public int UnreachableProgramPointCount { get; }

    public bool MaybeFactsTruncated { get; }

    internal static SymbolicCompactConservativeUnknownDiagnostic FromDiagnostic(
        SymbolicConservativeUnknownDiagnostic diagnostic,
        SymbolicCompactQueryOptions options)
    {
        return new SymbolicCompactConservativeUnknownDiagnostic(
            diagnostic.Target,
            diagnostic.UnknownText,
            diagnostic.Reason,
            diagnostic.MaybeFacts.Count,
            SymbolicCompactProjection.Take(diagnostic.MaybeFacts, options.MaxConditions),
            diagnostic.CandidateProgramPointCount,
            diagnostic.UnreachableProgramPointCount,
            diagnostic.MaybeFacts.Count > options.MaxConditions);
    }
}

public sealed class SymbolicCompactInvariantQueryView
{
    private SymbolicCompactInvariantQueryView(
        string text,
        string mergeKind,
        int mustFactCount,
        IReadOnlyList<string> mustFacts,
        int maybeFactCount,
        IReadOnlyList<string> maybeFacts,
        int unknownFactCount,
        IReadOnlyList<string> unknownFacts,
        IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> unknownDiagnostics,
        int targetSummaryCount,
        IReadOnlyList<SymbolicCompactInvariantTargetSummary> targetSummaries,
        int targetPathSummaryCount,
        IReadOnlyList<SymbolicCompactInvariantTargetPathSummary> targetPathSummaries,
        IReadOnlyList<string> targetFilters,
        int targetFilterCount,
        bool hasTargetFilter,
        bool targetFilterMatched,
        int matchedTargetFilterCount,
        IReadOnlyList<string> matchedTargetFilters,
        int unmatchedTargetFilterCount,
        IReadOnlyList<string> unmatchedTargetFilters,
        int unfilteredTargetSummaryCount,
        int unfilteredTargetPathSummaryCount,
        int diagnosticCount,
        IReadOnlyList<SymbolicCompactInvariantQueryDiagnostic> diagnostics,
        int candidateProgramPointCount,
        int unreachableProgramPointCount,
        bool isUnreachable,
        string status,
        string statusReason,
        string summary,
        bool hasMaybeFacts,
        bool hasUnknowns,
        bool hasUnresolvedAnalysis,
        bool mustFactsTruncated,
        bool maybeFactsTruncated,
        bool unknownFactsTruncated,
        bool unknownDiagnosticsTruncated,
        bool targetSummariesTruncated,
        bool targetPathSummariesTruncated,
        bool matchedTargetFiltersTruncated,
        bool unmatchedTargetFiltersTruncated,
        bool diagnosticsTruncated)
    {
        Text = text ?? string.Empty;
        MergeKind = mergeKind ?? string.Empty;
        MustFactCount = mustFactCount;
        MustFacts = mustFacts ?? throw new ArgumentNullException(nameof(mustFacts));
        MaybeFactCount = maybeFactCount;
        MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
        UnknownFactCount = unknownFactCount;
        UnknownFacts = unknownFacts ?? throw new ArgumentNullException(nameof(unknownFacts));
        UnknownDiagnostics = unknownDiagnostics ?? throw new ArgumentNullException(nameof(unknownDiagnostics));
        TargetSummaryCount = targetSummaryCount;
        TargetSummaries = targetSummaries ?? throw new ArgumentNullException(nameof(targetSummaries));
        TargetPathSummaryCount = targetPathSummaryCount;
        TargetPathSummaries = targetPathSummaries ?? throw new ArgumentNullException(nameof(targetPathSummaries));
        TargetFilters = targetFilters ?? throw new ArgumentNullException(nameof(targetFilters));
        TargetFilterCount = targetFilterCount;
        HasTargetFilter = hasTargetFilter;
        TargetFilterMatched = targetFilterMatched;
        MatchedTargetFilterCount = matchedTargetFilterCount;
        MatchedTargetFilters = matchedTargetFilters ?? throw new ArgumentNullException(nameof(matchedTargetFilters));
        UnmatchedTargetFilterCount = unmatchedTargetFilterCount;
        UnmatchedTargetFilters =
            unmatchedTargetFilters ?? throw new ArgumentNullException(nameof(unmatchedTargetFilters));
        UnfilteredTargetSummaryCount = unfilteredTargetSummaryCount;
        UnfilteredTargetPathSummaryCount = unfilteredTargetPathSummaryCount;
        DiagnosticCount = diagnosticCount;
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        CandidateProgramPointCount = candidateProgramPointCount;
        UnreachableProgramPointCount = unreachableProgramPointCount;
        IsUnreachable = isUnreachable;
        Status = status ?? string.Empty;
        StatusReason = statusReason ?? string.Empty;
        Summary = summary ?? string.Empty;
        HasMaybeFacts = hasMaybeFacts;
        HasUnknowns = hasUnknowns;
        HasUnresolvedAnalysis = hasUnresolvedAnalysis;
        MustFactsTruncated = mustFactsTruncated;
        MaybeFactsTruncated = maybeFactsTruncated;
        UnknownFactsTruncated = unknownFactsTruncated;
        UnknownDiagnosticsTruncated = unknownDiagnosticsTruncated;
        TargetSummariesTruncated = targetSummariesTruncated;
        TargetPathSummariesTruncated = targetPathSummariesTruncated;
        MatchedTargetFiltersTruncated = matchedTargetFiltersTruncated;
        UnmatchedTargetFiltersTruncated = unmatchedTargetFiltersTruncated;
        DiagnosticsTruncated = diagnosticsTruncated;
    }

    public string Text { get; }

    public string MergeKind { get; }

    public int MustFactCount { get; }

    public IReadOnlyList<string> MustFacts { get; }

    public int MaybeFactCount { get; }

    public IReadOnlyList<string> MaybeFacts { get; }

    public int UnknownFactCount { get; }

    public IReadOnlyList<string> UnknownFacts { get; }

    public IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> UnknownDiagnostics { get; }

    public int TargetSummaryCount { get; }

    public IReadOnlyList<SymbolicCompactInvariantTargetSummary> TargetSummaries { get; }

    public int TargetPathSummaryCount { get; }

    public IReadOnlyList<SymbolicCompactInvariantTargetPathSummary> TargetPathSummaries { get; }

    public IReadOnlyList<string> TargetFilters { get; }

    public int TargetFilterCount { get; }

    public bool HasTargetFilter { get; }

    public bool TargetFilterMatched { get; }

    public int MatchedTargetFilterCount { get; }

    public IReadOnlyList<string> MatchedTargetFilters { get; }

    public int UnmatchedTargetFilterCount { get; }

    public IReadOnlyList<string> UnmatchedTargetFilters { get; }

    public int UnfilteredTargetSummaryCount { get; }

    public int UnfilteredTargetPathSummaryCount { get; }

    public int DiagnosticCount { get; }

    public IReadOnlyList<SymbolicCompactInvariantQueryDiagnostic> Diagnostics { get; }

    public int CandidateProgramPointCount { get; }

    public int UnreachableProgramPointCount { get; }

    public bool IsUnreachable { get; }

    public string Status { get; }

    public string StatusReason { get; }

    public string Summary { get; }

    public bool HasMaybeFacts { get; }

    public bool HasUnknowns { get; }

    public bool HasUnresolvedAnalysis { get; }

    public bool MustFactsTruncated { get; }

    public bool MaybeFactsTruncated { get; }

    public bool UnknownFactsTruncated { get; }

    public bool UnknownDiagnosticsTruncated { get; }

    public bool TargetSummariesTruncated { get; }

    public bool TargetPathSummariesTruncated { get; }

    public bool MatchedTargetFiltersTruncated { get; }

    public bool UnmatchedTargetFiltersTruncated { get; }

    public bool DiagnosticsTruncated { get; }

    public bool IsTruncated =>
        MustFactsTruncated ||
        MaybeFactsTruncated ||
        UnknownFactsTruncated ||
        UnknownDiagnosticsTruncated ||
        TargetSummariesTruncated ||
        TargetPathSummariesTruncated ||
        MatchedTargetFiltersTruncated ||
        UnmatchedTargetFiltersTruncated ||
        DiagnosticsTruncated ||
        Diagnostics.Any(static diagnostic => diagnostic.EvidenceTruncated) ||
        UnknownDiagnostics.Any(static diagnostic => diagnostic.MaybeFactsTruncated) ||
        TargetSummaries.Any(static target => target.IsTruncated) ||
        TargetPathSummaries.Any(static target => target.ConditionsTruncated);

    internal static SymbolicCompactInvariantQueryView FromQueryView(
        SymbolicInvariantQueryView query,
        SymbolicCompactQueryOptions options)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        var filteredTargetSummaries = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.TargetSummaries,
            options.InvariantTargets,
            static summary => summary.Target);
        var focusedMustFacts = SymbolicInvariantTargetFilter.SelectFacts(
            query.MustFacts,
            filteredTargetSummaries,
            options.InvariantTargets,
            static summary => summary.MustFacts);
        var focusedMaybeFacts = SymbolicInvariantTargetFilter.SelectFacts(
            query.MaybeFacts,
            filteredTargetSummaries,
            options.InvariantTargets,
            static summary => summary.MaybeFacts);
        var focusedUnknownFacts = SymbolicInvariantTargetFilter.SelectFacts(
            query.UnknownFacts,
            filteredTargetSummaries,
            options.InvariantTargets,
            static summary => summary.UnknownFacts);
        var focusedMergedFacts = options.HasInvariantTargetFilter
            ? focusedMustFacts.Concat(focusedUnknownFacts).ToArray()
            : Array.Empty<string>();
        var focusedText = options.HasInvariantTargetFilter
            ? SymbolicInvariantService.FormatMergedInvariantFacts(focusedMergedFacts)
            : query.Text;
        var filteredUnknownDiagnostics = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.UnknownDiagnostics,
            options.InvariantTargets,
            static diagnostic => diagnostic.Target);
        var unknownDiagnostics = SymbolicCompactProjection
            .Take(filteredUnknownDiagnostics, options.MaxConditions)
            .Select(diagnostic => SymbolicCompactConservativeUnknownDiagnostic.FromDiagnostic(diagnostic, options))
            .ToArray();
        var targetSummaries = SymbolicCompactProjection
            .Take(filteredTargetSummaries, options.MaxConditions)
            .Select(target => SymbolicCompactInvariantTargetSummary.FromSummary(target, options))
            .ToArray();
        var filteredTargetPathSummaries = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.TargetPathSummaries,
            options.InvariantTargets,
            static summary => summary.Target);
        var targetPathSummaries = SymbolicCompactProjection
            .Take(filteredTargetPathSummaries, options.MaxConditions)
            .Select(target => SymbolicCompactInvariantTargetPathSummary.FromSummary(target, options))
            .ToArray();
        var diagnostics = SymbolicCompactProjection
            .Take(query.Diagnostics, options.MaxConditions)
            .Select(diagnostic => SymbolicCompactInvariantQueryDiagnostic.FromDiagnostic(diagnostic, options))
            .ToArray();
        var matchedTargetFilters = SymbolicInvariantTargetFilter.GetMatchedTargetFilters(
            query,
            options.InvariantTargets);
        var unmatchedTargetFilters =
            SymbolicInvariantTargetFilter.GetUnmatchedTargetFilters(options.InvariantTargets, matchedTargetFilters);
        var visibleMatchedTargetFilters = SymbolicCompactProjection.Take(matchedTargetFilters, options.MaxConditions);
        var visibleUnmatchedTargetFilters =
            SymbolicCompactProjection.Take(unmatchedTargetFilters, options.MaxConditions);
        var targetFilterMatched = !options.HasInvariantTargetFilter || matchedTargetFilters.Count != 0;
        return new SymbolicCompactInvariantQueryView(
            focusedText,
            query.MergeKind.ToString(),
            focusedMustFacts.Count,
            SymbolicCompactProjection.Take(focusedMustFacts, options.MaxConditions),
            focusedMaybeFacts.Count,
            SymbolicCompactProjection.Take(focusedMaybeFacts, options.MaxConditions),
            focusedUnknownFacts.Count,
            SymbolicCompactProjection.Take(focusedUnknownFacts, options.MaxConditions),
            unknownDiagnostics,
            filteredTargetSummaries.Count,
            targetSummaries,
            filteredTargetPathSummaries.Count,
            targetPathSummaries,
            options.InvariantTargets,
            options.InvariantTargets.Count,
            options.HasInvariantTargetFilter,
            targetFilterMatched,
            matchedTargetFilters.Count,
            visibleMatchedTargetFilters,
            unmatchedTargetFilters.Count,
            visibleUnmatchedTargetFilters,
            query.TargetSummaryCount,
            query.TargetPathSummaryCount,
            query.DiagnosticCount,
            diagnostics,
            query.CandidateProgramPointCount,
            query.UnreachableProgramPointCount,
            query.IsUnreachable,
            query.Status.ToString(),
            query.StatusReason,
            query.Summary,
            focusedMaybeFacts.Count != 0,
            focusedUnknownFacts.Count != 0,
            query.HasUnresolvedAnalysis,
            focusedMustFacts.Count > options.MaxConditions,
            focusedMaybeFacts.Count > options.MaxConditions,
            focusedUnknownFacts.Count > options.MaxConditions,
            filteredUnknownDiagnostics.Count > options.MaxConditions,
            filteredTargetSummaries.Count > targetSummaries.Length,
            filteredTargetPathSummaries.Count > targetPathSummaries.Length,
            matchedTargetFilters.Count > visibleMatchedTargetFilters.Count,
            unmatchedTargetFilters.Count > visibleUnmatchedTargetFilters.Count,
            query.Diagnostics.Count > options.MaxConditions);
    }
}
