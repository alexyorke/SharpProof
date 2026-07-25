namespace SharpProof.Symbolic.Ir;
internal static class SymbolicIrReferenceScanner {
    internal static bool ContainsVariablePrefix(SymbolicFact fact, string prefix) =>
        Contains(fact, name => MatchesPrefix(name, prefix));
    internal static bool ContainsVariablePrefix(SymbolicCondition condition, string prefix) =>
        Contains(condition, name => MatchesPrefix(name, prefix));
    internal static bool ContainsVariableOrMember(SymbolicFact fact, string name) =>
        Contains(fact, candidate => SymbolicFactFactory.MatchesVariableOrMemberName(candidate, name));
    internal static bool ContainsVariableOrMember(SymbolicCondition condition, string name) =>
        Contains(condition, candidate => SymbolicFactFactory.MatchesVariableOrMemberName(candidate, name));
    internal static bool ContainsVariableOrMember(SymbolicTerm term, string name) =>
        Contains(term, candidate => SymbolicFactFactory.MatchesVariableOrMemberName(candidate, name));
    internal static SymbolicState RemoveVariableReferences(SymbolicState state, string prefix) =>
        Remove(state, fact => ContainsVariablePrefix(fact, prefix), condition => ContainsVariablePrefix(condition, prefix));
    internal static SymbolicState RemoveVariableOrMemberReferences(SymbolicState state, string name) =>
        Remove(state, fact => ContainsVariableOrMember(fact, name), condition => ContainsVariableOrMember(condition, name));
    internal static SymbolicState RemoveVariableDescendantReferences(SymbolicState state, string name) =>
        Remove(state, fact => ContainsDescendant(fact, name), condition => ContainsDescendant(condition, name));
    internal static SymbolicState RemoveVariableElementReferences(SymbolicState state, string name) =>
        Remove(state, fact => ContainsElement(fact, name), condition => ContainsElement(condition, name));
    private static SymbolicState Remove(
        SymbolicState state,
        Func<SymbolicFact, bool> factMatches,
        Func<SymbolicCondition, bool> conditionMatches) => new SymbolicState(
        state.Facts.Where(fact => !factMatches(fact)),
        state.PathConditions.Where(condition => !conditionMatches(condition)),
        state.SymbolVersions,
        state.IsContradictory,
        state.IsExact,
        state.UnknownReason,
        state.Provenance).Normalize();
    private static bool Contains(SymbolicFact fact, Func<string, bool> match) =>
        SymbolicAlgebra.Any(fact, term => Matches(term, match));
    private static bool Contains(SymbolicCondition condition, Func<string, bool> match) =>
        SymbolicAlgebra.Any(condition, term => Matches(term, match));
    private static bool Contains(SymbolicTerm term, Func<string, bool> match) =>
        SymbolicAlgebra.Any(term, candidate => Matches(candidate, match));
    private static bool Matches(SymbolicTerm term, Func<string, bool> match) {
        if (term is SymbolicMemberTerm && TryCreatePath(term, out var path) && match(path)) return true;
        var name = term switch {
            SymbolicVariableTerm variable => variable.Name,
            SymbolicNullableHasValueTerm nullable => nullable.NullableName,
            SymbolicNullableValueTerm nullable => nullable.NullableName,
            _ => null
        };
        return name != null && match(name);
    }
    private static bool ContainsDescendant(SymbolicFact fact, string name) =>
        SymbolicAlgebra.Any(fact, term => IsDescendant(term, name));
    private static bool ContainsDescendant(SymbolicCondition condition, string name) =>
        SymbolicAlgebra.Any(condition, term => IsDescendant(term, name));
    private static bool ContainsElement(SymbolicFact fact, string name) =>
        SymbolicAlgebra.Any(fact, term => IsElementOf(term, name));
    private static bool ContainsElement(SymbolicCondition condition, string name) =>
        SymbolicAlgebra.Any(condition, term => IsElementOf(term, name));
    private static bool IsElementOf(SymbolicTerm term, string name) => term switch {
        SymbolicElementTerm element => ContainsVariableOrMember(element.Receiver, name),
        SymbolicMultiElementTerm element => ContainsVariableOrMember(element.Receiver, name),
        _ => false
    };
    private static bool IsDescendant(SymbolicTerm term, string name) =>
        term is not SymbolicVariableTerm &&
        Contains(term, candidate => SymbolicFactFactory.MatchesVariableOrMemberName(candidate, name)) ||
        Matches(term, candidate => candidate.StartsWith(name + ".", StringComparison.Ordinal) ||
                                   candidate.StartsWith(name + "[", StringComparison.Ordinal));
    private static bool TryCreatePath(SymbolicTerm term, out string path) {
        if (term is SymbolicVariableTerm variable) {
            path = variable.Name;
            return true;
        }
        if (term is SymbolicMemberTerm member && TryCreatePath(member.Receiver, out var receiver)) {
            path = receiver + "." + member.MemberName;
            return true;
        }
        path = string.Empty;
        return false;
    }
    private static bool MatchesPrefix(string candidate, string prefix) {
        if (SymbolicFactFactory.MatchesVariableOrMemberName(candidate, prefix)) return true;
        var versionPrefix = prefix + "@v";
        if (!candidate.StartsWith(versionPrefix, StringComparison.Ordinal)) return false;
        var index = versionPrefix.Length;
        var digitStart = index;
        while (index < candidate.Length && char.IsDigit(candidate[index])) index++;
        return index > digitStart && (index == candidate.Length || candidate[index] is '.' or '[');
    }
}
