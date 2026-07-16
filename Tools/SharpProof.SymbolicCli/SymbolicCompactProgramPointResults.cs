namespace SharpProof.Symbolic;

internal sealed class SymbolicCompactLineResult(
    SymbolicQueryResult result,
    SymbolicCompactScopeProjection projection)
{
    public string FilePath => result.FilePath;
    public int Line => result.Line ?? 0;
    public int ProgramPointCount => result.ProgramPoints.Count;
    public SymbolicCompactInvariantSummary ObservedInvariant => projection.ObservedInvariant;
    public SymbolicCompactInvariantSummary ConservativeInvariant => projection.ConservativeInvariant;
    public SymbolicCompactInvariantQueryView InvariantQuery => projection.InvariantQuery;
    public string MergedInvariantText => ConservativeInvariant.Text;
    public SymbolicReachabilitySummary Reachability => projection.Reachability;
    public SymbolicProgramPointSummary ProgramPointSummary => projection.ProgramPointSummary;
    public SymbolicProofOutcomeSummary ProofOutcomes => ProgramPointSummary.ProofOutcomes;
    public IReadOnlyList<SymbolicConditionProofSummary> ConditionProofs => projection.ConditionProofs;
    public IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints => projection.ProgramPoints;
    public SymbolicCompactSmtDiagnostics SmtDiagnostics => projection.SmtDiagnostics;
    public SymbolicCompactOutputTruncation Truncation => projection.Truncation;
    internal SymbolicCompactScopeProjection Projection => projection;

    internal static SymbolicCompactLineResult FromResult(
        SymbolicQueryResult result,
        SymbolicCompactQueryOptions options,
        int maxProgramPoints)
    {
        return new SymbolicCompactLineResult(result, SymbolicCompactScopeProjection.Create(
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
            maxProgramPoints));
    }
}

internal sealed class SymbolicCompactProgramPointResult(
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
    public string FilePath => result.FilePath;
    public int Line => result.Line;
    public int Column => result.Column;
    public int Position => result.Position;
    public int? RequestedLine => result.RequestedLine;
    public int? RequestedColumn => result.RequestedColumn;
    public int? RequestedPosition => result.RequestedPosition;
    public int? RequestedPositionDistance => result.RequestedPositionDistance;
    public bool? ContainsRequestedPosition => result.ContainsRequestedPosition;
    public int NodeSpanStart => result.NodeSpanStart;
    public int NodeSpanEnd => result.NodeSpanEnd;
    public int NodeSpanLength => result.NodeSpanLength;
    public int NodeStartLine => result.NodeStartLine;
    public int NodeStartColumn => result.NodeStartColumn;
    public int NodeEndLine => result.NodeEndLine;
    public int NodeEndColumn => result.NodeEndColumn;
    public string NodeKind => result.NodeKind;
    public string? MethodName => result.MethodName;
    public string ProgramPointKind => result.ProgramPointKind;
    public int FactCount { get; } = factCount;
    public IReadOnlyList<string> Facts { get; } = facts;
    public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; } = symbolicFacts;
    public SymbolicCompactInvariantSummary ObservedInvariant { get; } = observedInvariant;
    public SymbolicCompactInvariantSummary ConservativeInvariant { get; } = conservativeInvariant;
    public SymbolicCompactInvariantQueryView InvariantQuery { get; } = invariantQuery;
    public string MergedInvariantText => ConservativeInvariant.Text;
    public int PathConditionCount { get; } = pathConditionCount;
    public IReadOnlyList<SymbolicInvariantCondition> InvariantConditions { get; } = pathConditions;
    internal IReadOnlyList<SymbolicInvariantCondition> PathConditions => InvariantConditions;
    public string Reachability => result.Reachability.ToString();
    public string ReachabilityReason => result.ReachabilityReason;
    public IReadOnlyList<SymbolicConditionProofResult> ConditionProofs { get; } = conditionProofs;
    public SymbolicProofOutcomeSummary ProofOutcomes => result.ProofOutcomes;
    public SymbolicCompactSmtDiagnostics SmtDiagnostics { get; } = smtDiagnostics;
    public SymbolicCompactOutputTruncation Truncation { get; } = truncation;

    internal static SymbolicCompactProgramPointResult FromResult(
        SymbolicProgramPointResult result,
        SymbolicCompactQueryOptions options)
    {
        var observed = SymbolicCompactInvariantSummary.FromObservedFacts(
            SymbolicInvariantResult.FromFacts(result.Facts), result.Facts, options);
        var conservative = SymbolicCompactInvariantSummary.FromInvariant(result.Invariant, null, options);
        var focusedConditions = SymbolicInvariantTargetFilter.ApplyToConditions(
            result.Invariant.Conditions, options.InvariantTargets);
        var focusedFacts = options.HasInvariantTargetFilter
            ? focusedConditions.Select(static condition => condition.Text)
                .Where(static fact => !string.IsNullOrWhiteSpace(fact))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : result.Facts;
        var focusedProofs = SymbolicInvariantTargetFilter.ApplyToProofResults(
            result.ConditionProofs, options.InvariantTargets);
        var facts = SymbolicCompactProjection.Take(focusedFacts, options.MaxFacts);
        var symbolicFacts = SymbolicCompactProjection.Take(result.SymbolicFacts, options.MaxFacts);
        var conditions = SymbolicCompactProjection.Take(focusedConditions, options.MaxConditions);
        var proofs = SymbolicCompactProjection.Take(focusedProofs, options.MaxProofs);
        var truncation = SymbolicCompactOutputTruncation.Combine(
            new SymbolicCompactOutputTruncation(
                false,
                false,
                focusedFacts.Count > facts.Count || result.SymbolicFacts.Count > symbolicFacts.Count,
                focusedConditions.Count > conditions.Count,
                focusedProofs.Count > proofs.Count),
            SymbolicCompactOutputTruncation.FromInvariant(observed),
            SymbolicCompactOutputTruncation.FromInvariant(conservative));

        return new SymbolicCompactProgramPointResult(
            result,
            focusedFacts.Count,
            facts,
            symbolicFacts,
            observed,
            conservative,
            SymbolicCompactInvariantQueryView.FromQueryView(result.InvariantQuery, options),
            focusedConditions.Count,
            conditions,
            proofs,
            SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics),
            truncation);
    }
}

internal sealed class SymbolicCompactInvariantSummary(
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
    public string MergeKind => invariant.MergeKind.ToString();
    public string Text => invariant.MergedInvariantText;
    public int ConditionCount => invariant.ConditionCount;
    public IReadOnlyList<string> Conditions { get; } = conditions;
    public int TargetCount { get; } = targetCount;
    public IReadOnlyList<string> Targets { get; } = targets;
    public int RawFactCount { get; } = rawFactCount;
    public IReadOnlyList<string> RawFacts { get; } = rawFacts;
    public int ConservativeUnknownCount => invariant.ConservativeUnknownCount;
    public bool HasConservativeUnknowns => invariant.HasConservativeUnknowns;
    public SymbolicCompactMergedPathFacts? MergedPathFacts { get; } = mergedPathFacts;
    public bool ConditionsTruncated { get; } = conditionsTruncated;
    public bool TargetsTruncated { get; } = targetsTruncated;
    public bool RawFactsTruncated { get; } = rawFactsTruncated;
    internal bool MergedPathFactsTruncated => MergedPathFacts?.IsTruncated == true;

    internal static SymbolicCompactInvariantSummary FromObservedFacts(
        SymbolicInvariantResult invariant,
        IReadOnlyList<string> rawFacts,
        SymbolicCompactQueryOptions options) => Create(invariant, rawFacts, null, options);

    internal static SymbolicCompactInvariantSummary FromInvariant(
        SymbolicInvariantResult invariant,
        SymbolicMergedPathFacts? mergedPathFacts,
        SymbolicCompactQueryOptions options) => Create(invariant, Array.Empty<string>(), mergedPathFacts, options);

    private static SymbolicCompactInvariantSummary Create(
        SymbolicInvariantResult invariant,
        IReadOnlyList<string> rawFacts,
        SymbolicMergedPathFacts? mergedPathFacts,
        SymbolicCompactQueryOptions options)
    {
        var conditionProjection = SymbolicCompactProjection.Project(
            invariant.Conditions.Select(static condition => condition.Text).ToArray(), options.MaxConditions);
        var targetProjection = SymbolicCompactProjection.Project(
            invariant.Conditions.Select(static condition => condition.Target)
                .Where(static target => !string.IsNullOrWhiteSpace(target))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            options.MaxConditions);
        var rawFactProjection = SymbolicCompactProjection.Project(rawFacts, options.MaxFacts);
        return new SymbolicCompactInvariantSummary(
            invariant,
            conditionProjection.Items,
            targetProjection.TotalCount,
            targetProjection.Items,
            rawFactProjection.TotalCount,
            rawFactProjection.Items,
            mergedPathFacts == null ? null : SymbolicCompactMergedPathFacts.FromMergedPathFacts(mergedPathFacts, options),
            conditionProjection.IsTruncated,
            targetProjection.IsTruncated,
            rawFactProjection.IsTruncated);
    }
}

internal sealed class SymbolicCompactMergedPathFacts(
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
    public int AlwaysFactCount => facts.AlwaysFacts.Count;
    public IReadOnlyList<string> AlwaysFacts { get; } = alwaysFacts;
    public int MaybeFactCount => facts.MaybeFacts.Count;
    public IReadOnlyList<string> MaybeFacts { get; } = maybeFacts;
    public int ConservativeUnknownCount => facts.ConservativeUnknownCount;
    public IReadOnlyList<string> ConservativeUnknowns { get; } = conservativeUnknowns;
    public IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> ConservativeUnknownDiagnostics { get; } =
        conservativeUnknownDiagnostics;
    public int CandidateProgramPointCount => facts.CandidateProgramPointCount;
    public int UnreachableProgramPointCount => facts.UnreachableProgramPointCount;
    public bool IsUnreachable => facts.IsUnreachable;
    public bool AlwaysFactsTruncated { get; } = alwaysFactsTruncated;
    public bool MaybeFactsTruncated { get; } = maybeFactsTruncated;
    public bool ConservativeUnknownsTruncated { get; } = conservativeUnknownsTruncated;
    public bool ConservativeUnknownDiagnosticsTruncated { get; } = conservativeUnknownDiagnosticsTruncated;
    internal bool IsTruncated =>
        AlwaysFactsTruncated || MaybeFactsTruncated || ConservativeUnknownsTruncated ||
        ConservativeUnknownDiagnosticsTruncated ||
        ConservativeUnknownDiagnostics.Any(static diagnostic => diagnostic.MaybeFactsTruncated);

    internal static SymbolicCompactMergedPathFacts FromMergedPathFacts(
        SymbolicMergedPathFacts facts,
        SymbolicCompactQueryOptions options)
    {
        var diagnostics = SymbolicCompactProjection.Take(facts.ConservativeUnknownDiagnostics, options.MaxConditions)
            .Select(diagnostic => SymbolicCompactConservativeUnknownDiagnostic.FromDiagnostic(diagnostic, options))
            .ToArray();
        return new SymbolicCompactMergedPathFacts(
            facts,
            SymbolicCompactProjection.Take(facts.AlwaysFacts, options.MaxConditions),
            SymbolicCompactProjection.Take(facts.MaybeFacts, options.MaxConditions),
            SymbolicCompactProjection.Take(facts.ConservativeUnknowns, options.MaxConditions),
            diagnostics,
            facts.AlwaysFacts.Count > options.MaxConditions,
            facts.MaybeFacts.Count > options.MaxConditions,
            facts.ConservativeUnknowns.Count > options.MaxConditions,
            facts.ConservativeUnknownDiagnostics.Count > options.MaxConditions);
    }
}

internal sealed class SymbolicCompactConservativeUnknownDiagnostic(
    SymbolicConservativeUnknownDiagnostic diagnostic,
    IReadOnlyList<string> maybeFacts,
    bool maybeFactsTruncated)
{
    public string Target => diagnostic.Target;
    public string UnknownText => diagnostic.UnknownText;
    public string Reason => diagnostic.Reason;
    public int MaybeFactCount => diagnostic.MaybeFactCount;
    public IReadOnlyList<string> MaybeFacts { get; } = maybeFacts;
    public int CandidateProgramPointCount => diagnostic.CandidateProgramPointCount;
    public int UnreachableProgramPointCount => diagnostic.UnreachableProgramPointCount;
    public bool MaybeFactsTruncated { get; } = maybeFactsTruncated;

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

internal sealed record SymbolicCompactInvariantQueryView(
    string Text,
    string MergeKind,
    int MustFactCount,
    IReadOnlyList<string> MustFacts,
    int MaybeFactCount,
    IReadOnlyList<string> MaybeFacts,
    int UnknownFactCount,
    IReadOnlyList<string> UnknownFacts,
    IReadOnlyList<SymbolicCompactConservativeUnknownDiagnostic> UnknownDiagnostics,
    int TargetSummaryCount,
    IReadOnlyList<SymbolicCompactInvariantTargetSummary> TargetSummaries,
    int TargetPathSummaryCount,
    IReadOnlyList<SymbolicCompactInvariantTargetPathSummary> TargetPathSummaries,
    IReadOnlyList<string> TargetFilters,
    int TargetFilterCount,
    bool HasTargetFilter,
    bool TargetFilterMatched,
    int MatchedTargetFilterCount,
    IReadOnlyList<string> MatchedTargetFilters,
    int UnmatchedTargetFilterCount,
    IReadOnlyList<string> UnmatchedTargetFilters,
    int UnfilteredTargetSummaryCount,
    int UnfilteredTargetPathSummaryCount,
    int DiagnosticCount,
    IReadOnlyList<SymbolicCompactInvariantQueryDiagnostic> Diagnostics,
    int CandidateProgramPointCount,
    int UnreachableProgramPointCount,
    bool IsUnreachable,
    string Status,
    string StatusReason,
    string Summary,
    bool HasMaybeFacts,
    bool HasUnknowns,
    bool HasUnresolvedAnalysis,
    bool MustFactsTruncated,
    bool MaybeFactsTruncated,
    bool UnknownFactsTruncated,
    bool UnknownDiagnosticsTruncated,
    bool TargetSummariesTruncated,
    bool TargetPathSummariesTruncated,
    bool MatchedTargetFiltersTruncated,
    bool UnmatchedTargetFiltersTruncated,
    bool DiagnosticsTruncated)
{
    public bool IsTruncated =>
        MustFactsTruncated || MaybeFactsTruncated || UnknownFactsTruncated || UnknownDiagnosticsTruncated ||
        TargetSummariesTruncated || TargetPathSummariesTruncated || MatchedTargetFiltersTruncated ||
        UnmatchedTargetFiltersTruncated || DiagnosticsTruncated ||
        Diagnostics.Any(static diagnostic => diagnostic.EvidenceTruncated) ||
        UnknownDiagnostics.Any(static diagnostic => diagnostic.MaybeFactsTruncated) ||
        TargetSummaries.Any(static target => target.IsTruncated) ||
        TargetPathSummaries.Any(static target => target.ConditionsTruncated);

    internal static SymbolicCompactInvariantQueryView FromQueryView(
        SymbolicInvariantQueryView query,
        SymbolicCompactQueryOptions options)
    {
        var targets = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.TargetSummaries, options.InvariantTargets, static summary => summary.Target);
        var mustFacts = SymbolicInvariantTargetFilter.SelectFacts(
            query.MustFacts, targets, options.InvariantTargets, static summary => summary.MustFacts);
        var maybeFacts = SymbolicInvariantTargetFilter.SelectFacts(
            query.MaybeFacts, targets, options.InvariantTargets, static summary => summary.MaybeFacts);
        var unknownFacts = SymbolicInvariantTargetFilter.SelectFacts(
            query.UnknownFacts, targets, options.InvariantTargets, static summary => summary.UnknownFacts);
        var text = options.HasInvariantTargetFilter
            ? SymbolicInvariantService.FormatMergedInvariantFacts(mustFacts.Concat(unknownFacts).ToArray())
            : query.Text;
        var unknownSource = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.UnknownDiagnostics, options.InvariantTargets, static diagnostic => diagnostic.Target);
        var unknownDiagnostics = SymbolicCompactProjection.Take(unknownSource, options.MaxConditions)
            .Select(diagnostic => SymbolicCompactConservativeUnknownDiagnostic.FromDiagnostic(diagnostic, options))
            .ToArray();
        var targetSummaries = SymbolicCompactProjection.Take(targets, options.MaxConditions)
            .Select(target => SymbolicCompactInvariantTargetSummary.FromSummary(target, options))
            .ToArray();
        var pathTargets = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.TargetPathSummaries, options.InvariantTargets, static summary => summary.Target);
        var pathSummaries = SymbolicCompactProjection.Take(pathTargets, options.MaxConditions)
            .Select(target => SymbolicCompactInvariantTargetPathSummary.FromSummary(target, options))
            .ToArray();
        var diagnostics = SymbolicCompactProjection.Take(query.Diagnostics, options.MaxConditions)
            .Select(diagnostic => SymbolicCompactInvariantQueryDiagnostic.FromDiagnostic(diagnostic, options))
            .ToArray();
        var matched = SymbolicInvariantTargetFilter.GetMatchedTargetFilters(query, options.InvariantTargets);
        var unmatched = SymbolicInvariantTargetFilter.GetUnmatchedTargetFilters(options.InvariantTargets, matched);
        var visibleMatched = SymbolicCompactProjection.Take(matched, options.MaxConditions);
        var visibleUnmatched = SymbolicCompactProjection.Take(unmatched, options.MaxConditions);
        return new SymbolicCompactInvariantQueryView(
            text,
            query.MergeKind.ToString(),
            mustFacts.Count,
            SymbolicCompactProjection.Take(mustFacts, options.MaxConditions),
            maybeFacts.Count,
            SymbolicCompactProjection.Take(maybeFacts, options.MaxConditions),
            unknownFacts.Count,
            SymbolicCompactProjection.Take(unknownFacts, options.MaxConditions),
            unknownDiagnostics,
            targets.Count,
            targetSummaries,
            pathTargets.Count,
            pathSummaries,
            options.InvariantTargets,
            options.InvariantTargets.Count,
            options.HasInvariantTargetFilter,
            !options.HasInvariantTargetFilter || matched.Count != 0,
            matched.Count,
            visibleMatched,
            unmatched.Count,
            visibleUnmatched,
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
            maybeFacts.Count != 0,
            unknownFacts.Count != 0,
            query.HasUnresolvedAnalysis,
            mustFacts.Count > options.MaxConditions,
            maybeFacts.Count > options.MaxConditions,
            unknownFacts.Count > options.MaxConditions,
            unknownSource.Count > options.MaxConditions,
            targets.Count > targetSummaries.Length,
            pathTargets.Count > pathSummaries.Length,
            matched.Count > visibleMatched.Count,
            unmatched.Count > visibleUnmatched.Count,
            query.Diagnostics.Count > options.MaxConditions);
    }
}
