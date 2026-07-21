namespace SharpProof.Symbolic.Ir;

internal static class SymbolicIrReferenceScanner {
    internal static bool ContainsVariablePrefix(SymbolicFact fact, string variablePrefix) =>
        ContainsVariable(fact, name => MatchesVariablePrefix(name, variablePrefix));

    internal static bool ContainsVariablePrefix(SymbolicCondition condition, string variablePrefix) =>
        ContainsVariable(condition, name => MatchesVariablePrefix(name, variablePrefix));

    internal static bool ContainsVariableOrMember(SymbolicFact fact, string variableName) =>
        ContainsVariable(fact, name => SymbolicFactFactory.MatchesVariableOrMemberName(name, variableName));

    internal static bool ContainsVariableOrMember(SymbolicCondition condition, string variableName) => ContainsVariable(condition,
            name => SymbolicFactFactory.MatchesVariableOrMemberName(name, variableName));

    internal static bool ContainsVariableOrMember(SymbolicTerm term, string variableName) {
        var scanner = new VariableReferenceVisitor(
            name => SymbolicFactFactory.MatchesVariableOrMemberName(name, variableName));
        scanner.Visit(term);
        return scanner.Found;
    }

    internal static SymbolicState RemoveVariableReferences(SymbolicState state, string variablePrefix) => RemoveReferences(
            state,
            fact => ContainsVariablePrefix(fact, variablePrefix),
            condition => ContainsVariablePrefix(condition, variablePrefix));

    internal static SymbolicState RemoveVariableOrMemberReferences(SymbolicState state, string variableName) => RemoveReferences(
            state,
            fact => ContainsVariableOrMember(fact, variableName),
            condition => ContainsVariableOrMember(condition, variableName));

    private static SymbolicState RemoveReferences(
        SymbolicState state,
        Func<SymbolicFact, bool> containsReferenceInFact,
        Func<SymbolicCondition, bool> containsReferenceInCondition) => new SymbolicState(
            state.Facts.Where(fact => !containsReferenceInFact(fact)),
            state.PathConditions.Where(condition => !containsReferenceInCondition(condition)),
            state.SymbolVersions).Normalize();

    private static bool ContainsVariable(SymbolicFact fact, Func<string, bool> match) {
        var scanner = new VariableReferenceVisitor(match);
        scanner.Visit(fact);
        return scanner.Found;
    }

    private static bool ContainsVariable(SymbolicCondition condition, Func<string, bool> match) {
        var scanner = new VariableReferenceVisitor(match);
        scanner.Visit(condition);
        return scanner.Found;
    }

    private static bool TryCreateVariableOrMemberPath(SymbolicTerm term, out string path) {
        switch (term) {
            case SymbolicVariableTerm variable:
                path = variable.Name;
                return true;
            case SymbolicMemberTerm member when
                TryCreateVariableOrMemberPath(member.Receiver, out var receiverPath):
                path = receiverPath + "." + member.MemberName;
                return true;
            default:
                path = string.Empty;
                return false;
        }
    }

    private static bool MatchesVariablePrefix(string candidate, string variablePrefix) {
        if (SymbolicFactFactory.MatchesVariableOrMemberName(candidate, variablePrefix)) return true;

        var versionPrefix = variablePrefix + "@v";
        if (!candidate.StartsWith(versionPrefix, StringComparison.Ordinal)) return false;

        var index = versionPrefix.Length;
        var digitStart = index;
        while (index < candidate.Length && char.IsDigit(candidate[index])) index++;

        return index > digitStart &&
               (index == candidate.Length || candidate[index] is '.' or '[');
    }

    sealed class VariableReferenceVisitor : SymbolicIrVisitor {
        private readonly Func<string, bool> _match;

        internal VariableReferenceVisitor(Func<string, bool> match) => _match = match;

        internal bool Found { get; private set; }

        protected override void OnTerm(SymbolicTerm term) {
            if (!Found &&
                term is SymbolicMemberTerm &&
                TryCreateVariableOrMemberPath(term, out var memberPath) &&
                _match(memberPath))
                Found = true;
        }

        protected override void OnVariableLikeName(string name) {
            if (!Found && _match(name)) Found = true;
        }
    }
}
