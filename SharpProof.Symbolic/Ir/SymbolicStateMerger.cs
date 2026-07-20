namespace SharpProof.Symbolic.Ir;

internal static class SymbolicStateMerger {
    internal readonly record struct GuardedState(SymbolicCondition Condition, SymbolicState State);

    internal static ImmutableArray<SymbolicCondition> MergePathConditionsAcrossAll(
        IReadOnlyList<SymbolicState> states) => MergePathConditionsAcrossAll(
            states.Select(static state => (IReadOnlyList<SymbolicCondition>)state.PathConditions).ToArray());

    internal static ImmutableArray<SymbolicCondition> MergePathConditionsAcrossAll(
        IReadOnlyList<IReadOnlyList<SymbolicCondition>> conditionSets) {
        var limits = SymbolicAnalysisLimitContext.Limits;
        return PathConditionMergeEngine.MergeAcrossAll(
            conditionSets,
            new PathConditionMergeLimits(
                limits.MaxMergedPathConditions,
                limits.MaxMergeableFactsPerTargetPerState,
                limits.MaxFactChoiceCombinationsPerTarget,
                limits.MaxGuardFactsPerTargetPerState));
    }

    internal static SymbolicCondition CreateGuardedChoice(
        SymbolicCondition guard,
        SymbolicCondition value) =>
        value is SymbolicConstantCondition { Value: false }
            ? new SymbolicNotCondition(guard)
            : new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicNotCondition(guard),
                value);

    internal static SymbolicState MergeCommonStates(
        SymbolicState baseline,
        IReadOnlyList<SymbolicState> states) {
        if (states.Count == 0) return baseline;

        var conditionKeys = GetCommonConditionKeys(states);
        return new SymbolicState(
            baseline.Facts.Concat(IntersectFactsAcrossAll(states)),
            baseline.PathConditions.Concat(states[0].PathConditions.Where(condition =>
                conditionKeys.Contains(SymbolicState.CreateProofConditionKey(condition)))),
            baseline.SymbolVersions,
            baseline.IsContradictory);
    }

    internal static SymbolicState MergeGuardedStates(
        SymbolicState baseline,
        IReadOnlyList<GuardedState> branches,
        SyntaxNode source,
        SymbolicAnalysisLimitKind limitKind,
        int limit,
        string provenance) {
        var states = branches.Select(static branch => branch.State).ToArray();
        var commonFactKeys = new HashSet<string>(
            IntersectFactsAcrossAll(states).Select(SymbolicState.CreateProofFactKey),
            StringComparer.Ordinal);
        var commonConditionKeys = GetCommonConditionKeys(states);
        var state = MergeCommonStates(baseline, states);
        var addedCount = 0;
        foreach (var branch in branches) {
            foreach (var fact in branch.State.Facts)
                if (!commonFactKeys.Contains(SymbolicState.CreateProofFactKey(fact)) &&
                    !TryAddGuardedFact(ref state, branch.Condition, new SymbolicFactCondition(fact)))
                    return state;

            var branchConditionKey = SymbolicState.CreateProofConditionKey(branch.Condition);
            foreach (var condition in branch.State.PathConditions) {
                var key = SymbolicState.CreateProofConditionKey(condition);
                if (commonConditionKeys.Contains(key) || key == branchConditionKey) continue;
                if (!TryAddGuardedFact(ref state, branch.Condition, condition)) return state;
            }
        }

        return state;

        bool TryAddGuardedFact(
            ref SymbolicState target,
            SymbolicCondition branchCondition,
            SymbolicCondition branchFact) {
            if (addedCount >= limit) {
                SymbolicAnalysisLimitContext.Record(
                    limitKind, limit, addedCount + 1, source, provenance);
                return false;
            }

            target = target.AddPathCondition(CreateGuardedChoice(branchCondition, branchFact));
            addedCount++;
            return true;
        }
    }

    private static HashSet<string> GetCommonConditionKeys(IReadOnlyList<SymbolicState> states) {
        var keys = new HashSet<string>(
            states[0].PathConditions.Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);
        foreach (var state in states.Skip(1))
            keys.IntersectWith(state.PathConditions.Select(SymbolicState.CreateProofConditionKey));
        return keys;
    }

    internal static ImmutableArray<SymbolicFact> IntersectFactsAcrossAll(
        IReadOnlyList<SymbolicState> states,
        Func<SymbolicFact, SymbolicFact, bool>? equivalent = null) {
        if (states.Count == 0) return ImmutableArray<SymbolicFact>.Empty;

        var common = states[0].Facts;
        for (var index = 1; index < states.Count && !common.IsEmpty; index++) {
            var candidateFacts = states[index].Facts;
            common = common.Where(fact => candidateFacts.Any(candidate => equivalent?.Invoke(fact, candidate) ??
                SymbolicState.CreateProofFactKey(fact) == SymbolicState.CreateProofFactKey(candidate))).ToImmutableArray();
        }

        return common;
    }

    internal static SymbolicState MergePathStatesAcrossAll(
        IReadOnlyList<SymbolicState> states,
        Func<SymbolicFact, SymbolicFact, bool> equivalent,
        int phiScope) {
        if (states.Count == 0) return new SymbolicState();

        var versions = MergePhiVersions(states, phiScope);
        var normalized = states.Select(state => RewriteToVersions(state, versions)).ToArray();
        var facts = MergeResourceStateFacts(IntersectFactsAcrossAll(normalized, equivalent), normalized);
        return new SymbolicState(facts, MergePathConditionsAcrossAll(normalized), versions);
    }

    private static ImmutableDictionary<string, int> MergePhiVersions(
        IReadOnlyList<SymbolicState> states,
        int phiScope) {
        var keys = states.SelectMany(static state => state.SymbolVersions.Keys)
            .Distinct(StringComparer.Ordinal);
        var builder = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var key in keys) {
            var versions = states.Select(state => state.SymbolVersions.TryGetValue(key, out var version) ? version : 0)
                .Distinct()
                .Take(2)
                .ToArray();
            builder[key] = versions.Length == 1 ? versions[0] : checked(phiScope * 2 + 1);
        }

        return builder.ToImmutable();
    }

    private static SymbolicState RewriteToVersions(
        SymbolicState state,
        ImmutableDictionary<string, int> versions) =>
        new(
            state.Facts.Select(fact => SymbolicIrVersionRewriter.RewriteToCurrentVersions(fact, versions)),
            state.PathConditions.Select(condition =>
                SymbolicIrVersionRewriter.RewriteToCurrentVersions(condition, versions)),
            versions,
            state.IsContradictory);

    internal static bool AreEvidenceEquivalentFacts(SymbolicFact first, SymbolicFact second) =>
        first.Polarity == second.Polarity &&
        first.Confidence == second.Confidence &&
        Equals(first.Atom, second.Atom) &&
        SymbolEqualityComparer.Default.Equals(first.Symbol, second.Symbol) &&
        string.Equals(first.EvidenceKey, second.EvidenceKey, StringComparison.Ordinal);

    internal static IEnumerable<SymbolicTerm> EnumerateExactAliasComponent(
        SymbolicTerm root,
        IReadOnlyList<SymbolicFact> facts) {
        var pending = new Queue<SymbolicTerm>();
        var visited = new HashSet<SymbolicTerm>();
        pending.Enqueue(root);
        while (pending.Count != 0) {
            var term = pending.Dequeue();
            if (!visited.Add(term)) continue;
            yield return term;
            foreach (var fact in facts) {
                if (!fact.Polarity || fact.Confidence != SymbolicFactConfidence.Exact ||
                    fact.Atom is not SymbolicAliasAtom { MayAlias: true } alias)
                    continue;
                if (Equals(alias.Target, term)) pending.Enqueue(alias.Source);
                if (Equals(alias.Source, term)) pending.Enqueue(alias.Target);
            }
        }
    }

    internal static bool ExactAliasComponentFactAny(
        SymbolicTerm root,
        IReadOnlyList<SymbolicFact> facts,
        Func<SymbolicFact, SymbolicTerm, bool> predicate) =>
        EnumerateExactAliasComponent(root, facts).Any(term => facts.Any(fact =>
            fact.Polarity && fact.Confidence == SymbolicFactConfidence.Exact && predicate(fact, term)));

    internal static bool HasExactResourceRelease(SymbolicState state, SymbolicTerm resource) =>
        ExactAliasComponentFactAny(resource, state.Facts, static (fact, term) =>
            TryGetExactResourceRelease(fact, out var released, out _) && Equals(released, term));

    private static ImmutableArray<SymbolicFact> MergeResourceStateFacts(
        ImmutableArray<SymbolicFact> commonFacts,
        IReadOnlyList<SymbolicState> states) {
        var builder = commonFacts.ToBuilder();
        var resources = new List<(SymbolicTerm Resource, ISymbol? Symbol)>();
        foreach (var fact in states.SelectMany(static state => state.Facts)) {
            if (!TryGetResourceStateIdentity(fact, out var resource, out var symbol) ||
                resources.Any(key => ResourceStateIdentityMatches(key.Resource, key.Symbol, resource, symbol)))
                continue;
            resources.Add((resource, symbol));
        }

        foreach (var (resource, symbol) in resources) {
            if (states.All(state => HasExactResourceRelease(state, resource, symbol))) {
                var representative = states
                    .SelectMany(state => state.Facts.Select(fact => (State: state, Fact: fact)))
                    .First(pair =>
                        TryGetExactResourceRelease(pair.Fact, out var released, out var releasedSymbol) &&
                        (ResourceStateIdentityMatches(resource, symbol, released, releasedSymbol) ||
                         EnumerateExactAliasComponent(resource, pair.State.Facts)
                             .Any(term => Equals(term, released))))
                    .Fact;
                var mergedFact = representative with {
                    Atom = new SymbolicResourceLifetimeAtom(resource, SymbolicResourceLifetimeState.Released),
                    Provenance = "analyzer.resource.merge.all-path-release",
                    EvidenceKey = representative.EvidenceKey ?? "evidence.resource.released",
                    Symbol = symbol ?? representative.Symbol
                };
                if (!builder.Any(fact => AreEvidenceEquivalentFacts(fact, mergedFact))) builder.Add(mergedFact);
                continue;
            }

            foreach (var outstanding in states.SelectMany(static state => state.Facts)
                         .Where(fact => IsOutstandingResourceFactFor(fact, resource, symbol)))
                if (!builder.Any(fact => AreEvidenceEquivalentFacts(fact, outstanding))) builder.Add(outstanding);
        }

        return builder.ToImmutable();
    }

    private static bool HasExactResourceRelease(SymbolicState state, SymbolicTerm resource, ISymbol? symbol) {
        var releasedResources = new HashSet<SymbolicTerm>();
        foreach (var release in EnumerateExactResourceReleases(state)) {
            if (ResourceStateIdentityMatches(resource, symbol, release.Resource, release.Symbol)) return true;
            releasedResources.Add(release.Resource);
        }

        return EnumerateExactAliasComponent(resource, state.Facts).Any(releasedResources.Contains);
    }

    private static bool TryGetResourceStateIdentity(
        SymbolicFact fact,
        out SymbolicTerm resource,
        out ISymbol? symbol) {
        if (TryGetExactResourceRelease(fact, out resource, out symbol)) return true;

        symbol = fact.Symbol;
        resource = fact.Atom switch {
            SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime =>
                lifetime.Resource,
            SymbolicDisposalAtom { State: SymbolicDisposalState.NotDisposed } disposal => disposal.Resource,
            _ => null!
        };
        if (resource != null) return true;
        symbol = null;
        return false;
    }

    private static bool IsOutstandingResourceFactFor(
        SymbolicFact fact,
        SymbolicTerm resource,
        ISymbol? symbol) {
        if (!fact.Polarity || fact.Confidence != SymbolicFactConfidence.Exact) return false;

        var outstanding = fact.Atom switch {
            SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime =>
                lifetime.Resource,
            SymbolicDisposalAtom { State: SymbolicDisposalState.NotDisposed } disposal => disposal.Resource,
            _ => null
        };
        return outstanding != null && ResourceStateIdentityMatches(resource, symbol, outstanding, fact.Symbol);
    }

    private static bool ResourceStateIdentityMatches(
        SymbolicTerm firstResource,
        ISymbol? firstSymbol,
        SymbolicTerm secondResource,
        ISymbol? secondSymbol) =>
        firstSymbol != null && secondSymbol != null
            ? SymbolEqualityComparer.Default.Equals(firstSymbol, secondSymbol)
            : Equals(firstResource, secondResource);

    private static IEnumerable<(SymbolicTerm Resource, ISymbol? Symbol)> EnumerateExactResourceReleases(
        SymbolicState state) {
        foreach (var fact in state.Facts)
            if (TryGetExactResourceRelease(fact, out var resource, out var symbol))
                yield return (resource, symbol);
    }

    private static bool TryGetExactResourceRelease(
        SymbolicFact fact,
        out SymbolicTerm resource,
        out ISymbol? symbol) {
        resource = fact.Atom switch {
            SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Released or SymbolicResourceLifetimeState.Returned } lifetime =>
                lifetime.Resource,
            SymbolicDisposalAtom { State: SymbolicDisposalState.Disposed } disposal => disposal.Resource,
            _ => null!
        };
        symbol = resource == null ? null : fact.Symbol;
        return resource != null && fact.Polarity && fact.Confidence == SymbolicFactConfidence.Exact;
    }

    internal static SymbolicState MergeCompletionStates(
        IReadOnlyList<SymbolicState> states,
        SymbolicState entryState,
        Microsoft.CodeAnalysis.SyntaxNode source) {
        if (states.Count == 1) return states[0];

        var retainedFacts = entryState.Facts.ToList();
        var retainedConditions = entryState.PathConditions.ToList();
        var addedCount = 0;
        AddLimitedCommonItems(
            IntersectFactsAcrossAll(states),
            retainedFacts,
            entryState.Facts.Select(SymbolicState.CreateProofFactKey),
            SymbolicState.CreateProofFactKey, source, ref addedCount);
        AddLimitedCommonItems(
            MergePathConditionsAcrossAll(states),
            retainedConditions,
            entryState.PathConditions.Select(SymbolicState.CreateProofConditionKey),
            SymbolicState.CreateProofConditionKey, source, ref addedCount);

        var commonVersions = states[0].SymbolVersions.Where(pair => states.Skip(1).All(state =>
            state.SymbolVersions.TryGetValue(pair.Key, out var version) && version == pair.Value));
        return new SymbolicState(
            retainedFacts,
            retainedConditions,
            commonVersions,
            states.All(static state => state.IsContradictory)).Normalize();
    }

    private static void AddLimitedCommonItems<T>(
        IEnumerable<T> candidates,
        ICollection<T> retained,
        IEnumerable<string> retainedKeys,
        Func<T, string> getKey,
        Microsoft.CodeAnalysis.SyntaxNode source,
        ref int addedCount) {
        var limit = SymbolicAnalysisLimitContext.Limits.MaxMergedTryFacts;
        var keys = new HashSet<string>(retainedKeys, StringComparer.Ordinal);
        foreach (var candidate in candidates.Where(candidate => keys.Add(getKey(candidate)))) {
            if (addedCount >= limit) {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.TryFactMerge, limit, addedCount + 1, source,
                    "program_point.try_fact_merge");
                return;
            }

            retained.Add(candidate);
            addedCount++;
        }
    }

    internal static SymbolicCondition Combine(
        SymbolicConditionOperator op,
        IReadOnlyList<SymbolicCondition> conditions) {
        var result = conditions[0];
        for (var index = 1; index < conditions.Count; index++)
            result = new SymbolicBinaryCondition(op, result, conditions[index]);

        return result;
    }

    internal static bool TryGetMergeTargetKey(SymbolicCondition condition, out string targetKey) {
        if (TryGetMergeTarget(condition, out var target)) {
            targetKey = SymbolicState.CreateProofTermKey(target);
            return true;
        }

        targetKey = string.Empty;
        return false;
    }

    private static bool TryGetMergeTarget(SymbolicCondition condition, out SymbolicTerm target) {
        if (condition is SymbolicNotCondition { Operand: { } operand }) condition = operand;

        if (condition is SymbolicFactCondition { Fact.Atom: SymbolicRelationAtom relation } &&
            (TryGetTargetTerm(relation.Left, out target) || TryGetTargetTerm(relation.Right, out target)))
            return true;

        if (condition is SymbolicFactCondition {
            Fact.Atom: SymbolicTruthAtom { Condition: SymbolicVariableTerm variable }
        }) {
            target = variable;
            return true;
        }

        target = null!;
        return false;
    }

    private static bool TryGetTargetTerm(SymbolicTerm term, out SymbolicTerm target) {
        switch (term) {
            case SymbolicVariableTerm:
            case SymbolicMemberTerm:
            case SymbolicElementTerm:
            case SymbolicMultiElementTerm:
            case SymbolicNullableHasValueTerm:
            case SymbolicNullableValueTerm:
            case SymbolicLengthTerm:
            case SymbolicArrayDimensionLengthTerm:
            case SymbolicCountTerm:
            case SymbolicStringContentTerm:
                target = term;
                return true;
            default:
                target = null!;
                return false;
        }
    }
}
