using System.Globalization;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

public enum SymbolicProofBackend
{
    None,
    Syntactic,
    Smt
}

public enum SymbolicProofStatus
{
    Unknown,
    Reachable,
    Unreachable,
    ProvenTrue,
    ProvenFalse
}

public enum SymbolicUnknownReason
{
    None,
    UnsupportedIrEncoding,
    SmtDisabled,
    SmtUnavailable,
    Timeout,
    MethodBudgetExceeded,
    PathConditionBudgetExceeded,
    ExpressionBudgetExceeded,
    CancellationRequested,
    EncodingFailure,
    Unknown
}

public sealed class SymbolicBudgetInfo
{
    public SymbolicBudgetInfo(
        int maxPathConditions,
        int maxExpressionNodes,
        int timeoutMilliseconds,
        int methodBudgetMilliseconds,
        int executedQueryCount,
        int cacheEntryCount)
    {
        MaxPathConditions = maxPathConditions;
        MaxExpressionNodes = maxExpressionNodes;
        TimeoutMilliseconds = timeoutMilliseconds;
        MethodBudgetMilliseconds = methodBudgetMilliseconds;
        ExecutedQueryCount = executedQueryCount;
        CacheEntryCount = cacheEntryCount;
    }

    public int MaxPathConditions { get; }

    public int MaxExpressionNodes { get; }

    public int TimeoutMilliseconds { get; }

    public int MethodBudgetMilliseconds { get; }

    public int ExecutedQueryCount { get; }

    public int CacheEntryCount { get; }
}

public sealed class SymbolicProofInfo
{
    public SymbolicProofInfo(
        SymbolicProofStatus status,
        SymbolicProofBackend backend,
        SymbolicUnknownReason unknownReason,
        string reason,
        bool cacheHit,
        SymbolicBudgetInfo? budget,
        string? target = null,
        string? conditionText = null,
        string? displayKind = null)
    {
        Status = status;
        Backend = backend;
        UnknownReason = unknownReason;
        Reason = reason ?? string.Empty;
        UnknownReasonInfo = SymbolicUnknownReasonTaxonomy.ForProof(unknownReason, Reason);
        CacheHit = cacheHit;
        Budget = budget;
        Target = target ?? string.Empty;
        ConditionText = conditionText ?? string.Empty;
        DisplayKind = displayKind ?? backend.ToString();
    }

    public SymbolicProofStatus Status { get; }

    public SymbolicProofBackend Backend { get; }

    public SymbolicUnknownReason UnknownReason { get; }

    public string Reason { get; }

    public SymbolicUnknownReasonInfo UnknownReasonInfo { get; }

    public bool CacheHit { get; }

    public SymbolicBudgetInfo? Budget { get; }

    public string Target { get; }

    public string ConditionText { get; }

    public string DisplayKind { get; }
}

public sealed class SymbolicFactInfo
{
    public SymbolicFactInfo(
        string kind,
        string text,
        string provenance,
        string confidence,
        int sourceSpanStart,
        int sourceSpanLength,
        string? symbolKey = null,
        string? evidenceKey = null)
    {
        Kind = kind ?? string.Empty;
        Text = text ?? string.Empty;
        Provenance = provenance ?? string.Empty;
        Confidence = confidence ?? string.Empty;
        SourceSpanStart = sourceSpanStart;
        SourceSpanLength = sourceSpanLength;
        SymbolKey = symbolKey;
        EvidenceKey = evidenceKey;
    }

    public string Kind { get; }

    public string Text { get; }

    public string Provenance { get; }

    public string Confidence { get; }

    public int SourceSpanStart { get; }

    public int SourceSpanLength { get; }

    public string? SymbolKey { get; }

    public string? EvidenceKey { get; }

    internal static SymbolicFactInfo FromFact(SymbolicFact fact)
    {
        if (fact == null) throw new ArgumentNullException(nameof(fact));

        var atomKind = fact.Atom.GetType().Name;
        return new SymbolicFactInfo(
            atomKind,
            FormatFactText(fact),
            fact.Provenance,
            fact.Confidence.ToString(),
            fact.SourceSpan.Start,
            fact.SourceSpan.Length,
            fact.Symbol?.ToDisplayString(),
            fact.EvidenceKey);
    }

    internal static IReadOnlyList<SymbolicFactInfo> FromState(SymbolicState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        var facts = new List<SymbolicFactInfo>(state.Facts.Length + state.PathConditions.Length);
        foreach (var fact in state.Facts) facts.Add(FromFact(fact));

        foreach (var condition in state.PathConditions) AddConditionFacts(facts, condition);

        return facts;
    }

    internal static IReadOnlyList<SymbolicFactInfo> Distinct(IEnumerable<SymbolicFactInfo> facts)
    {
        if (facts == null) throw new ArgumentNullException(nameof(facts));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<SymbolicFactInfo>();
        foreach (var fact in facts)
        {
            if (fact == null) continue;

            var key = string.Join(
                "\u001f",
                fact.Kind,
                fact.Text,
                fact.Provenance,
                fact.Confidence,
                fact.SourceSpanStart.ToString(CultureInfo.InvariantCulture),
                fact.SourceSpanLength.ToString(CultureInfo.InvariantCulture),
                fact.SymbolKey ?? string.Empty,
                fact.EvidenceKey ?? string.Empty);
            if (seen.Add(key)) distinct.Add(fact);
        }

        return distinct;
    }

    private static void AddConditionFacts(ICollection<SymbolicFactInfo> facts, SymbolicCondition condition)
    {
        switch (condition)
        {
            case SymbolicFactCondition factCondition:
                facts.Add(FromFact(factCondition.Fact));
                break;
            case SymbolicNotCondition { Operand: SymbolicFactCondition factCondition }:
                facts.Add(FromFact(factCondition.Fact.Negate()));
                break;
            case SymbolicBinaryCondition binaryCondition:
                AddConditionFacts(facts, binaryCondition.Left);
                AddConditionFacts(facts, binaryCondition.Right);
                break;
        }
    }

    private static string FormatFactText(SymbolicFact fact)
    {
        if (fact.Confidence != SymbolicFactConfidence.Exact &&
            fact.Atom is SymbolicExceptionPreconditionAtom precondition)
            return FormatUnsupportedExceptionPrecondition(precondition);

        var text = fact.Confidence == SymbolicFactConfidence.Exact &&
                   SymbolicIrFormulaEncoder.TryEncode(fact.Atom, out var formula)
            ? SymbolicFormulaDisplay.Format(formula)
            : fact.Atom.ToString();
        return fact.Polarity ? text : "!(" + text + ")";
    }

    private static string FormatUnsupportedExceptionPrecondition(SymbolicExceptionPreconditionAtom precondition)
    {
        if (precondition.Subject != null &&
            SymbolicIrFormulaEncoder.TryEncodeTerm(precondition.Subject, out var subjectFormula))
            return "unknown(" + precondition.Kind + " trigger for " +
                   SymbolicFormulaDisplay.Format(subjectFormula) + ")";

        return "unknown(" + precondition.Kind + " trigger)";
    }
}

public sealed class SymbolicInvariantInfo
{
    public SymbolicInvariantInfo(
        string mergedText,
        IReadOnlyList<SymbolicFactInfo>? facts = null,
        IReadOnlyList<SymbolicProofInfo>? proofs = null,
        SymbolicInvariantMergeKind mergeKind = SymbolicInvariantMergeKind.Conjunction,
        int conditionCount = 0)
    {
        MergedText = mergedText ?? string.Empty;
        Facts = facts ?? Array.Empty<SymbolicFactInfo>();
        Proofs = proofs ?? Array.Empty<SymbolicProofInfo>();
        MergeKind = mergeKind;
        ConditionCount = conditionCount;
    }

    public string MergedText { get; }

    public SymbolicInvariantMergeKind MergeKind { get; }

    public int ConditionCount { get; }

    public IReadOnlyList<SymbolicFactInfo> Facts { get; }

    public IReadOnlyList<SymbolicProofInfo> Proofs { get; }
}
