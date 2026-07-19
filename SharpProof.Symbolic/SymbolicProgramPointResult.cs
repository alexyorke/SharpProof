using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicProgramPointResult
{
    internal SymbolicProgramPointResult(
        string filePath,
        int line,
        int column,
        int position,
        int nodeSpanStart,
        string nodeKind,
        IReadOnlyList<string> facts,
        SymbolicReachability reachability = SymbolicReachability.NotChecked,
        string reachabilityReason = "reachability_not_checked",
        IReadOnlyList<SymbolicConditionProofResult>? conditionProofs = null,
        SymbolicSmtDiagnostics? smtDiagnostics = null,
        string? mergedInvariantText = null,
        SymbolicInvariantResult? invariant = null,
        int? nodeSpanEnd = null,
        int? nodeStartLine = null,
        int? nodeStartColumn = null,
        int? nodeEndLine = null,
        int? nodeEndColumn = null,
        string? methodName = null,
        string? programPointKind = null,
        int? requestedLine = null,
        int? requestedColumn = null,
        int? requestedPosition = null,
        int? requestedPositionDistance = null,
        bool? containsRequestedPosition = null,
        IReadOnlyList<SymbolicFactInfo>? symbolicFacts = null,
        SymbolicInputWitness? reachabilityWitness = null,
        SymbolicAnalysisTruncationInfo? analysisTruncation = null)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
        Position = position;
        RequestedLine = requestedLine;
        RequestedColumn = requestedColumn;
        RequestedPosition = requestedPosition;
        RequestedPositionDistance = requestedPositionDistance;
        ContainsRequestedPosition = containsRequestedPosition;
        NodeSpanStart = nodeSpanStart;
        NodeSpanEnd = nodeSpanEnd ?? nodeSpanStart;
        NodeSpanLength = Math.Max(0, NodeSpanEnd - NodeSpanStart);
        NodeStartLine = nodeStartLine ?? line;
        NodeStartColumn = nodeStartColumn ?? column;
        NodeEndLine = nodeEndLine ?? NodeStartLine;
        NodeEndColumn = nodeEndColumn ?? NodeStartColumn + NodeSpanLength;
        NodeKind = nodeKind;
        MethodName = string.IsNullOrWhiteSpace(methodName) ? null : methodName;
        ProgramPointKind = SymbolicProgramPointKinds.Normalize(programPointKind, nodeKind);
        Facts = facts ?? Array.Empty<string>();
        SymbolicFacts = symbolicFacts ?? Array.Empty<SymbolicFactInfo>();
        MergedInvariantText = mergedInvariantText ?? invariant?.MergedInvariantText ?? FormatMergedInvariantText(Facts);
        Invariant = invariant == null
            ? SymbolicInvariantResult.FromFacts(
                Facts,
                MergedInvariantText,
                SymbolicInvariantMergeKind.Conjunction)
            : invariant;
        Reachability = reachability;
        ReachabilityReason = reachabilityReason;
        ReachabilityWitness = reachabilityWitness ?? SymbolicInputWitnessFactory.CreateReachability(
            null,
            Array.Empty<SmtFormula>(),
            null,
            position,
            reachability,
            reachabilityReason);
        ConditionProofs = AttachProgramPointMetadata(conditionProofs ?? Array.Empty<SymbolicConditionProofResult>());
        ProofOutcomes = new SymbolicProofOutcomeSummary(
            ConditionProofs.Count,
            ConditionProofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.Unknown),
            ConditionProofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.ProvenTrue),
            ConditionProofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.ProvenFalse),
            ConditionProofs.Count(static proof => proof.TruthValue == SymbolicTruthValue.Unreachable));
        InvariantInfo = new SymbolicInvariantInfo(
            MergedInvariantText,
            SymbolicFacts,
            ConditionProofs.Select(static proof => proof.Proof).ToArray(),
            Invariant.MergeKind,
            Invariant.ConditionCount);
        SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        AnalysisTruncation = analysisTruncation ?? SymbolicAnalysisTruncationInfo.None;
    }

    public string FilePath { get; }

    public int Line { get; }

    public int Column { get; }

    public int Position { get; }

    public int? RequestedLine { get; }

    public int? RequestedColumn { get; }

    public int? RequestedPosition { get; }

    public int? RequestedPositionDistance { get; }

    public bool? ContainsRequestedPosition { get; }

    public int NodeSpanStart { get; }

    public int NodeSpanEnd { get; }

    public int NodeSpanLength { get; }

    public int NodeStartLine { get; }

    public int NodeStartColumn { get; }

    public int NodeEndLine { get; }

    public int NodeEndColumn { get; }

    public string NodeKind { get; }

    public string? MethodName { get; }

    public string ProgramPointKind { get; }

    public IReadOnlyList<string> Facts { get; }

    public IReadOnlyList<SymbolicFactInfo> SymbolicFacts { get; }

    public string MergedInvariantText { get; }

    public SymbolicInvariantResult Invariant { get; }

    public SymbolicInvariantInfo InvariantInfo { get; }

    public int PathConditionCount => InvariantInfo.ConditionCount;

    public SymbolicReachability Reachability { get; }

    public string ReachabilityReason { get; }

    public SymbolicInputWitness ReachabilityWitness { get; }

    public SymbolicInputDomainSummary InputDomainSummary => ReachabilityWitness.DomainSummary;

    public IReadOnlyList<SymbolicConditionProofResult> ConditionProofs { get; }

    public SymbolicProofOutcomeSummary ProofOutcomes { get; }

    public SymbolicSmtDiagnostics SmtDiagnostics { get; }

    public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; }

    private static string FormatMergedInvariantText(IReadOnlyList<string> facts)
    {
        if (facts.Count == 0) return "true";

        if (facts.Count == 1) return facts[0];

        return string.Join(" && ", facts.Select(static fact => "(" + fact + ")"));
    }

    private IReadOnlyList<SymbolicConditionProofResult> AttachProgramPointMetadata(
        IReadOnlyList<SymbolicConditionProofResult> proofs)
    {
        if (proofs.Count == 0) return proofs;

        return proofs
            .Select(proof => proof.WithProgramPointMetadata(
                FilePath,
                Line,
                Column,
                Position,
                NodeSpanStart,
                NodeSpanEnd,
                NodeStartLine,
                NodeStartColumn,
                NodeEndLine,
                NodeEndColumn,
                NodeKind,
                MethodName,
                ProgramPointKind,
                RequestedLine,
                RequestedColumn,
                RequestedPosition,
                RequestedPositionDistance,
                ContainsRequestedPosition))
            .ToArray();
    }
}

internal sealed class SymbolicInvariantResult
{
    private SymbolicInvariantResult(
        IReadOnlyList<SymbolicInvariantCondition> conditions,
        string mergedInvariantText,
        SymbolicInvariantMergeKind mergeKind)
    {
        Conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        MergedInvariantText = mergedInvariantText ?? throw new ArgumentNullException(nameof(mergedInvariantText));
        MergeKind = mergeKind;
    }

    public IReadOnlyList<SymbolicInvariantCondition> Conditions { get; }

    public int ConditionCount => Conditions.Count;

    public int ConservativeUnknownCount => Conditions.Count(static condition => condition.IsConservativeUnknown);

    public bool HasConservativeUnknowns => ConservativeUnknownCount != 0;

    public string MergedInvariantText { get; }

    public SymbolicInvariantMergeKind MergeKind { get; }

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

internal sealed class SymbolicInvariantCondition
{
    private SymbolicInvariantCondition(
        int index,
        string text,
        string formulaKind,
        string valueKind,
        bool isSolverBacked,
        string target,
        bool isConservativeUnknown)
    {
        Index = index;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        FormulaKind = formulaKind ?? throw new ArgumentNullException(nameof(formulaKind));
        ValueKind = valueKind ?? throw new ArgumentNullException(nameof(valueKind));
        IsSolverBacked = isSolverBacked;
        DisplayKind = FormulaKind;
        Target = target ?? string.Empty;
        IsConservativeUnknown = isConservativeUnknown;
    }

    public int Index { get; }

    public string Text { get; }

    public string DisplayKind { get; }

    public string ValueKind { get; }

    public bool IsSolverBacked { get; }

    internal string FormulaKind { get; }

    public string Target { get; }

    public bool IsConservativeUnknown { get; }

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
    [property: JsonIgnore] string? filePath = null,
    [property: JsonIgnore] int? line = null,
    [property: JsonIgnore] int? column = null,
    [property: JsonIgnore] int? position = null,
    [property: JsonIgnore] int? nodeSpanStart = null,
    [property: JsonIgnore] int? nodeSpanEnd = null,
    [property: JsonIgnore] int? nodeStartLine = null,
    [property: JsonIgnore] int? nodeStartColumn = null,
    [property: JsonIgnore] int? nodeEndLine = null,
    [property: JsonIgnore] int? nodeEndColumn = null,
    [property: JsonIgnore] string? nodeKind = null,
    [property: JsonIgnore] string? methodName = null,
    [property: JsonIgnore] string? programPointKind = null,
    [property: JsonIgnore] int? requestedLine = null,
    [property: JsonIgnore] int? requestedColumn = null,
    [property: JsonIgnore] int? requestedPosition = null,
    [property: JsonIgnore] int? requestedPositionDistance = null,
    [property: JsonIgnore] bool? containsRequestedPosition = null,
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

    public SymbolicProofInfo Proof => SymbolicProofProjection
        .FromSolverBackedResult(
            SymbolicProofProjection.MapStatus(TruthValue),
            IsSolverBacked,
            TruthValue == SymbolicTruthValue.Unknown ? Reason : null)
        .CreateInfo(Reason, false, null, Target, FormulaText, DisplayKind);

    public string? FilePath { get; init; } = string.IsNullOrWhiteSpace(filePath) ? null : filePath;

    public int? Line { get; init; } = line;

    public int? Column { get; init; } = column;

    public int? Position { get; init; } = position;

    public int? NodeSpanStart { get; init; } = nodeSpanStart;

    public int? NodeSpanEnd { get; init; } = nodeSpanEnd;

    public int? NodeSpanLength { get; init; } = nodeSpanStart.HasValue && nodeSpanEnd.HasValue
        ? Math.Max(0, nodeSpanEnd.Value - nodeSpanStart.Value)
        : null;

    public int? NodeStartLine { get; init; } = nodeStartLine;

    public int? NodeStartColumn { get; init; } = nodeStartColumn;

    public int? NodeEndLine { get; init; } = nodeEndLine;

    public int? NodeEndColumn { get; init; } = nodeEndColumn;

    public string? NodeKind { get; init; } = string.IsNullOrWhiteSpace(nodeKind) ? null : nodeKind;

    public string? MethodName { get; init; } = string.IsNullOrWhiteSpace(methodName) ? null : methodName;

    public string? ProgramPointKind { get; init; } =
        string.IsNullOrWhiteSpace(programPointKind) ? null : programPointKind;

    public int? RequestedLine { get; init; } = requestedLine;

    public int? RequestedColumn { get; init; } = requestedColumn;

    public int? RequestedPosition { get; init; } = requestedPosition;

    public int? RequestedPositionDistance { get; init; } = requestedPositionDistance;

    public bool? ContainsRequestedPosition { get; init; } = containsRequestedPosition;

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
        string filePath,
        int line,
        int column,
        int position,
        int nodeSpanStart,
        int nodeSpanEnd,
        int nodeStartLine,
        int nodeStartColumn,
        int nodeEndLine,
        int nodeEndColumn,
        string nodeKind,
        string? methodName,
        string programPointKind,
        int? requestedLine,
        int? requestedColumn,
        int? requestedPosition,
        int? requestedPositionDistance,
        bool? containsRequestedPosition)
    {
        var effectiveSpanStart = NodeSpanStart ?? nodeSpanStart;
        var effectiveSpanEnd = NodeSpanEnd ?? nodeSpanEnd;
        return this with
        {
            FilePath = FilePath ?? filePath,
            Line = Line ?? line,
            Column = Column ?? column,
            Position = Position ?? position,
            NodeSpanStart = effectiveSpanStart,
            NodeSpanEnd = effectiveSpanEnd,
            NodeSpanLength = Math.Max(0, effectiveSpanEnd - effectiveSpanStart),
            NodeStartLine = NodeStartLine ?? nodeStartLine,
            NodeStartColumn = NodeStartColumn ?? nodeStartColumn,
            NodeEndLine = NodeEndLine ?? nodeEndLine,
            NodeEndColumn = NodeEndColumn ?? nodeEndColumn,
            NodeKind = NodeKind ?? nodeKind,
            MethodName = MethodName ?? methodName,
            ProgramPointKind = ProgramPointKind ?? programPointKind,
            RequestedLine = RequestedLine ?? requestedLine,
            RequestedColumn = RequestedColumn ?? requestedColumn,
            RequestedPosition = RequestedPosition ?? requestedPosition,
            RequestedPositionDistance = RequestedPositionDistance ?? requestedPositionDistance,
            ContainsRequestedPosition = ContainsRequestedPosition ?? containsRequestedPosition
        };
    }

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
