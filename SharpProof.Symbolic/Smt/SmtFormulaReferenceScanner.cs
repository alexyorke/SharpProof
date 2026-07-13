using Microsoft.CodeAnalysis;
using SearchLib.Smt;

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
        return ContainsVariable(formula, candidateName =>
            string.Equals(candidateName, variableName, StringComparison.Ordinal) ||
            candidateName.StartsWith(variableName + ".", StringComparison.Ordinal) ||
            candidateName.StartsWith(variableName + "[", StringComparison.Ordinal));
    }

    internal static void RemoveFactsReferencingSymbol(IList<SmtFormula> facts, ISymbol symbol)
    {
        var variablePrefix = SymbolicFactFactory.GetSmtVariableName(symbol);
        for (var index = facts.Count - 1; index >= 0; index--)
            if (ContainsVariablePrefix(facts[index], variablePrefix))
                facts.RemoveAt(index);
    }

    internal static void RemoveFormulasReferencingVariable(
        ICollection<SmtFormula> formulas,
        string variableName)
    {
        foreach (var formula in new List<SmtFormula>(formulas))
            if (ContainsVariableOrMember(formula, variableName))
                formulas.Remove(formula);
    }

    private static bool ContainsVariable(SmtFormula formula, Func<string, bool> matchVariableName)
    {
        switch (formula)
        {
            case SmtVariable variable:
                return matchVariableName(variable.Name);
            case SmtUnaryFormula unary:
                return ContainsVariable(unary.Operand, matchVariableName);
            case SmtBinaryFormula binary:
                return ContainsVariable(binary.Left, matchVariableName) ||
                       ContainsVariable(binary.Right, matchVariableName);
            case SmtIntegerUnaryTerm integerUnary:
                return ContainsVariable(integerUnary.Operand, matchVariableName);
            case SmtIntegerBinaryTerm integerBinary:
                return ContainsVariable(integerBinary.Left, matchVariableName) ||
                       ContainsVariable(integerBinary.Right, matchVariableName);
            case SmtOpaqueIntegerBinaryTerm opaqueIntegerBinary:
                return ContainsVariable(opaqueIntegerBinary.Left, matchVariableName) ||
                       ContainsVariable(opaqueIntegerBinary.Right, matchVariableName);
            case SmtStringLengthTerm stringLength:
                return ContainsVariable(stringLength.Value, matchVariableName);
            case SmtStringConcatTerm stringConcat:
                return ContainsVariable(stringConcat.Left, matchVariableName) ||
                       ContainsVariable(stringConcat.Right, matchVariableName);
            case SmtStringContainsFormula stringContains:
                return ContainsVariable(stringContains.Value, matchVariableName) ||
                       ContainsVariable(stringContains.Search, matchVariableName);
            case SmtStringStartsWithFormula stringStartsWith:
                return ContainsVariable(stringStartsWith.Value, matchVariableName) ||
                       ContainsVariable(stringStartsWith.Prefix, matchVariableName);
            case SmtStringEndsWithFormula stringEndsWith:
                return ContainsVariable(stringEndsWith.Value, matchVariableName) ||
                       ContainsVariable(stringEndsWith.Suffix, matchVariableName);
            case SmtRegexMatchFormula regexMatch:
                return ContainsVariable(regexMatch.Value, matchVariableName);
            case SmtRuntimeTypeTestFormula runtimeTypeTest:
                return ContainsVariable(runtimeTypeTest.Value, matchVariableName);
            case SmtConditionalFormula conditional:
                return ContainsVariable(conditional.Condition, matchVariableName) ||
                       ContainsVariable(conditional.WhenTrue, matchVariableName) ||
                       ContainsVariable(conditional.WhenFalse, matchVariableName);
            default:
                return false;
        }
    }
}
