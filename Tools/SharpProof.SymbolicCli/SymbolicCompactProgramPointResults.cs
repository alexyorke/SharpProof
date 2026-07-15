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
    private readonly SymbolicCompactScopeProjection _projection;
    private readonly SymbolicQueryResult _result;

    private SymbolicCompactLineResult(
        SymbolicQueryResult result,
        SymbolicCompactScopeProjection projection)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
    }

    public string FilePath => _result.FilePath;

    public int Line => _result.Line ?? 0;

    public int ProgramPointCount => _result.ProgramPoints.Count;

    public SymbolicCompactInvariantSummary ObservedInvariant => _projection.ObservedInvariant;

    public SymbolicCompactInvariantSummary ConservativeInvariant => _projection.ConservativeInvariant;

    public SymbolicCompactInvariantQueryView InvariantQuery => _projection.InvariantQuery;

    public string MergedInvariantText => ConservativeInvariant.Text;

    public SymbolicReachabilitySummary Reachability => _projection.Reachability;

    public SymbolicProgramPointSummary ProgramPointSummary => _projection.ProgramPointSummary;

    public SymbolicProofOutcomeSummary ProofOutcomes => ProgramPointSummary.ProofOutcomes;

    public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs => _projection.ConditionProofs;

    public IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints => _projection.ProgramPoints;

    public SymbolicCompactSmtDiagnostics SmtDiagnostics => _projection.SmtDiagnostics;

    public SymbolicCompactOutputTruncation Truncation => _projection.Truncation;

    internal SymbolicCompactScopeProjection Projection => _projection;

    internal static SymbolicCompactLineResult FromResult(
        SymbolicQueryResult result,
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

        return new SymbolicCompactLineResult(result, projection);
    }
}

public sealed class SymbolicCompactProgramPointResult
{
    private readonly SymbolicProgramPointResult _result;

    private SymbolicCompactProgramPointResult(
        SymbolicProgramPointResult result,
        int factCount,
        IReadOnlyList<string> facts,
        IReadOnlyList<SymbolicFactInfo> symbolicFacts,
        SymbolicCompactInvariantSummary observedInvariant,
        SymbolicCompactInvariantSummary conservativeInvariant,
        SymbolicCompactInvariantQueryView invariantQuery,
        int pathConditionCount,
        IReadOnlyList<SymbolicInvariantCondition> pathConditions,
        IReadOnlyList<SymbolicConditionProofResult> conditionProofs,
        SymbolicCompactSmtDiagnostics smtDiagnostics,
        SymbolicCompactOutputTruncation truncation)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        FactCount = factCount;
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        SymbolicFacts = symbolicFacts ?? throw new ArgumentNullException(nameof(symbolicFacts));
        ObservedInvariant = observedInvariant ?? throw new ArgumentNullException(nameof(observedInvariant));
        ConservativeInvariant = conservativeInvariant ?? throw new ArgumentNullException(nameof(conservativeInvariant));
        InvariantQuery = invariantQuery ?? throw new ArgumentNullException(nameof(invariantQuery));
        PathConditionCount = pathConditionCount;
        InvariantConditions = pathConditions ?? throw new ArgumentNullException(nameof(pathConditions));
        ConditionProofs = conditionProofs ?? throw new ArgumentNullException(nameof(conditionProofs));
        SmtDiagnostics = smtDiagnostics ?? throw new ArgumentNullException(nameof(smtDiagnostics));
        Truncation = truncation ?? throw new ArgumentNullException(nameof(truncation));
    }

    public string FilePath => _result.FilePath;

    public int Line => _result.Line;

    public int Column => _result.Column;

    public int Position => _result.Position;

    public int? RequestedLine => _result.RequestedLine;

    public int? RequestedColumn => _result.RequestedColumn;

    public int? RequestedPosition => _result.RequestedPosition;

    public int? RequestedPositionDistance => _result.RequestedPositionDistance;

    public bool? ContainsRequestedPosition => _result.ContainsRequestedPosition;

    public int NodeSpanStart => _result.NodeSpanStart;

    public int NodeSpanEnd => _result.NodeSpanEnd;

    public int NodeSpanLength => _result.NodeSpanLength;

    public int NodeStartLine => _result.NodeStartLine;

    public int NodeStartColumn => _result.NodeStartColumn;

    public int NodeEndLine => _result.NodeEndLine;

    public int NodeEndColumn => _result.NodeEndColumn;

    public string NodeKind => _result.NodeKind;

    public string? MethodName => _result.MethodName;

    public string ProgramPointKind => _result.ProgramPointKind;

    public int FactCount { get; }

    public IReadOnlyList<string> Facts { get; }

    public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; }

    public SymbolicCompactInvariantSummary ObservedInvariant { get; }

    public SymbolicCompactInvariantSummary ConservativeInvariant { get; }

    public SymbolicCompactInvariantQueryView InvariantQuery { get; }

    public string MergedInvariantText => ConservativeInvariant.Text;

    public int PathConditionCount { get; }

    public IReadOnlyList<SymbolicInvariantCondition> InvariantConditions { get; }

    internal IReadOnlyList<SymbolicInvariantCondition> PathConditions => InvariantConditions;

    public string Reachability => _result.Reachability.ToString();

    public string ReachabilityReason => _result.ReachabilityReason;

    public IReadOnlyList<SymbolicConditionProofResult> ConditionProofs { get; }

    public SymbolicProofOutcomeSummary ProofOutcomes => _result.ProofOutcomes;

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
            result,
            focusedFacts.Count,
            facts,
            symbolicFacts,
            observedInvariant,
            conservativeInvariant,
            SymbolicCompactInvariantQueryView.FromQueryView(result.InvariantQuery, options),
            focusedPathConditions.Count,
            pathConditions,
            conditionProofs,
            SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics),
            truncation);
    }
}

public sealed class SymbolicCompactInvariantSummary
{
    private readonly SymbolicInvariantResult _invariant;

    private SymbolicCompactInvariantSummary(
        SymbolicInvariantResult invariant,
        IReadOnlyList<string> conditions,
        int targetCount,
        IReadOnlyList<string> targets,
        int rawFactCount,
        IReadOnlyList<string> rawFacts,
        SymbolicCompactMergedPathFacts? mergedPathFacts,
        bool conditionsTruncated,
        bool targetsTruncated,
        bool rawFactsTruncated)
    {
        _invariant = invariant ?? throw new ArgumentNullException(nameof(invariant));
        Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        TargetCount = targetCount;
        Targets = targets ?? throw new ArgumentNullException(nameof(targets));
        RawFactCount = rawFactCount;
        RawFacts = rawFacts ?? throw new ArgumentNullException(nameof(rawFacts));
        MergedPathFacts = mergedPathFacts;
        ConditionsTruncated = conditionsTruncated;
        TargetsTruncated = targetsTruncated;
        RawFactsTruncated = rawFactsTruncated;
    }

    public string MergeKind => _invariant.MergeKind.ToString();

    public string Text => _invariant.MergedInvariantText;

    public int ConditionCount => _invariant.ConditionCount;

    public IReadOnlyList<string> Conditions { get; }

    public int TargetCount { get; }

    public IReadOnlyList<string> Targets { get; }

    public int RawFactCount { get; }

    public IReadOnlyList<string> RawFacts { get; }

    public int ConservativeUnknownCount => _invariant.ConservativeUnknownCount;

    public bool HasConservativeUnknowns => _invariant.HasConservativeUnknowns;

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
            invariant,
            conditionProjection.Items,
            targetProjection.TotalCount,
            targetProjection.Items,
            rawFactProjection.TotalCount,
            rawFactProjection.Items,
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
    private readonly SymbolicMergedPathFacts _facts;

    private SymbolicCompactMergedPathFacts(
        SymbolicMergedPathFacts facts,
        IReadOnlyList<string> alwaysFacts,
        IReadOnlyList<string> maybeFacts,
        IReadOnlyList<string> conservativeUnknowns,
        IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> conservativeUnknownDiagnostics,
        bool alwaysFactsTruncated,
        bool maybeFactsTruncated,
        bool conservativeUnknownsTruncated,
        bool conservativeUnknownDiagnosticsTruncated)
    {
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        AlwaysFacts = alwaysFacts ?? throw new ArgumentNullException(nameof(alwaysFacts));
        MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
        ConservativeUnknowns = conservativeUnknowns ?? throw new ArgumentNullException(nameof(conservativeUnknowns));
        ConservativeUnknownDiagnostics = conservativeUnknownDiagnostics ??
                                         throw new ArgumentNullException(nameof(conservativeUnknownDiagnostics));
        AlwaysFactsTruncated = alwaysFactsTruncated;
        MaybeFactsTruncated = maybeFactsTruncated;
        ConservativeUnknownsTruncated = conservativeUnknownsTruncated;
        ConservativeUnknownDiagnosticsTruncated = conservativeUnknownDiagnosticsTruncated;
    }

    public int AlwaysFactCount => _facts.AlwaysFacts.Count;

    public IReadOnlyList<string> AlwaysFacts { get; }

    public int MaybeFactCount => _facts.MaybeFacts.Count;

    public IReadOnlyList<string> MaybeFacts { get; }

    public int ConservativeUnknownCount => _facts.ConservativeUnknownCount;

    public IReadOnlyList<string> ConservativeUnknowns { get; }

    public IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> ConservativeUnknownDiagnostics { get; }

    public int CandidateProgramPointCount => _facts.CandidateProgramPointCount;

    public int UnreachableProgramPointCount => _facts.UnreachableProgramPointCount;

    public bool IsUnreachable => _facts.IsUnreachable;

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
            facts,
            SymbolicCompactProjection.Take(facts.AlwaysFacts, options.MaxConditions),
            SymbolicCompactProjection.Take(facts.MaybeFacts, options.MaxConditions),
            SymbolicCompactProjection.Take(facts.ConservativeUnknowns, options.MaxConditions),
            conservativeUnknownDiagnostics,
            facts.AlwaysFacts.Count > options.MaxConditions,
            facts.MaybeFacts.Count > options.MaxConditions,
            facts.ConservativeUnknowns.Count > options.MaxConditions,
            facts.ConservativeUnknownDiagnostics.Count > options.MaxConditions);
    }
}

public sealed class SymbolicCompactConservativeUnknownDiagnostic
{
    private readonly SymbolicConservativeUnknownDiagnostic _diagnostic;

    private SymbolicCompactConservativeUnknownDiagnostic(
        SymbolicConservativeUnknownDiagnostic diagnostic,
        IReadOnlyList<string> maybeFacts,
        bool maybeFactsTruncated)
    {
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        MaybeFacts = maybeFacts ?? throw new ArgumentNullException(nameof(maybeFacts));
        MaybeFactsTruncated = maybeFactsTruncated;
    }

    public string Target => _diagnostic.Target;

    public string UnknownText => _diagnostic.UnknownText;

    public string Reason => _diagnostic.Reason;

    public int MaybeFactCount => _diagnostic.MaybeFactCount;

    public IReadOnlyList<string> MaybeFacts { get; }

    public int CandidateProgramPointCount => _diagnostic.CandidateProgramPointCount;

    public int UnreachableProgramPointCount => _diagnostic.UnreachableProgramPointCount;

    public bool MaybeFactsTruncated { get; }

    internal static SymbolicCompactConservativeUnknownDiagnostic FromDiagnostic(
        SymbolicConservativeUnknownDiagnostic diagnostic,
        SymbolicCompactQueryOptions options)
    {
        return new SymbolicCompactConservativeUnknownDiagnostic(
            diagnostic,
            SymbolicCompactProjection.Take(diagnostic.MaybeFacts, options.MaxConditions),
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
