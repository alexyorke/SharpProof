namespace SharpProof.Symbolic;

internal static class SymbolicProofStateFacts {
    internal static SymbolicState NormalizeState(SymbolicState state) =>
        state.Normalize();

    internal static SymbolicCondition RewriteQueryConditionToCurrentVersions(SymbolicCondition condition,
        SymbolicState state) {
        return state.SymbolVersions.Count == 0
            ? condition
            : SymbolicIrVersionRewriter.RewriteToCurrentVersions(condition, state.SymbolVersions);
    }

    internal static SymbolicFact RewriteQueryFactToCurrentVersions(SymbolicFact fact, SymbolicState state) {
        return state.SymbolVersions.Count == 0
            ? fact
            : SymbolicIrVersionRewriter.RewriteToCurrentVersions(fact, state.SymbolVersions);
    }

    internal static bool TryClassifySyntacticConditionTruth(
        SymbolicCondition condition,
        out SymbolicProofStatus status) {
        switch (SymbolicState.CreateProofConditionKey(condition)) {
            case "const:true":
                status = SymbolicProofStatus.ProvenTrue;
                return true;
            case "const:false":
                status = SymbolicProofStatus.ProvenFalse;
                return true;
            default:
                status = SymbolicProofStatus.Unknown;
                return false;
        }
    }

    internal static bool StateContainsFact(SymbolicState state, SymbolicFact fact) {
        var factKey = SymbolicState.CreateProofFactKey(fact);
        var factConditionKey = "fact-condition:" + factKey;
        return state.Facts.Any(candidate => string.Equals(
                   SymbolicState.CreateProofFactKey(candidate),
                   factKey,
                   StringComparison.Ordinal)) ||
               state.PathConditions.Any(candidate =>
                   string.Equals(
                       SymbolicState.CreateProofConditionKey(candidate),
                       factConditionKey,
                       StringComparison.Ordinal) ||
                   SymbolicState.EnumerateProofConditionFactKeys(candidate).Any(conditionFactKey => string.Equals(
                       conditionFactKey,
                       factKey,
                       StringComparison.Ordinal)));
    }

    internal static bool StateContradictsFact(SymbolicState state, SymbolicFact fact) =>
        StateContainsFact(state, fact.Negate());

    internal static bool StateContainsCondition(SymbolicState state, SymbolicCondition condition) {
        if (TryEvaluateConditionFromState(state, condition, out var value)) return value;

        if (condition is SymbolicFactCondition factCondition &&
            StateContainsFact(state, factCondition.Fact))
            return true;

        var conditionKey = SymbolicState.CreateProofConditionKey(condition);
        return state.Facts.Any(candidate => string.Equals(
                   "fact-condition:" + SymbolicState.CreateProofFactKey(candidate),
                   conditionKey,
                   StringComparison.Ordinal)) ||
               state.PathConditions.Any(candidate => string.Equals(
                   SymbolicState.CreateProofConditionKey(candidate),
                   conditionKey,
                   StringComparison.Ordinal));
    }

    internal static bool StateContradictsCondition(SymbolicState state, SymbolicCondition condition) {
        if (TryEvaluateConditionFromState(state, condition, out var value)) return !value;

        return StateContainsCondition(state, new SymbolicNotCondition(condition));
    }

    internal static bool TryEvaluateConditionFromState(
        SymbolicState state,
        SymbolicCondition condition,
        out bool value) {
        return TryEvaluateConditionFromState(
            state,
            condition,
            new Dictionary<string, bool>(StringComparer.Ordinal),
            out value);
    }

    internal static bool TryEvaluateConditionFromState(
        SymbolicState state,
        SymbolicCondition condition,
        IDictionary<string, bool> memo,
        out bool value) {
        var conditionKey = SymbolicState.CreateProofConditionKey(condition);
        if (memo.TryGetValue(conditionKey, out value)) return true;

        if (state.Facts.Any(fact => string.Equals(
                "fact-condition:" + SymbolicState.CreateProofFactKey(fact),
                conditionKey,
                StringComparison.Ordinal)) ||
            state.PathConditions.Any(pathCondition => string.Equals(
                SymbolicState.CreateProofConditionKey(pathCondition),
                conditionKey,
                StringComparison.Ordinal))) {
            value = true;
            memo[conditionKey] = true;
            return true;
        }

        var negatedConditionKey = SymbolicState.CreateProofConditionKey(new SymbolicNotCondition(condition));
        if (state.PathConditions.Any(pathCondition => string.Equals(
                SymbolicState.CreateProofConditionKey(pathCondition),
                negatedConditionKey,
                StringComparison.Ordinal))) {
            value = false;
            memo[conditionKey] = false;
            return true;
        }

        switch (condition) {
            case SymbolicConstantCondition constant:
                value = constant.Value;
                memo[conditionKey] = value;
                return true;
            case SymbolicFactCondition factCondition:
                if (StateContainsFact(state, factCondition.Fact)) {
                    value = true;
                    memo[conditionKey] = value;
                    return true;
                }

                if (StateContradictsFact(state, factCondition.Fact)) {
                    value = false;
                    memo[conditionKey] = value;
                    return true;
                }

                break;
            case SymbolicNotCondition notCondition:
                if (TryEvaluateConditionFromState(state, notCondition.Operand, memo, out var operandValue)) {
                    value = !operandValue;
                    memo[conditionKey] = value;
                    return true;
                }

                break;
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } andCondition:
                var leftAndKnown = TryEvaluateConditionFromState(state, andCondition.Left, memo, out var leftAndValue);
                var rightAndKnown =
                    TryEvaluateConditionFromState(state, andCondition.Right, memo, out var rightAndValue);
                if ((leftAndKnown && !leftAndValue) ||
                    (rightAndKnown && !rightAndValue)) {
                    value = false;
                    memo[conditionKey] = value;
                    return true;
                }

                if (leftAndKnown && rightAndKnown) {
                    value = leftAndValue && rightAndValue;
                    memo[conditionKey] = value;
                    return true;
                }

                break;
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } orCondition:
                var leftOrKnown = TryEvaluateConditionFromState(state, orCondition.Left, memo, out var leftOrValue);
                var rightOrKnown = TryEvaluateConditionFromState(state, orCondition.Right, memo, out var rightOrValue);
                if ((leftOrKnown && leftOrValue) ||
                    (rightOrKnown && rightOrValue)) {
                    value = true;
                    memo[conditionKey] = value;
                    return true;
                }

                if (leftOrKnown && rightOrKnown) {
                    value = leftOrValue || rightOrValue;
                    memo[conditionKey] = value;
                    return true;
                }

                break;
        }

        value = false;
        return false;
    }
}
