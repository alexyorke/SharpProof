using System.Text.Json.Serialization;

namespace SharpProof.Symbolic;

internal sealed record SymbolicProgramPointMetadata(
    string FilePath,
    int Line,
    int Column,
    int Position,
    int? RequestedLine,
    int? RequestedColumn,
    int? RequestedPosition,
    int? RequestedPositionDistance,
    bool? ContainsRequestedPosition,
    int NodeSpanStart,
    int NodeSpanEnd,
    int NodeStartLine,
    int NodeStartColumn,
    int NodeEndLine,
    int NodeEndColumn,
    string NodeKind,
    string? MethodName,
    string ProgramPointKind)
{
    public int NodeSpanLength => Math.Max(0, NodeSpanEnd - NodeSpanStart);
}

internal sealed class SymbolicProgramPointResult(
    SymbolicProgramPointMetadata metadata,
    IReadOnlyList<string> facts,
    SymbolicReachability reachability = SymbolicReachability.NotChecked,
    string reachabilityReason = "reachability_not_checked",
    IReadOnlyList<SymbolicConditionProofResult>? conditionProofs = null,
    SymbolicSmtDiagnostics? smtDiagnostics = null,
    string? mergedInvariantText = null,
    SymbolicInvariantResult? invariant = null,
    IReadOnlyList<SymbolicFactInfo>? symbolicFacts = null,
    SymbolicInputWitness? reachabilityWitness = null,
    SymbolicAnalysisTruncationInfo? analysisTruncation = null)
{
    [JsonIgnore] internal SymbolicProgramPointMetadata Metadata { get; } = metadata;

    public string FilePath => Metadata.FilePath;
    public int Line => Metadata.Line;
    public int Column => Metadata.Column;
    public int Position => Metadata.Position;
    public int? RequestedLine => Metadata.RequestedLine;
    public int? RequestedColumn => Metadata.RequestedColumn;
    public int? RequestedPosition => Metadata.RequestedPosition;
    public int? RequestedPositionDistance => Metadata.RequestedPositionDistance;
    public bool? ContainsRequestedPosition => Metadata.ContainsRequestedPosition;
    public int NodeSpanStart => Metadata.NodeSpanStart;
    public int NodeSpanEnd => Metadata.NodeSpanEnd;
    public int NodeSpanLength => Metadata.NodeSpanLength;
    public int NodeStartLine => Metadata.NodeStartLine;
    public int NodeStartColumn => Metadata.NodeStartColumn;
    public int NodeEndLine => Metadata.NodeEndLine;
    public int NodeEndColumn => Metadata.NodeEndColumn;
    public string NodeKind => Metadata.NodeKind;
    public string? MethodName => Metadata.MethodName;
    public string ProgramPointKind => Metadata.ProgramPointKind;

    public IReadOnlyList<string> Facts { get; } = facts ?? Array.Empty<string>();

    public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; } = symbolicFacts ?? Array.Empty<SymbolicFactInfo>();

    public string MergedInvariantText { get; } = mergedInvariantText ?? invariant?.MergedInvariantText ??
        FormatMergedInvariantText(facts ?? Array.Empty<string>());

    public SymbolicInvariantResult Invariant { get; } = invariant ?? SymbolicInvariantResult.FromFacts(
        facts ?? Array.Empty<string>(),
        mergedInvariantText ?? FormatMergedInvariantText(facts ?? Array.Empty<string>()),
        SymbolicInvariantMergeKind.Conjunction);

    public SymbolicInvariantInfo InvariantInfo => new(
        MergedInvariantText,
        SymbolicFacts,
        ConditionProofs.Select(static proof => proof.Proof).ToArray(),
        Invariant.MergeKind,
        Invariant.ConditionCount);

    public int PathConditionCount => InvariantInfo.ConditionCount;

    public SymbolicReachability Reachability { get; } = reachability;

    public string ReachabilityReason { get; } = reachabilityReason;

    public SymbolicInputWitness ReachabilityWitness { get; } = reachabilityWitness ??
        SymbolicInputWitnessFactory.CreateReachability(
            null, Array.Empty<SmtFormula>(), null, metadata.Position, reachability, reachabilityReason);

    public SymbolicInputDomainSummary InputDomainSummary => ReachabilityWitness.DomainSummary;

    public IReadOnlyList<SymbolicConditionProofResult> ConditionProofs { get; } =
        (conditionProofs ?? Array.Empty<SymbolicConditionProofResult>())
        .Select(proof => proof.WithProgramPointMetadata(metadata))
        .ToArray();

    public SymbolicProofOutcomeSummary ProofOutcomes => new(
        ConditionProofs.Count,
        ConditionProofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.Unknown),
        ConditionProofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.ProvenTrue),
        ConditionProofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.ProvenFalse),
        ConditionProofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.Unreachable));

    public SymbolicSmtDiagnostics SmtDiagnostics { get; } = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; } =
        analysisTruncation ?? SymbolicAnalysisTruncationInfo.None;

    private static string FormatMergedInvariantText(IReadOnlyList<string> facts)
    {
        if (facts.Count == 0) return "true";

        if (facts.Count == 1) return facts[0];

        return string.Join(" && ", facts.Select(static fact => "(" + fact + ")"));
    }

}

internal sealed record SymbolicInvariantResult(
    [property: JsonPropertyOrder(0)] IReadOnlyList<SymbolicInvariantCondition> Conditions,
    [property: JsonPropertyOrder(4)] string MergedInvariantText,
    [property: JsonPropertyOrder(5)] SymbolicInvariantMergeKind MergeKind)
{
    [JsonPropertyOrder(1)]
    public int ConditionCount => Conditions.Count;

    [JsonPropertyOrder(2)]
    public int ConservativeUnknownCount => Conditions.Count(static condition => condition.IsConservativeUnknown);

    [JsonPropertyOrder(3)]
    public bool HasConservativeUnknowns => ConservativeUnknownCount != 0;

    [JsonPropertyOrder(6)]
    public bool IsTrivial =>
        Conditions.Count == 0 && string.Equals(MergedInvariantText, "true", StringComparison.Ordinal);

    public static SymbolicInvariantResult FromFacts(
        IReadOnlyList<string> facts,
        string? mergedInvariantText = null,
        SymbolicInvariantMergeKind mergeKind = SymbolicInvariantMergeKind.DistinctFactUnion)
    {
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
        SymbolicInvariantMergeKind mergeKind = SymbolicInvariantMergeKind.Conjunction)
    {
        if (formulas == null) throw new ArgumentNullException(nameof(formulas));

        return new SymbolicInvariantResult(
            formulas
                .Select(static (formula, index) => SymbolicInvariantCondition.FromFormula(index, formula))
                .ToArray(),
            mergedInvariantText ?? SymbolicFormulaDisplay.FormatMergedInvariant(formulas),
            mergeKind);
    }

    public static SymbolicInvariantResult FromMergedPathFacts(SymbolicMergedPathFacts facts)
    {
        if (facts == null) throw new ArgumentNullException(nameof(facts));

        var conditions = new List<SymbolicInvariantCondition>();
        foreach (var fact in facts.AlwaysFacts)
            conditions.Add(SymbolicInvariantCondition.FromText(conditions.Count, fact));

        foreach (var unknown in facts.ConservativeUnknowns)
            conditions.Add(SymbolicInvariantCondition.FromConservativeUnknown(conditions.Count, unknown));

        if (facts.IsUnreachable) conditions.Add(SymbolicInvariantCondition.FromText(conditions.Count, "false"));

        return new SymbolicInvariantResult(
            conditions,
            facts.MergedInvariantText,
            SymbolicInvariantMergeKind.ConservativeFactMerge);
    }
}

internal sealed record SymbolicInvariantCondition(
    [property: JsonPropertyOrder(0)] int Index,
    [property: JsonPropertyOrder(1)] string Text,
    [property: JsonPropertyOrder(2)] string DisplayKind,
    [property: JsonPropertyOrder(3)] string ValueKind,
    [property: JsonPropertyOrder(4)] bool IsSolverBacked,
    [property: JsonPropertyOrder(5)] string Target,
    [property: JsonPropertyOrder(6)] bool IsConservativeUnknown)
{
    public static SymbolicInvariantCondition FromText(int index, string text)
    {
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

    internal static SymbolicInvariantCondition FromFormula(int index, SmtFormula formula)
    {
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

    public static SymbolicInvariantCondition FromConservativeUnknown(int index, string text)
    {
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

    private static string ExtractConservativeUnknownTarget(string? text)
    {
        const string prefix = "unknown(";
        if (text != null &&
            text.StartsWith(prefix, StringComparison.Ordinal) &&
            text.EndsWith(")", StringComparison.Ordinal) &&
            text.Length > prefix.Length + 1)
            return text.Substring(prefix.Length, text.Length - prefix.Length - 1);

        return text ?? string.Empty;
    }

}

internal static class TextFactTargetExtraction
{
    internal static string? TryExtract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        return ScanIdentifierTarget(Unwrap(text!.Trim()));
    }

    internal static string Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var value = Unwrap(text.Trim());
        return ScanIdentifierTarget(value) ?? value;
    }

    private static string Unwrap(string value)
    {
        while (value.StartsWith("!", StringComparison.Ordinal) ||
               (value.StartsWith("(", StringComparison.Ordinal) && value.EndsWith(")", StringComparison.Ordinal)))
            value = value.StartsWith("!", StringComparison.Ordinal)
                ? value.Substring(1).TrimStart()
                : value.Substring(1, value.Length - 2).Trim();

        return value;
    }

    private static string? ScanIdentifierTarget(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
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

internal enum SymbolicInvariantMergeKind
{
    Conjunction,
    DistinctFactUnion,
    ConservativeFactMerge
}

internal sealed record SymbolicConditionProofResult(
    [property: JsonIgnore] string condition,
    [property: JsonIgnore] SymbolicTruthValue truthValue,
    [property: JsonIgnore] string reason,
    [property: JsonIgnore] SmtFormula? formula = null,
    [property: JsonIgnore] string? target = null,
    [property: JsonIgnore] string? formulaKind = null,
    [property: JsonIgnore] string? valueKind = null,
    [property: JsonIgnore] string? formulaText = null,
    [property: JsonIgnore] bool? isSolverBacked = null,
    [property: JsonIgnore] SymbolicProgramPointMetadata? programPoint = null,
    [property: JsonIgnore] SymbolicInputWitness? witness = null,
    [property: JsonIgnore] SymbolicInputWitness? counterexampleWitness = null,
    [property: JsonIgnore] SymbolicAnalysisTruncationInfo? analysisTruncation = null)
{
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

    [JsonIgnore] internal SymbolicProgramPointMetadata? ProgramPoint { get; init; } = programPoint;

    public string? FilePath => ProgramPoint?.FilePath;
    public int? Line => ProgramPoint?.Line;
    public int? Column => ProgramPoint?.Column;
    public int? Position => ProgramPoint?.Position;
    public int? NodeSpanStart => ProgramPoint?.NodeSpanStart;
    public int? NodeSpanEnd => ProgramPoint?.NodeSpanEnd;
    public int? NodeSpanLength => ProgramPoint?.NodeSpanLength;
    public int? NodeStartLine => ProgramPoint?.NodeStartLine;
    public int? NodeStartColumn => ProgramPoint?.NodeStartColumn;
    public int? NodeEndLine => ProgramPoint?.NodeEndLine;
    public int? NodeEndColumn => ProgramPoint?.NodeEndColumn;
    public string? NodeKind => ProgramPoint?.NodeKind;
    public string? MethodName => ProgramPoint?.MethodName;
    public string? ProgramPointKind => ProgramPoint?.ProgramPointKind;
    public int? RequestedLine => ProgramPoint?.RequestedLine;
    public int? RequestedColumn => ProgramPoint?.RequestedColumn;
    public int? RequestedPosition => ProgramPoint?.RequestedPosition;
    public int? RequestedPositionDistance => ProgramPoint?.RequestedPositionDistance;
    public bool? ContainsRequestedPosition => ProgramPoint?.ContainsRequestedPosition;

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

    public string GetDisplayReason() => SymbolicReasonDisplay.Format(Reason);

    internal SymbolicConditionProofResult WithProgramPointMetadata(
        SymbolicProgramPointMetadata metadata) => ProgramPoint == null
        ? this with { ProgramPoint = metadata }
        : this;

    internal SymbolicConditionProofResult WithAnalysisTruncation(SymbolicAnalysisTruncationInfo truncation) =>
        this with { AnalysisTruncation = truncation };

}

internal enum SymbolicTruthValue
{
    Unknown,
    ProvenTrue,
    ProvenFalse,
    Unreachable
}
