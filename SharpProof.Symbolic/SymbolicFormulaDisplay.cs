namespace SharpProof.Symbolic;

internal static class SymbolicFormulaDisplay {
    internal static string Format(SymbolicCondition condition) {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        return SymbolicIrFormulaEncoder.TryEncode(condition, out var formula)
            ? Format(formula)
            : condition.ToString() ?? string.Empty;
    }

    internal static string Format(SmtFormula formula) {
        if (formula == null) throw new ArgumentNullException(nameof(formula));
        return SmtFormulaStructuralKey.Create(formula);
    }
}
