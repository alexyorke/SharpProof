namespace SharpProof.Symbolic.Ir;
internal static class SymbolicOperationTransferKernel {
    internal static bool TryApply(ref SymbolicState state, SymbolicStateDelta delta) {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (delta == null) throw new ArgumentNullException(nameof(delta));
        var candidate = state;
        ApplyInvalidations(ref candidate, delta.Invalidations);
        if (!TryApplyBindings(ref candidate, delta.Bindings, delta.Origin))
            return false;
        if (!delta.Assumptions.IsDefaultOrEmpty)
            foreach (var assumption in delta.Assumptions)
                candidate = candidate.AddPathCondition(assumption);
        foreach (var binding in delta.Bindings)
            if (binding.DeriveIntegerBounds)
                AddDerivedIntegerBounds(ref candidate, binding, delta.Origin);
        state = candidate.Normalize();
        return true;
    }
    internal static SymbolicState Invalidate(
        SymbolicState state,
        ImmutableArray<SymbolicInvalidationTarget> targets,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan,
        string provenance) {
        var operation = new SymbolicStateDelta(
            [],
            new SymbolicOperationOrigin(sourceSpan, provenance),
            Invalidations: targets);
        TryApply(ref state, operation);
        return state;
    }
    internal static SymbolicState AssumeAll(
        SymbolicState state,
        IReadOnlyList<SymbolicCondition> conditions) {
        foreach (var condition in conditions)
            state = state.AddPathCondition(condition);
        return state.Normalize();
    }
    private static void ApplyInvalidations(ref SymbolicState state, ImmutableArray<SymbolicInvalidationTarget> targets) {
        if (targets.IsDefaultOrEmpty) return;
        foreach (var target in targets) {
            state = target.MatchKind switch {
                SymbolicInvalidationMatchKind.VariablePrefix =>
                    SymbolicIrReferenceScanner.RemoveVariableReferences(state, target.Key),
                SymbolicInvalidationMatchKind.VariableOrMember =>
                    SymbolicIrReferenceScanner.RemoveVariableOrMemberReferences(state, target.Key),
                SymbolicInvalidationMatchKind.VariableDescendants =>
                    SymbolicIrReferenceScanner.RemoveVariableDescendantReferences(state, target.Key),
                _ => state
            };
            if (target.DefinitionVersion is { } definitionVersion)
                state = state.WithSymbolVersion(target.Key, definitionVersion);
        }
    }
    internal static SymbolicState PropagateSourceFacts(SymbolicState state, SymbolicTerm source, SymbolicTerm target) {
        if (!SymbolicStateFactBuilder.CanCompareIrTerms(source, target) ||
            string.Equals(SymbolicState.CreateProofTermKey(source), SymbolicState.CreateProofTermKey(target), StringComparison.Ordinal))
            return state;
        var facts = state.Facts;
        var conditions = state.PathConditions;
        foreach (var fact in facts) {
            var substituted = SymbolicIrSubstitution.ReplaceTerm(fact, source, target);
            if (!string.Equals(
                    SymbolicState.CreateProofFactKey(substituted),
                    SymbolicState.CreateProofFactKey(fact),
                    StringComparison.Ordinal))
                state = state.AddFact(substituted);
        }
        foreach (var condition in conditions) {
            var substituted = SymbolicIrSubstitution.ReplaceTerm(condition, source, target);
            if (!string.Equals(
                    SymbolicState.CreateProofConditionKey(substituted),
                    SymbolicState.CreateProofConditionKey(condition),
                    StringComparison.Ordinal))
                state = state.AddPathCondition(substituted);
        }
        return state;
    }
    private static bool TryApplyBindings(
        ref SymbolicState state,
        ImmutableArray<SymbolicAssignmentBinding> bindings,
        SymbolicOperationOrigin origin) {
        foreach (var binding in bindings) {
            if (binding.Source == null ||
                !SymbolicStateFactBuilder.CanCompareIrTerms(binding.Target, binding.Source))
                return false;
            if (binding.InvalidateTarget)
                state = SymbolicStateValueFacts.RemoveReferences(state, binding.TargetKey);
            state = state.AddPathCondition(new SymbolicFactCondition(new SymbolicFact(
                new SymbolicRelationAtom(SymbolicRelationOperator.Equal, binding.Target, binding.Source),
                true,
                SymbolicFactConfidence.Exact,
                binding.Provenance ?? origin.Provenance + ".value",
                origin.SourceSpan,
                null,
                binding.EvidenceKey)));
            if (binding.PropagateSourceFacts)
                state = PropagateSourceFacts(state, binding.Source, binding.Target);
        }
        return true;
    }
    private static void AddDerivedIntegerBounds(ref SymbolicState state, SymbolicAssignmentBinding binding,
        SymbolicOperationOrigin origin) {
        if (binding.Target.Kind != SmtValueKind.Int || binding.Source is not { Kind: SmtValueKind.Int } source)
            return;
        if (StateProvesIntegerBound(state, source, strictlyPositive: true))
            AddIntegerBound(
                ref state,
                binding.Target,
                SymbolicRelationOperator.GreaterThan,
                new SymbolicIntegerConstantTerm(0),
                origin,
                ".assigned-integer.positive");
        else if (StateProvesIntegerBound(state, source, strictlyPositive: false))
            AddIntegerBound(
                ref state,
                binding.Target,
                SymbolicRelationOperator.GreaterThanOrEqual,
                new SymbolicIntegerConstantTerm(0),
                origin,
                ".assigned-integer.non-negative");
        if (source is not SymbolicBinaryTerm {
            Operator: SymbolicBinaryTermOperator.Remainder,
            Left: { Kind: SmtValueKind.Int } dividend,
            Right: { Kind: SmtValueKind.Int } divisor
        } ||
            !StateProvesIntegerBound(state, dividend, strictlyPositive: false) ||
            !StateProvesIntegerBound(state, divisor, strictlyPositive: true))
            return;
        AddIntegerBound(
            ref state,
            binding.Target,
            SymbolicRelationOperator.GreaterThanOrEqual,
            new SymbolicIntegerConstantTerm(0),
            origin,
            ".assigned-remainder.non-negative");
        AddIntegerBound(ref state, binding.Target, SymbolicRelationOperator.LessThan, divisor, origin, ".assigned-remainder.upper-bound");
    }
    private static bool StateProvesIntegerBound(SymbolicState state, SymbolicTerm term, bool strictlyPositive)
        => state.PathConditions.Any(condition => ConditionProvesIntegerBound(condition, term, strictlyPositive)) ||
               state.Facts.Any(fact => FactProvesIntegerBound(fact, term, strictlyPositive));
    private static bool ConditionProvesIntegerBound(SymbolicCondition condition, SymbolicTerm term, bool strictlyPositive)
        => condition switch {
            SymbolicFactCondition fact => FactProvesIntegerBound(fact.Fact, term, strictlyPositive),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } binary =>
                ConditionProvesIntegerBound(binary.Left, term, strictlyPositive) ||
                ConditionProvesIntegerBound(binary.Right, term, strictlyPositive),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } binary =>
                ConditionProvesIntegerBound(binary.Left, term, strictlyPositive) &&
                ConditionProvesIntegerBound(binary.Right, term, strictlyPositive),
            _ => false
        };
    private static bool FactProvesIntegerBound(SymbolicFact fact, SymbolicTerm term, bool strictlyPositive) {
        if (!fact.Polarity || fact.Atom is not SymbolicRelationAtom relation) return false;
        return Equals(relation.Left, term) && relation.Right is SymbolicIntegerConstantTerm right
            ? RelationProvesIntegerBound(relation.Operator, right.Value, strictlyPositive, termOnLeft: true)
            : Equals(relation.Right, term) && relation.Left is SymbolicIntegerConstantTerm left &&
              RelationProvesIntegerBound(relation.Operator, left.Value, strictlyPositive, termOnLeft: false);
    }
    private static bool RelationProvesIntegerBound(SymbolicRelationOperator relation, long constant, bool strictlyPositive,
        bool termOnLeft) => (termOnLeft, relation) switch {
            (true, SymbolicRelationOperator.GreaterThan) => strictlyPositive ? constant >= 0 : constant >= -1,
            (true, SymbolicRelationOperator.GreaterThanOrEqual) => strictlyPositive ? constant > 0 : constant >= 0,
            (true, SymbolicRelationOperator.Equal) => strictlyPositive ? constant > 0 : constant >= 0,
            (false, SymbolicRelationOperator.LessThan) => strictlyPositive ? constant <= 0 : constant <= -1,
            (false, SymbolicRelationOperator.LessThanOrEqual) => strictlyPositive ? constant < 0 : constant <= 0,
            (false, SymbolicRelationOperator.Equal) => strictlyPositive ? constant > 0 : constant >= 0,
            _ => false
        };
    private static void AddIntegerBound(
        ref SymbolicState state,
        SymbolicTerm left,
        SymbolicRelationOperator relation,
        SymbolicTerm right,
        SymbolicOperationOrigin origin,
        string provenanceSuffix) => state = state.AddPathCondition(new SymbolicFactCondition(new SymbolicFact(
            new SymbolicRelationAtom(relation, left, right),
            true,
            SymbolicFactConfidence.Exact,
            origin.Provenance + provenanceSuffix,
            origin.SourceSpan,
            null,
            null)));
}
