namespace SharpProof.Symbolic;
internal sealed record SymbolicConditionProofResult(
    string condition,
    SymbolicTruthValue truthValue,
    string reason,
    SmtFormula? formula = null,
    string? formulaText = null,
    SymbolicInputWitness? witness = null,
    SymbolicInputWitness? counterexampleWitness = null,
    SymbolicAnalysisTruncationInfo? analysisTruncation = null) {
    public string Condition { get; init; } = condition ?? string.Empty;
    internal string FormulaText { get; init; } = ResolveFormulaText(condition, formula, formulaText);
    public SymbolicTruthValue TruthValue { get; init; } = truthValue;
    public string Reason { get; init; } = reason ?? string.Empty;
    internal SymbolicUnknownReason UnknownReason => TruthValue == SymbolicTruthValue.Unknown
        ? SymbolicUnknownReasonClassifier.Classify(Reason)
        : SymbolicUnknownReason.None;
    public SymbolicInputWitness Witness { get; init; } = witness ?? (truthValue == SymbolicTruthValue.Unreachable
        ? SymbolicInputWitnessFactory.None(reason ?? string.Empty)
        : SymbolicInputWitnessFactory.Unsupported("condition_witness_unavailable"));
    public SymbolicInputWitness CounterexampleWitness { get; init; } = counterexampleWitness ??
        SymbolicInputWitnessFactory.None("counterexample_not_available");
    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; init; } =
        analysisTruncation ?? SymbolicAnalysisTruncationInfo.None;
    private static string ResolveFormulaText(string? condition, SmtFormula? formula, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? formula == null ? condition ?? string.Empty : SymbolicFormulaDisplay.Format(formula)
            : value!;
    internal SymbolicConditionProofResult WithAnalysisTruncation(SymbolicAnalysisTruncationInfo truncation) =>
        this with { AnalysisTruncation = truncation };
}
internal enum SymbolicTruthValue {
    Unknown,
    ProvenTrue,
    ProvenFalse,
    Unreachable
}
