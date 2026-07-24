namespace SharpProof.Symbolic;
internal readonly struct ProofKey : IEquatable<ProofKey> {
    private readonly int _hashCode;
    internal ProofKey(string value) {
        Value = value;
        _hashCode = StringComparer.Ordinal.GetHashCode(value);
    }
    internal string Value { get; }
    public bool Equals(ProofKey other) =>
        _hashCode == other._hashCode && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is ProofKey other && Equals(other);
    public override int GetHashCode() => _hashCode;
    public override string ToString() => Value;
}
internal sealed class FactIndex {
    private readonly HashSet<ProofKey> _facts = [];
    private readonly HashSet<ProofKey> _conditions = [];
    internal FactIndex(ImmutableArray<SymbolicFact> facts, ImmutableArray<SymbolicCondition> conditions) {
        foreach (var fact in facts) {
            var key = SymbolicState.CreateProofFactIndexKey(fact);
            _facts.Add(key);
            _conditions.Add(SymbolicState.CreateFactConditionIndexKey(key));
        }
        foreach (var condition in conditions) {
            var key = SymbolicState.CreateProofConditionIndexKey(condition);
            _conditions.Add(key);
            foreach (var factKey in SymbolicState.EnumerateProofConditionFactIndexKeys(condition))
                _facts.Add(factKey);
        }
    }
    internal bool ContainsFact(SymbolicFact fact) =>
        _facts.Contains(SymbolicState.CreateProofFactIndexKey(fact));
    internal bool ContainsCondition(SymbolicCondition condition) =>
        _conditions.Contains(SymbolicState.CreateProofConditionIndexKey(condition));
    internal bool ContainsNegatedCondition(SymbolicCondition condition) =>
        ContainsCondition(new SymbolicNotCondition(condition));
}
internal static class SymbolicProofStateFacts {
    internal static SymbolicState NormalizeState(SymbolicState state) => state.Normalize();
    internal static SymbolicCondition RewriteQueryConditionToCurrentVersions(SymbolicCondition condition, SymbolicState state) =>
        state.SymbolVersions.Count == 0
            ? condition
            : SymbolicIrVersionRewriter.RewriteToCurrentVersions(condition, state.SymbolVersions);
    internal static SymbolicFact RewriteQueryFactToCurrentVersions(SymbolicFact fact, SymbolicState state) =>
        state.SymbolVersions.Count == 0
            ? fact
            : SymbolicIrVersionRewriter.RewriteToCurrentVersions(fact, state.SymbolVersions);
    internal static bool TryClassifySyntacticConditionTruth(
        SymbolicCondition condition,
        out SymbolicProofStatus status) {
        var key = SymbolicState.CreateProofConditionIndexKey(condition).Value;
        status = key == "const:true"
            ? SymbolicProofStatus.ProvenTrue
            : key == "const:false"
                ? SymbolicProofStatus.ProvenFalse
                : SymbolicProofStatus.Unknown;
        return status != SymbolicProofStatus.Unknown;
    }
    internal static bool StateContainsFact(SymbolicState state, SymbolicFact fact) =>
        state.ProofIndex.ContainsFact(fact);
    internal static bool StateContradictsFact(SymbolicState state, SymbolicFact fact) =>
        state.ProofIndex.ContainsFact(fact.Negate());
    internal static bool StateContainsCondition(SymbolicState state, SymbolicCondition condition) =>
        TryEvaluateConditionFromState(state, condition, out var value)
            ? value
            : state.ProofIndex.ContainsCondition(condition);
    internal static bool StateContradictsCondition(SymbolicState state, SymbolicCondition condition) =>
        TryEvaluateConditionFromState(state, condition, out var value)
            ? !value
            : state.ProofIndex.ContainsCondition(new SymbolicNotCondition(condition));
    internal static bool TryEvaluateConditionFromState(
        SymbolicState state,
        SymbolicCondition condition,
        out bool value) =>
        TryEvaluateConditionFromState(state, condition, new Dictionary<ProofKey, bool>(), out value);
    private static bool TryEvaluateConditionFromState(
        SymbolicState state,
        SymbolicCondition condition,
        IDictionary<ProofKey, bool> memo,
        out bool value) {
        var key = SymbolicState.CreateProofConditionIndexKey(condition);
        if (memo.TryGetValue(key, out value)) return true;
        if (state.ProofIndex.ContainsCondition(condition)) return Set(key, memo, true, out value);
        if (state.ProofIndex.ContainsNegatedCondition(condition)) return Set(key, memo, false, out value);
        switch (condition) {
            case SymbolicConstantCondition constant:
                return Set(key, memo, constant.Value, out value);
            case SymbolicFactCondition fact:
                if (StateContainsFact(state, fact.Fact)) return Set(key, memo, true, out value);
                if (StateContradictsFact(state, fact.Fact)) return Set(key, memo, false, out value);
                break;
            case SymbolicNotCondition not
                when TryEvaluateConditionFromState(state, not.Operand, memo, out var operand):
                return Set(key, memo, !operand, out value);
            case SymbolicBinaryCondition binary:
                var leftKnown = TryEvaluateConditionFromState(state, binary.Left, memo, out var left);
                var rightKnown = TryEvaluateConditionFromState(state, binary.Right, memo, out var right);
                if (binary.Operator == SymbolicConditionOperator.And) {
                    if (leftKnown && !left || rightKnown && !right) return Set(key, memo, false, out value);
                    if (leftKnown && rightKnown) return Set(key, memo, left && right, out value);
                }
                else {
                    if (leftKnown && left || rightKnown && right) return Set(key, memo, true, out value);
                    if (leftKnown && rightKnown) return Set(key, memo, left || right, out value);
                }
                break;
        }
        value = false;
        return false;
    }
    private static bool Set(ProofKey key, IDictionary<ProofKey, bool> memo, bool result, out bool value) {
        value = result;
        memo[key] = result;
        return true;
    }
}
