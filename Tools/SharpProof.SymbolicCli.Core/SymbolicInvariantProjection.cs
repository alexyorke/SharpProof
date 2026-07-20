using SharpProof.Symbolic;

internal enum SymbolicInvariantQueryStatus {
    Exact,
    Conservative,
    Unresolved,
    Unreachable
}

internal sealed record SymbolicInvariantTargetSummary(
    string Target,
    IReadOnlyList<string> MustFacts,
    IReadOnlyList<string> MaybeFacts,
    IReadOnlyList<string> UnknownFacts) {
    internal int MustFactCount => MustFacts.Count;
    internal int MaybeFactCount => MaybeFacts.Count;
    internal int UnknownFactCount => UnknownFacts.Count;
    internal SymbolicInvariantQueryStatus Status =>
        UnknownFacts.Count != 0 || MaybeFacts.Count != 0
            ? SymbolicInvariantQueryStatus.Conservative
            : SymbolicInvariantQueryStatus.Exact;
    internal string StatusReason => UnknownFacts.Count != 0
        ? "target_has_conservative_unknowns"
        : MaybeFacts.Count != 0 ? "target_has_path_specific_facts" : "target_exact";
    internal string ReasonCode => UnknownFacts.Count != 0
        ? "SP-SYM-TARGET-CONSERVATIVE-UNKNOWN"
        : MaybeFacts.Count != 0 ? "SP-SYM-TARGET-PATH-SPECIFIC" : "SP-SYM-TARGET-EXACT";
    internal string Summary => UnknownFacts.Count != 0
        ? "Facts for this target differ across selected paths; the merged invariant keeps a conservative unknown for the target."
        : MaybeFacts.Count != 0
            ? "Some facts for this target apply only to a subset of selected paths."
            : "All selected reachable program points agree on the facts for this target.";
}

internal sealed record SymbolicInvariantTargetPathSummary(
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
    bool ConditionsTruncated) {
    internal string StatusReason => ProofUnknownCount != 0
        ? "target_has_unknown_proofs"
        : PathConditionCount != 0 ? "target_has_path_conditions" : "target_has_no_path_conditions";
    internal string ReasonCode => ProofUnknownCount != 0
        ? "SP-SYM-TARGET-PROOF-UNKNOWN"
        : PathConditionCount != 0 ? "SP-SYM-TARGET-PATH-CONDITIONS" : "SP-SYM-TARGET-NO-PATH-CONDITIONS";
    internal string Summary => ProofUnknownCount != 0
        ? "This target has unresolved bounded-SMT outcomes."
        : PathConditionCount != 0
            ? "This target has source-location path conditions available for invariant queries."
            : "No path conditions were recorded for this target.";
}

internal sealed record SymbolicInvariantQueryDiagnostic(
    string Code,
    string Severity,
    string Message,
    int Count,
    IReadOnlyList<string> Evidence,
    int EvidenceTotalCount,
    bool EvidenceTruncated) {
    internal const int DefaultMaxEvidence = 8;
}

internal sealed class SymbolicInvariantQueryView {
    private SymbolicInvariantQueryView(
        SymbolicInvariantResult invariant,
        SymbolicMergedPathFacts facts,
        SymbolicQueryMetrics metrics,
        SymbolicSmtDiagnostics smt,
        IReadOnlyList<SymbolicProgramPointResult> points) {
        Text = facts.MergedInvariantText;
        MergeKind = invariant.MergeKind;
        MustFacts = facts.AlwaysFacts;
        MaybeFacts = facts.MaybeFacts;
        UnknownFacts = facts.ConservativeUnknowns;
        UnknownDiagnostics = facts.ConservativeUnknownDiagnostics;
        CandidateProgramPointCount = facts.CandidateProgramPointCount;
        UnreachableProgramPointCount = facts.UnreachableProgramPointCount;
        IsUnreachable = facts.IsUnreachable;
        Metrics = metrics;
        SmtDiagnostics = smt;
        TargetSummaries = BuildTargetSummaries(invariant, facts);
        TargetPathSummaries = BuildTargetPaths(points);
        Diagnostics = BuildDiagnostics();
        Status = ResolveStatus();
        StatusReason = ResolveStatusReason();
        Summary = Status switch {
            SymbolicInvariantQueryStatus.Unreachable => "All candidate program points are unreachable.",
            SymbolicInvariantQueryStatus.Unresolved => "Invariant analysis has unresolved outcomes.",
            SymbolicInvariantQueryStatus.Conservative => "Invariant analysis contains path-specific facts.",
            _ => "Invariant analysis is exact."
        };
    }

    internal string Text { get; }
    internal SymbolicInvariantMergeKind MergeKind { get; }
    internal IReadOnlyList<string> MustFacts { get; }
    internal int MustFactCount => MustFacts.Count;
    internal IReadOnlyList<string> MaybeFacts { get; }
    internal int MaybeFactCount => MaybeFacts.Count;
    internal IReadOnlyList<string> UnknownFacts { get; }
    internal int UnknownFactCount => UnknownFacts.Count;
    internal IReadOnlyList<SymbolicConservativeUnknownDiagnostic> UnknownDiagnostics { get; }
    internal IReadOnlyList<SymbolicInvariantTargetSummary> TargetSummaries { get; }
    internal int TargetSummaryCount => TargetSummaries.Count;
    internal IReadOnlyList<SymbolicInvariantTargetPathSummary> TargetPathSummaries { get; }
    internal int TargetPathSummaryCount => TargetPathSummaries.Count;
    internal int CandidateProgramPointCount { get; }
    internal int UnreachableProgramPointCount { get; }
    internal bool IsUnreachable { get; }
    internal SymbolicQueryMetrics Metrics { get; }
    internal SymbolicSmtDiagnostics SmtDiagnostics { get; }
    internal IReadOnlyList<SymbolicInvariantQueryDiagnostic> Diagnostics { get; }
    internal int DiagnosticCount => Diagnostics.Count;
    internal SymbolicInvariantQueryStatus Status { get; }
    internal string StatusReason { get; }
    internal string Summary { get; }
    internal bool HasMaybeFacts => MaybeFacts.Count != 0;
    internal bool HasUnknowns => UnknownFacts.Count != 0;
    internal bool HasUnresolvedAnalysis => HasUnknowns || Metrics.ReachabilityUnknownCount != 0 ||
        Metrics.ReachabilityNotCheckedCount != 0 || Metrics.ProofUnknownCount != 0;

    internal static SymbolicInvariantQueryView From(SymbolicQueryResult result) => new(
        result.MergedInvariant,
        result.MergedPathFacts,
        result.Metrics,
        result.SmtDiagnostics,
        result.ProgramPoints);

    internal static SymbolicInvariantQueryView From(SymbolicProgramPointResult point) {
        var points = new[] { point };
        return new SymbolicInvariantQueryView(
            point.Invariant,
            SymbolicMergedPathFacts.FromProgramPoints(points),
            SymbolicQueryMetrics.FromProgramPoints(points),
            point.SmtDiagnostics,
            points);
    }

    internal static IReadOnlyList<string> SelectFacts(
        IReadOnlyList<string> facts,
        IReadOnlyList<SymbolicInvariantTargetSummary> targets,
        IReadOnlyList<string> filters,
        Func<SymbolicInvariantTargetSummary, IReadOnlyList<string>> selector) => filters.Count == 0
        ? facts
        : targets.SelectMany(selector).Distinct(StringComparer.Ordinal).ToArray();

    internal IReadOnlyList<string> GetMatchedTargets(IReadOnlyList<string> filters) {
        var available = TargetSummaries.Select(static value => value.Target)
            .Concat(TargetPathSummaries.Select(static value => value.Target))
            .Select(SymbolicInvariantTargetFilter.NormalizeTarget)
            .ToHashSet(StringComparer.Ordinal);
        return filters.Select(SymbolicInvariantTargetFilter.NormalizeTarget)
            .Where(available.Contains).Distinct(StringComparer.Ordinal).ToArray();
    }

    private SymbolicInvariantQueryStatus ResolveStatus() {
        if (IsUnreachable) return SymbolicInvariantQueryStatus.Unreachable;
        if (Metrics.ReachabilityUnknownCount != 0 || Metrics.ReachabilityNotCheckedCount != 0 ||
            Metrics.ProofUnknownCount != 0 || SmtDiagnostics.IsConfigured && !SmtDiagnostics.IsEnabled)
            return SymbolicInvariantQueryStatus.Unresolved;
        return HasMaybeFacts || HasUnknowns
            ? SymbolicInvariantQueryStatus.Conservative
            : SymbolicInvariantQueryStatus.Exact;
    }

    private string ResolveStatusReason() => Status switch {
        SymbolicInvariantQueryStatus.Unreachable => "all_candidate_program_points_unreachable",
        SymbolicInvariantQueryStatus.Unresolved => "analysis_not_fully_resolved",
        SymbolicInvariantQueryStatus.Conservative => HasUnknowns
            ? "path_varying_targets"
            : "path_specific_facts",
        _ => "all_candidate_program_points_exact"
    };

    private IReadOnlyList<SymbolicInvariantQueryDiagnostic> BuildDiagnostics() {
        var diagnostics = new List<SymbolicInvariantQueryDiagnostic>();
        if (IsUnreachable)
            diagnostics.Add(Diagnostic("SP-SYM-UNREACHABLE", "Info",
                "No reachable candidate program points contributed invariant facts.",
                UnreachableProgramPointCount, new[] { $"UnreachableProgramPoints={UnreachableProgramPointCount}" }));
        if (MaybeFacts.Count != 0)
            diagnostics.Add(Diagnostic("SP-SYM-MAYBE-FACTS", "Info",
                "Some path facts are present on only a subset of candidate program points.",
                MaybeFacts.Count, MaybeFacts));
        if (UnknownFacts.Count != 0)
            diagnostics.Add(Diagnostic("SP-SYM-CONSERVATIVE-UNKNOWN", "Warning",
                "The merged invariant contains conservative unknown placeholders for path-varying targets.",
                UnknownFacts.Count, UnknownFacts));
        if (Metrics.ReachabilityUnknownCount != 0 || Metrics.ReachabilityNotCheckedCount != 0)
            diagnostics.Add(Diagnostic("SP-SYM-REACHABILITY", "Warning",
                "Some program point reachability checks are unknown or were not requested.",
                Metrics.ReachabilityUnknownCount + Metrics.ReachabilityNotCheckedCount,
                new[]
                {
                    $"Unknown={Metrics.ReachabilityUnknownCount}",
                    $"NotChecked={Metrics.ReachabilityNotCheckedCount}"
                }));
        if (Metrics.ProofUnknownCount != 0)
            diagnostics.Add(Diagnostic("SP-SYM-PROOF-UNKNOWN", "Warning",
                "Some requested implication proofs were not resolved by bounded SMT.",
                Metrics.ProofUnknownCount, new[] { $"UnknownProofs={Metrics.ProofUnknownCount}" }));
        if (SmtDiagnostics.IsConfigured && !SmtDiagnostics.IsEnabled)
            diagnostics.Add(Diagnostic("SP-SYM-SMT-DISABLED", "Warning",
                "SMT is configured but disabled, so solver-backed analysis is conservative.",
                1, new[] { "Mode=" + SmtDiagnostics.Mode }));
        return diagnostics;
    }

    private static SymbolicInvariantQueryDiagnostic Diagnostic(
        string code, string severity, string message, int count, IEnumerable<string> evidence) {
        var all = evidence.Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).ToArray();
        var shown = all.Take(SymbolicInvariantQueryDiagnostic.DefaultMaxEvidence).ToArray();
        return new SymbolicInvariantQueryDiagnostic(
            code, severity, message, count, shown, all.Length, shown.Length != all.Length);
    }

    private static IReadOnlyList<SymbolicInvariantTargetSummary> BuildTargetSummaries(
        SymbolicInvariantResult invariant,
        SymbolicMergedPathFacts facts) {
        var values = new Dictionary<string, TargetFacts>(StringComparer.Ordinal);
        foreach (var condition in invariant.Conditions) {
            var target = SymbolicInvariantTargetFilter.NormalizeTarget(condition.Target);
            var item = Get(values, target);
            (condition.IsConservativeUnknown ? item.Unknown : item.Must).Add(condition.Text);
        }
        foreach (var diagnostic in facts.ConservativeUnknownDiagnostics) {
            var item = Get(values, SymbolicInvariantTargetFilter.NormalizeTarget(diagnostic.Target));
            item.Unknown.Add(diagnostic.UnknownText);
            item.Maybe.AddRange(diagnostic.MaybeFacts);
        }
        return values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new SymbolicInvariantTargetSummary(
                pair.Key,
                Distinct(pair.Value.Must),
                Distinct(pair.Value.Maybe),
                Distinct(pair.Value.Unknown)))
            .ToArray();
    }

    private static IReadOnlyList<SymbolicInvariantTargetPathSummary> BuildTargetPaths(
        IReadOnlyList<SymbolicProgramPointResult> points) {
        var values = new Dictionary<string, TargetPath>(StringComparer.Ordinal);
        foreach (var point in points) {
            var pointTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var condition in point.Invariant.Conditions) {
                var target = SymbolicInvariantTargetFilter.NormalizeTarget(condition.Target);
                var item = Get(values, target);
                item.Add(condition);
                pointTargets.Add(target);
            }
            foreach (var proof in point.ConditionProofs) {
                var target = SymbolicInvariantTargetFilter.NormalizeTarget(proof.Target);
                Get(values, target).Add(proof);
                pointTargets.Add(target);
            }
            foreach (var target in pointTargets) Get(values, target).Add(point.Reachability);
        }
        return values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Value.Create(pair.Key)).ToArray();
    }

    private static TValue Get<TValue>(IDictionary<string, TValue> values, string key)
        where TValue : new() {
        if (!values.TryGetValue(key, out var value)) values.Add(key, value = new TValue());
        return value;
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> values) =>
        values.Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).ToArray();

    private sealed class TargetFacts {
        internal List<string> Must { get; } = new();
        internal List<string> Maybe { get; } = new();
        internal List<string> Unknown { get; } = new();
    }

    private sealed class TargetPath {
        private const int MaxConditions = 8;
        private readonly List<string> _conditions = new();
        private int _pathCount, _smtCount, _unknownConditions, _points, _reachable;
        private int _proofs, _proofUnknown, _proofTrue, _proofFalse, _proofUnreachable;

        internal void Add(SymbolicInvariantCondition condition) {
            _pathCount++;
            if (condition.IsSolverBacked) _smtCount++;
            if (condition.IsConservativeUnknown) _unknownConditions++;
            if (_conditions.Count < MaxConditions && !_conditions.Contains(condition.Text, StringComparer.Ordinal))
                _conditions.Add(condition.Text);
        }

        internal void Add(SymbolicConditionProofResult proof) {
            _proofs++;
            switch (proof.TruthValue) {
                case SymbolicTruthValue.Unknown: _proofUnknown++; break;
                case SymbolicTruthValue.ProvenTrue: _proofTrue++; break;
                case SymbolicTruthValue.ProvenFalse: _proofFalse++; break;
                case SymbolicTruthValue.Unreachable: _proofUnreachable++; break;
            }
        }

        internal void Add(SymbolicReachability reachability) {
            _points++;
            if (reachability == SymbolicReachability.Reachable) _reachable++;
        }

        internal SymbolicInvariantTargetPathSummary Create(string target) => new(
            target, _pathCount, _smtCount, _unknownConditions, _points, _reachable,
            _proofs, _proofUnknown, _proofTrue, _proofFalse, _proofUnreachable,
            _conditions.ToArray(), _pathCount > _conditions.Count);
    }
}
