namespace SharpProof.Symbolic;

internal sealed class SymbolicProgramPointResult(
    IReadOnlyList<SmtFormula> pathConditions,
    SymbolicReachability reachability,
    string reachabilityReason,
    SymbolicInputWitness reachabilityWitness,
    SymbolicAnalysisTruncationInfo analysisTruncation) {
    internal IReadOnlyList<SmtFormula> PathConditions { get; } = pathConditions;

    public SymbolicReachability Reachability { get; } = reachability;

    public string ReachabilityReason { get; } = reachabilityReason;

    public SymbolicInputWitness ReachabilityWitness { get; } = reachabilityWitness;

    public SymbolicInputDomainSummary InputDomainSummary => ReachabilityWitness.DomainSummary;

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; } = analysisTruncation;

}

internal sealed record SymbolicConditionProofResult(
    string condition,
    SymbolicTruthValue truthValue,
    string reason,
    SmtFormula? formula = null,
    string? target = null,
    string? formulaKind = null,
    string? valueKind = null,
    string? formulaText = null,
    bool? isSolverBacked = null,
    SymbolicInputWitness? witness = null,
    SymbolicInputWitness? counterexampleWitness = null,
    SymbolicAnalysisTruncationInfo? analysisTruncation = null) {
    public string Condition { get; init; } = condition ?? string.Empty;

    public string Target { get; init; } = ResolveTarget(formula, target);

    public string DisplayKind { get; init; } = ResolveFormulaKind(formula, formulaKind);

    public string ValueKind { get; init; } = ResolveValueKind(formula, valueKind);

    internal string FormulaText { get; init; } = ResolveFormulaText(condition, formula, formulaText);

    internal bool IsSolverBacked { get; init; } = isSolverBacked ?? formula != null;

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

    private static string ResolveTarget(SmtFormula? formula, string? value) => string.IsNullOrWhiteSpace(value)
        ? formula == null ? string.Empty : SymbolicFormulaDisplay.GetMergeTarget(formula)
        : value!;

    private static string ResolveFormulaKind(SmtFormula? formula, string? value) => string.IsNullOrWhiteSpace(value)
        ? formula == null ? "Unknown" : SymbolicFormulaDisplay.GetKind(formula)
        : value!;

    private static string ResolveValueKind(SmtFormula? formula, string? value) => string.IsNullOrWhiteSpace(value)
        ? formula == null ? "Unknown" : formula.Kind.ToString()
        : value!;

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
