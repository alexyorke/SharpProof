using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Smt;

internal static class SmtFormulaReferenceScanner
{
    internal static bool ContainsVariablePrefix(SmtFormula formula, string variablePrefix)
    {
        return ContainsVariable(formula, variableName => ContainsBoundedPrefix(variableName, variablePrefix));
    }

    private static bool ContainsBoundedPrefix(string variableName, string variablePrefix)
    {
        for (var searchStart = 0; searchStart <= variableName.Length - variablePrefix.Length;)
        {
            var match = variableName.IndexOf(variablePrefix, searchStart, StringComparison.Ordinal);
            if (match < 0) return false;

            var end = match + variablePrefix.Length;
            if (end == variableName.Length || !char.IsDigit(variableName[end])) return true;
            searchStart = match + 1;
        }

        return false;
    }

    internal static bool ContainsVariableOrMember(SmtFormula formula, string variableName)
    {
        return ContainsVariable(formula,
            candidateName => SymbolicFactFactory.MatchesVariableOrMemberName(candidateName, variableName));
    }

    private static bool ContainsVariable(SmtFormula formula, Func<string, bool> matchVariableName)
    {
        return SmtFormulaTraversal.Enumerate(formula)
            .OfType<SmtVariable>()
            .Any(variable => matchVariableName(variable.Name));
    }
}
