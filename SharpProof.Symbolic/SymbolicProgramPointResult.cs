namespace SharpProof.Symbolic;

internal sealed class SymbolicProgramPointResult(
    SymbolicInvariantResult invariant,
    SymbolicReachability reachability,
    string reachabilityReason,
    IReadOnlyList<SymbolicConditionProofResult> conditionProofs,
    SymbolicInputWitness reachabilityWitness,
    SymbolicAnalysisTruncationInfo analysisTruncation) {
    public SymbolicInvariantResult Invariant { get; } = invariant;

    public SymbolicReachability Reachability { get; } = reachability;

    public string ReachabilityReason { get; } = reachabilityReason;

    public SymbolicInputWitness ReachabilityWitness { get; } = reachabilityWitness;

    public SymbolicInputDomainSummary InputDomainSummary => ReachabilityWitness.DomainSummary;

    public IReadOnlyList<SymbolicConditionProofResult> ConditionProofs { get; } = conditionProofs;

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; } = analysisTruncation;

}

internal sealed record SymbolicInvariantResult(
    IReadOnlyList<SymbolicInvariantCondition> Conditions,
    string MergedInvariantText,
    SymbolicInvariantMergeKind MergeKind) {
    public int ConditionCount => Conditions.Count;

    public int ConservativeUnknownCount => Conditions.Count(static condition => condition.IsConservativeUnknown);

    public bool HasConservativeUnknowns => ConservativeUnknownCount != 0;

    public bool IsTrivial =>
        Conditions.Count == 0 && string.Equals(MergedInvariantText, "true", StringComparison.Ordinal);

    public static SymbolicInvariantResult FromFacts(
        IReadOnlyList<string> facts,
        string? mergedInvariantText = null,
        SymbolicInvariantMergeKind mergeKind = SymbolicInvariantMergeKind.DistinctFactUnion) {
        if (facts == null) throw new ArgumentNullException(nameof(facts));

        return new SymbolicInvariantResult(
            facts
                .Select(static (fact, index) => SymbolicInvariantCondition.FromText(index, fact))
                .ToArray(),
            mergedInvariantText ?? SymbolicInvariantFactSummary.FormatMergedInvariantFacts(facts),
            mergeKind);
    }

    internal static SymbolicInvariantResult FromFormulas(
        IReadOnlyList<SmtFormula> formulas,
        string? mergedInvariantText = null,
        SymbolicInvariantMergeKind mergeKind = SymbolicInvariantMergeKind.Conjunction) {
        if (formulas == null) throw new ArgumentNullException(nameof(formulas));

        return new SymbolicInvariantResult(
            formulas
                .Select(static (formula, index) => SymbolicInvariantCondition.FromFormula(index, formula))
                .ToArray(),
            mergedInvariantText ?? SymbolicFormulaDisplay.FormatMergedInvariant(formulas),
            mergeKind);
    }

}

internal sealed record SymbolicInvariantCondition(
    int Index,
    string Text,
    string DisplayKind,
    string ValueKind,
    bool IsSolverBacked,
    string Target,
    bool IsConservativeUnknown) {
    public static SymbolicInvariantCondition FromText(int index, string text) {
        var normalizedText = text ?? string.Empty;
        return new SymbolicInvariantCondition(
            index,
            normalizedText,
            "Text",
            "Unknown",
            false,
            TextFactTargetExtraction.Extract(normalizedText),
            false);
    }

    internal static SymbolicInvariantCondition FromFormula(int index, SmtFormula formula) {
        if (formula == null) throw new ArgumentNullException(nameof(formula));

        return new SymbolicInvariantCondition(
            index,
            SymbolicFormulaDisplay.Format(formula),
            SymbolicFormulaDisplay.GetKind(formula),
            formula.Kind.ToString(),
            true,
            SymbolicFormulaDisplay.GetMergeTarget(formula),
            false);
    }

    public static SymbolicInvariantCondition FromConservativeUnknown(int index, string text) {
        var target = ExtractConservativeUnknownTarget(text);
        return new SymbolicInvariantCondition(
            index,
            text ?? string.Empty,
            "ConservativeUnknown",
            "Unknown",
            false,
            target,
            true);
    }

    private static string ExtractConservativeUnknownTarget(string? text) {
        const string prefix = "unknown(";
        if (text != null &&
            text.StartsWith(prefix, StringComparison.Ordinal) &&
            text.EndsWith(")", StringComparison.Ordinal) &&
            text.Length > prefix.Length + 1)
            return text.Substring(prefix.Length, text.Length - prefix.Length - 1);

        return text ?? string.Empty;
    }

}

internal static class TextFactTargetExtraction {
    internal static string Extract(string text) {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var value = Unwrap(text.Trim());
        return ScanIdentifierTarget(value) ?? value;
    }

    private static string Unwrap(string value) {
        while (value.StartsWith("!", StringComparison.Ordinal) ||
               (value.StartsWith("(", StringComparison.Ordinal) && value.EndsWith(")", StringComparison.Ordinal)))
            value = value.StartsWith("!", StringComparison.Ordinal)
                ? value.Substring(1).TrimStart()
                : value.Substring(1, value.Length - 2).Trim();

        return value;
    }

    private static string? ScanIdentifierTarget(string value) {
        for (var index = 0; index < value.Length; index++) {
            if (!SyntaxFacts.IsIdentifierStartCharacter(value[index]) && value[index] != '@') continue;

            var start = index;
            index++;
            while (index < value.Length && SyntaxFacts.IsIdentifierPartCharacter(value[index])) index++;

            var target = value.Substring(start, index - start);
            if (index + ".Length".Length <= value.Length &&
                string.Equals(value.Substring(index, ".Length".Length), ".Length", StringComparison.Ordinal))
                target += ".Length";

            return target;
        }

        return null;
    }
}

internal enum SymbolicInvariantMergeKind {
    Conjunction,
    DistinctFactUnion,
    ConservativeFactMerge
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

    public SymbolicProofInfo Proof => SymbolicProofInfo.Project(
        SymbolicProofInfo.MapStatus(TruthValue),
        IsSolverBacked,
        Reason,
        false,
        null,
        Target,
        FormulaText,
        DisplayKind,
        TruthValue == SymbolicTruthValue.Unknown ? Reason : null);

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
