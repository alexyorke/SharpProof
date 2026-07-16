using System.Text.Json;

namespace SharpProof.Symbolic;

internal sealed record SymbolicCompactLineResult(
    JsonElement Json,
    SymbolicCompactScopeProjection Projection) : ISymbolicRawJsonProjection
{
    internal IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints => Projection.ProgramPoints;
    internal SymbolicCompactOutputTruncation Truncation => Projection.Truncation;

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
        return new SymbolicCompactLineResult(
            SymbolicOrderedJson.Object(
                ("filePath", result.FilePath), ("line", result.Line),
                ("programPointCount", result.ProgramPoints.Count),
                ("observedInvariant", projection.ObservedInvariant),
                ("conservativeInvariant", projection.ConservativeInvariant),
                ("invariantQuery", projection.InvariantQuery),
                ("mergedInvariantText", projection.ConservativeInvariant.Text),
                ("reachability", projection.Reachability),
                ("programPointSummary", projection.ProgramPointSummary),
                ("proofOutcomes", projection.ProgramPointSummary.ProofOutcomes),
                ("conditionProofs", projection.ConditionProofs),
                ("programPoints", projection.ProgramPoints),
                ("smtDiagnostics", projection.SmtDiagnostics),
                ("truncation", projection.Truncation)),
            projection);
    }
}

internal sealed record SymbolicCompactProgramPointResult(
    JsonElement Json,
    SymbolicCompactOutputTruncation Truncation) : ISymbolicRawJsonProjection
{

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

        var invariantQuery = SymbolicCompactInvariantQueryView.FromQueryView(result.InvariantQuery, options);
        return new SymbolicCompactProgramPointResult(
            SymbolicOrderedJson.Object(
                ("filePath", result.FilePath), ("line", result.Line), ("column", result.Column),
                ("position", result.Position), ("requestedLine", result.RequestedLine),
                ("requestedColumn", result.RequestedColumn), ("requestedPosition", result.RequestedPosition),
                ("requestedPositionDistance", result.RequestedPositionDistance),
                ("containsRequestedPosition", result.ContainsRequestedPosition),
                ("nodeSpanStart", result.NodeSpanStart), ("nodeSpanEnd", result.NodeSpanEnd),
                ("nodeSpanLength", result.NodeSpanLength), ("nodeStartLine", result.NodeStartLine),
                ("nodeStartColumn", result.NodeStartColumn), ("nodeEndLine", result.NodeEndLine),
                ("nodeEndColumn", result.NodeEndColumn), ("nodeKind", result.NodeKind),
                ("methodName", result.MethodName), ("programPointKind", result.ProgramPointKind),
                ("factCount", focusedFacts.Count), ("facts", facts), ("symbolicFacts", symbolicFacts),
                ("observedInvariant", observed), ("conservativeInvariant", conservative),
                ("invariantQuery", invariantQuery), ("mergedInvariantText", conservative.Text),
                ("pathConditionCount", focusedConditions.Count), ("invariantConditions", conditions),
                ("reachability", result.Reachability.ToString()),
                ("reachabilityReason", result.ReachabilityReason), ("conditionProofs", proofs),
                ("proofOutcomes", result.ProofOutcomes),
                ("smtDiagnostics", SymbolicCompactSmtDiagnostics.FromDiagnostics(result.SmtDiagnostics)),
                ("truncation", truncation)),
            truncation);
    }
}

internal sealed record SymbolicCompactInvariantSummary(
    JsonElement Json,
    string Text,
    int ConditionCount,
    int TargetCount,
    IReadOnlyList<string> Targets,
    bool ConditionsTruncated,
    bool TargetsTruncated,
    bool RawFactsTruncated,
    bool MergedPathFactsTruncated) : ISymbolicRawJsonProjection
{

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
        var compactMerged = mergedPathFacts == null
            ? null
            : SymbolicCompactMergedPathFacts.FromMergedPathFacts(mergedPathFacts, options);
        return new SymbolicCompactInvariantSummary(
            SymbolicOrderedJson.Object(
                ("mergeKind", invariant.MergeKind.ToString()),
                ("text", invariant.MergedInvariantText),
                ("conditionCount", invariant.ConditionCount),
                ("conditions", conditionProjection.Items),
                ("targetCount", targetProjection.TotalCount),
                ("targets", targetProjection.Items),
                ("rawFactCount", rawFactProjection.TotalCount),
                ("rawFacts", rawFactProjection.Items),
                ("conservativeUnknownCount", invariant.ConservativeUnknownCount),
                ("hasConservativeUnknowns", invariant.HasConservativeUnknowns),
                ("mergedPathFacts", compactMerged),
                ("conditionsTruncated", conditionProjection.IsTruncated),
                ("targetsTruncated", targetProjection.IsTruncated),
                ("rawFactsTruncated", rawFactProjection.IsTruncated)),
            invariant.MergedInvariantText,
            invariant.ConditionCount,
            targetProjection.TotalCount,
            targetProjection.Items,
            conditionProjection.IsTruncated,
            targetProjection.IsTruncated,
            rawFactProjection.IsTruncated,
            compactMerged?.IsTruncated == true);
    }
}

internal sealed record SymbolicCompactMergedPathFacts(
    JsonElement Json,
    bool IsTruncated) : ISymbolicRawJsonProjection
{

    internal static SymbolicCompactMergedPathFacts FromMergedPathFacts(
        SymbolicMergedPathFacts facts,
        SymbolicCompactQueryOptions options)
    {
        var diagnostics = SymbolicCompactProjection.Take(facts.ConservativeUnknownDiagnostics, options.MaxConditions)
            .Select(diagnostic => SymbolicCompactConservativeUnknownDiagnostic.FromDiagnostic(diagnostic, options))
            .ToArray();
        var alwaysFacts = SymbolicCompactProjection.Take(facts.AlwaysFacts, options.MaxConditions);
        var maybeFacts = SymbolicCompactProjection.Take(facts.MaybeFacts, options.MaxConditions);
        var unknowns = SymbolicCompactProjection.Take(facts.ConservativeUnknowns, options.MaxConditions);
        var alwaysTruncated = facts.AlwaysFacts.Count > options.MaxConditions;
        var maybeTruncated = facts.MaybeFacts.Count > options.MaxConditions;
        var unknownsTruncated = facts.ConservativeUnknowns.Count > options.MaxConditions;
        var diagnosticsTruncated = facts.ConservativeUnknownDiagnostics.Count > options.MaxConditions;
        return new SymbolicCompactMergedPathFacts(
            SymbolicOrderedJson.Object(
                ("alwaysFactCount", facts.AlwaysFacts.Count),
                ("alwaysFacts", alwaysFacts),
                ("maybeFactCount", facts.MaybeFacts.Count),
                ("maybeFacts", maybeFacts),
                ("conservativeUnknownCount", facts.ConservativeUnknownCount),
                ("conservativeUnknowns", unknowns),
                ("conservativeUnknownDiagnostics", diagnostics),
                ("candidateProgramPointCount", facts.CandidateProgramPointCount),
                ("unreachableProgramPointCount", facts.UnreachableProgramPointCount),
                ("isUnreachable", facts.IsUnreachable),
                ("alwaysFactsTruncated", alwaysTruncated),
                ("maybeFactsTruncated", maybeTruncated),
                ("conservativeUnknownsTruncated", unknownsTruncated),
                ("conservativeUnknownDiagnosticsTruncated", diagnosticsTruncated)),
            alwaysTruncated || maybeTruncated || unknownsTruncated || diagnosticsTruncated ||
            diagnostics.Any(static diagnostic => diagnostic.MaybeFactsTruncated));
    }
}

internal sealed record SymbolicCompactConservativeUnknownDiagnostic(
    JsonElement Json,
    string Target,
    string UnknownText,
    string Reason,
    bool MaybeFactsTruncated) : ISymbolicRawJsonProjection
{

    internal static SymbolicCompactConservativeUnknownDiagnostic FromDiagnostic(
        SymbolicConservativeUnknownDiagnostic diagnostic,
        SymbolicCompactQueryOptions options)
    {
        var maybeFacts = SymbolicCompactProjection.Take(diagnostic.MaybeFacts, options.MaxConditions);
        var truncated = diagnostic.MaybeFacts.Count > options.MaxConditions;
        return new SymbolicCompactConservativeUnknownDiagnostic(
            SymbolicOrderedJson.Object(
                ("target", diagnostic.Target),
                ("unknownText", diagnostic.UnknownText),
                ("reason", diagnostic.Reason),
                ("maybeFactCount", diagnostic.MaybeFactCount),
                ("maybeFacts", maybeFacts),
                ("candidateProgramPointCount", diagnostic.CandidateProgramPointCount),
                ("unreachableProgramPointCount", diagnostic.UnreachableProgramPointCount),
                ("maybeFactsTruncated", truncated)),
            diagnostic.Target,
            diagnostic.UnknownText,
            diagnostic.Reason,
            truncated);
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
