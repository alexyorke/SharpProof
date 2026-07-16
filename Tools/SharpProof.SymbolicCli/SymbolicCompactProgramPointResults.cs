using System.Text.Json;

namespace SharpProof.Symbolic;

internal sealed record SymbolicCompactLineResult(
    JsonElement Json,
    IReadOnlyList<SymbolicCompactProgramPointResult> ProgramPoints,
    SymbolicCompactOutputTruncation Truncation) : ISymbolicRawJsonProjection;

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
    JsonElement Json, string Text,
    int MustFactCount, int MaybeFactCount, int UnknownFactCount,
    int TargetSummaryCount, int TargetPathSummaryCount,
    IReadOnlyList<string> TargetFilters, bool HasTargetFilter, int DiagnosticCount,
    string Status, string StatusReason, string Summary,
    bool HasUnresolvedAnalysis, bool IsTruncated, IReadOnlyList<string> TargetPathTargets,
    IReadOnlyList<string> ReasonDetails) : ISymbolicRawJsonProjection
{

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
        var visibleTargetSummaries = SymbolicCompactProjection.Take(targets, options.MaxConditions);
        var targetSummaries = visibleTargetSummaries.Select(target => ProjectTargetSummary(target, options)).ToArray();
        var pathTargets = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.TargetPathSummaries, options.InvariantTargets, static summary => summary.Target);
        var visiblePathTargets = SymbolicCompactProjection.Take(pathTargets, options.MaxConditions);
        var pathSummaries = visiblePathTargets.Select(target => ProjectTargetPathSummary(target, options)).ToArray();
        var visibleDiagnostics = SymbolicCompactProjection.Take(query.Diagnostics, options.MaxConditions);
        var diagnostics = visibleDiagnostics.Select(diagnostic => ProjectDiagnostic(diagnostic, options)).ToArray();
        var matched = SymbolicInvariantTargetFilter.GetMatchedTargetFilters(query, options.InvariantTargets);
        var unmatched = SymbolicInvariantTargetFilter.GetUnmatchedTargetFilters(options.InvariantTargets, matched);
        var visibleMatched = SymbolicCompactProjection.Take(matched, options.MaxConditions);
        var visibleUnmatched = SymbolicCompactProjection.Take(unmatched, options.MaxConditions);
        var visibleMustFacts = SymbolicCompactProjection.Take(mustFacts, options.MaxConditions);
        var visibleMaybeFacts = SymbolicCompactProjection.Take(maybeFacts, options.MaxConditions);
        var visibleUnknownFacts = SymbolicCompactProjection.Take(unknownFacts, options.MaxConditions);
        var mustFactsTruncated = mustFacts.Count > options.MaxConditions;
        var maybeFactsTruncated = maybeFacts.Count > options.MaxConditions;
        var unknownFactsTruncated = unknownFacts.Count > options.MaxConditions;
        var unknownDiagnosticsTruncated = unknownSource.Count > options.MaxConditions;
        var targetSummariesTruncated = targets.Count > targetSummaries.Length;
        var targetPathSummariesTruncated = pathTargets.Count > pathSummaries.Length;
        var matchedTargetFiltersTruncated = matched.Count > visibleMatched.Count;
        var unmatchedTargetFiltersTruncated = unmatched.Count > visibleUnmatched.Count;
        var diagnosticsTruncated = query.Diagnostics.Count > options.MaxConditions;
        var isTruncated =
            mustFactsTruncated || maybeFactsTruncated || unknownFactsTruncated || unknownDiagnosticsTruncated ||
            targetSummariesTruncated || targetPathSummariesTruncated || matchedTargetFiltersTruncated ||
            unmatchedTargetFiltersTruncated || diagnosticsTruncated ||
            visibleDiagnostics.Any(diagnostic =>
                diagnostic.EvidenceTruncated || diagnostic.Evidence.Count > options.MaxConditions) ||
            unknownDiagnostics.Any(static diagnostic => diagnostic.MaybeFactsTruncated) ||
            visibleTargetSummaries.Any(target =>
                target.MustFactCount > options.MaxConditions ||
                target.MaybeFactCount > options.MaxConditions ||
                target.UnknownFactCount > options.MaxConditions) ||
            visiblePathTargets.Any(target =>
                target.ConditionsTruncated || target.Conditions.Count > options.MaxConditions);
        var status = query.Status.ToString();
        var reasonDetails = visibleDiagnostics
            .Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message)
            .Concat(unknownDiagnostics.Select(static diagnostic =>
                diagnostic.UnknownText + ": " + diagnostic.Reason))
            .ToArray();
        var json = SymbolicOrderedJson.Object(
            ("text", text), ("mergeKind", query.MergeKind.ToString()),
            ("mustFactCount", mustFacts.Count), ("mustFacts", visibleMustFacts),
            ("maybeFactCount", maybeFacts.Count), ("maybeFacts", visibleMaybeFacts),
            ("unknownFactCount", unknownFacts.Count), ("unknownFacts", visibleUnknownFacts),
            ("unknownDiagnostics", unknownDiagnostics), ("targetSummaryCount", targets.Count),
            ("targetSummaries", targetSummaries), ("targetPathSummaryCount", pathTargets.Count),
            ("targetPathSummaries", pathSummaries), ("targetFilters", options.InvariantTargets),
            ("targetFilterCount", options.InvariantTargets.Count), ("hasTargetFilter", options.HasInvariantTargetFilter),
            ("targetFilterMatched", !options.HasInvariantTargetFilter || matched.Count != 0),
            ("matchedTargetFilterCount", matched.Count), ("matchedTargetFilters", visibleMatched),
            ("unmatchedTargetFilterCount", unmatched.Count), ("unmatchedTargetFilters", visibleUnmatched),
            ("unfilteredTargetSummaryCount", query.TargetSummaryCount),
            ("unfilteredTargetPathSummaryCount", query.TargetPathSummaryCount),
            ("diagnosticCount", query.DiagnosticCount), ("diagnostics", diagnostics),
            ("candidateProgramPointCount", query.CandidateProgramPointCount),
            ("unreachableProgramPointCount", query.UnreachableProgramPointCount),
            ("isUnreachable", query.IsUnreachable), ("status", status),
            ("statusReason", query.StatusReason), ("summary", query.Summary),
            ("hasMaybeFacts", maybeFacts.Count != 0), ("hasUnknowns", unknownFacts.Count != 0),
            ("hasUnresolvedAnalysis", query.HasUnresolvedAnalysis),
            ("mustFactsTruncated", mustFactsTruncated), ("maybeFactsTruncated", maybeFactsTruncated),
            ("unknownFactsTruncated", unknownFactsTruncated),
            ("unknownDiagnosticsTruncated", unknownDiagnosticsTruncated),
            ("targetSummariesTruncated", targetSummariesTruncated),
            ("targetPathSummariesTruncated", targetPathSummariesTruncated),
            ("matchedTargetFiltersTruncated", matchedTargetFiltersTruncated),
            ("unmatchedTargetFiltersTruncated", unmatchedTargetFiltersTruncated),
            ("diagnosticsTruncated", diagnosticsTruncated),
            ("isTruncated", isTruncated));
        return new SymbolicCompactInvariantQueryView(
            json, text,
            mustFacts.Count, maybeFacts.Count, unknownFacts.Count,
            targets.Count, pathTargets.Count,
            options.InvariantTargets, options.HasInvariantTargetFilter, query.DiagnosticCount,
            status, query.StatusReason, query.Summary, query.HasUnresolvedAnalysis, isTruncated,
            visiblePathTargets.Select(static target => target.Target).ToArray(),
            reasonDetails);
    }

    private static JsonElement ProjectTargetSummary(
        SymbolicInvariantTargetSummary summary,
        SymbolicCompactQueryOptions options) => SymbolicOrderedJson.Object(
        ("target", summary.Target), ("status", summary.Status.ToString()),
        ("statusReason", summary.StatusReason), ("reasonCode", summary.ReasonCode),
        ("summary", summary.Summary), ("mustFactCount", summary.MustFactCount),
        ("mustFacts", SymbolicCompactProjection.Take(summary.MustFacts, options.MaxConditions)),
        ("maybeFactCount", summary.MaybeFactCount),
        ("maybeFacts", SymbolicCompactProjection.Take(summary.MaybeFacts, options.MaxConditions)),
        ("unknownFactCount", summary.UnknownFactCount),
        ("unknownFacts", SymbolicCompactProjection.Take(summary.UnknownFacts, options.MaxConditions)),
        ("mustFactsTruncated", summary.MustFactCount > options.MaxConditions),
        ("maybeFactsTruncated", summary.MaybeFactCount > options.MaxConditions),
        ("unknownFactsTruncated", summary.UnknownFactCount > options.MaxConditions));

    private static JsonElement ProjectTargetPathSummary(
        SymbolicInvariantTargetPathSummary summary,
        SymbolicCompactQueryOptions options)
    {
        var conditions = SymbolicCompactProjection.Take(summary.Conditions, options.MaxConditions);
        var truncated = summary.ConditionsTruncated || summary.Conditions.Count > conditions.Count;
        return SymbolicOrderedJson.Object(
            ("target", summary.Target), ("pathConditionCount", summary.PathConditionCount),
            ("smtConditionCount", summary.SmtConditionCount),
            ("conservativeUnknownCount", summary.ConservativeUnknownCount),
            ("programPointCount", summary.ProgramPointCount),
            ("reachableProgramPointCount", summary.ReachableProgramPointCount),
            ("proofTotalCount", summary.ProofTotalCount), ("proofUnknownCount", summary.ProofUnknownCount),
            ("proofProvenTrueCount", summary.ProofProvenTrueCount),
            ("proofProvenFalseCount", summary.ProofProvenFalseCount),
            ("proofUnreachableCount", summary.ProofUnreachableCount), ("conditions", conditions),
            ("conditionsTruncated", truncated), ("statusReason", summary.StatusReason),
            ("reasonCode", summary.ReasonCode),
            ("summary", summary.Summary));
    }

    private static JsonElement ProjectDiagnostic(
        SymbolicInvariantQueryDiagnostic diagnostic,
        SymbolicCompactQueryOptions options)
    {
        var evidence = SymbolicCompactProjection.Take(diagnostic.Evidence, options.MaxConditions);
        var truncated = diagnostic.EvidenceTruncated || diagnostic.Evidence.Count > options.MaxConditions;
        return SymbolicOrderedJson.Object(
            ("code", diagnostic.Code), ("severity", diagnostic.Severity),
            ("message", diagnostic.Message), ("count", diagnostic.Count),
            ("evidenceTotalCount", diagnostic.EvidenceTotalCount), ("evidence", evidence),
            ("evidenceTruncated", truncated));
    }
}
