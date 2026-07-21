using System.Text.Json.Serialization;

namespace SharpProof.Symbolic;

internal enum SymbolicProofBackend {
    None,
    Syntactic,
    Smt
}

internal enum SymbolicProofStatus {
    Unknown,
    Reachable,
    Unreachable,
    ProvenTrue,
    ProvenFalse
}

internal enum SymbolicProofStage {
    None,
    Lowering,
    Normalization,
    SyntacticClassification,
    Budgeting,
    SmtExecution,
    ResultMapping
}

internal enum SymbolicProofSupport {
    Exact,
    Approximate,
    Unsupported
}

internal enum SymbolicUnknownReason {
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

internal sealed record SymbolicBudgetInfo(
    int MaxPathConditions,
    int MaxExpressionNodes,
    int TimeoutMilliseconds,
    int MethodBudgetMilliseconds,
    int ExecutedQueryCount,
    int CacheEntryCount,
    SymbolicCacheInfo? Cache = null);

internal sealed record SymbolicCacheInfo(long Hits, long Misses, int Entries, long Evictions);

internal sealed record SymbolicProofInfo(
    SymbolicProofStatus Status,
    SymbolicProofBackend Backend,
    SymbolicUnknownReason UnknownReason,
    string Reason,
    bool CacheHit,
    SymbolicBudgetInfo? Budget,
    SymbolicProofStage Stage,
    SymbolicProofSupport Support,
    string? Target = null,
    string? ConditionText = null,
    string? DisplayKind = null) {
    public SymbolicProofStatus Status { get; init; } = Status;
    public SymbolicProofBackend Backend { get; init; } = Backend;
    public SymbolicUnknownReason UnknownReason { get; init; } = UnknownReason;
    public string Reason { get; init; } = Reason ?? string.Empty;

    public SymbolicUnknownReasonInfo UnknownReasonInfo =>
        SymbolicUnknownReasonTaxonomy.ForProof(UnknownReason, Reason);

    public bool CacheHit { get; init; } = CacheHit;
    public SymbolicBudgetInfo? Budget { get; init; } = Budget;
    public SymbolicProofStage Stage { get; init; } = Stage;
    public SymbolicProofSupport Support { get; init; } = Support;
    public string Target { get; init; } = Target ?? string.Empty;

    public string ConditionText { get; init; } = ConditionText ?? string.Empty;

    public string DisplayKind { get; init; } = DisplayKind ?? Backend.ToString();

    internal AnalysisProofResult? RawResult { get; init; }

    internal static SymbolicProofInfo Unknown(
        SymbolicUnknownReason reason,
        SymbolicProofStage stage = SymbolicProofStage.Lowering,
        SymbolicProofSupport support = SymbolicProofSupport.Unsupported,
        string? detail = null) => new(
        SymbolicProofStatus.Unknown,
        SymbolicProofBackend.None,
        reason,
        detail ?? reason.ToString(),
        false,
        null,
        stage,
        support);

    internal static SymbolicProofInfo Syntactic(SymbolicProofStatus status, string reason) => new(
        status,
        SymbolicProofBackend.Syntactic,
        SymbolicUnknownReason.None,
        reason,
        false,
        null,
        SymbolicProofStage.SyntacticClassification,
        SymbolicProofSupport.Exact);

    internal SymbolicProofInfo WithCacheHit(SymbolicBudgetInfo? budget) => this with {
        CacheHit = true,
        Budget = budget ?? Budget
    };

    internal SymbolicProofInfo WithStatus(SymbolicProofStatus status, string? reason = null) => this with {
        Status = status,
        UnknownReason = status == SymbolicProofStatus.Unknown && UnknownReason == SymbolicUnknownReason.None
            ? SymbolicUnknownReason.Unknown
            : UnknownReason,
        Reason = reason ?? Reason
    };

    internal static SymbolicProofInfo FromReachability(
        AnalysisProofResult result,
        SymbolicBudgetInfo? budget) =>
        FromResult(
            result,
            result.PathCheck.Feasibility switch {
                Feasibility.Satisfiable => SymbolicProofStatus.Reachable,
                Feasibility.Unsatisfiable => SymbolicProofStatus.Unreachable,
                _ => SymbolicProofStatus.Unknown
            },
            budget);

    internal static SymbolicProofInfo FromImplication(
        AnalysisProofResult result,
        SymbolicBudgetInfo? budget) =>
        FromResult(
            result,
            result.Outcome switch {
                AnalysisProofOutcome.Proven => SymbolicProofStatus.ProvenTrue,
                AnalysisProofOutcome.Disproven => SymbolicProofStatus.ProvenFalse,
                _ => SymbolicProofStatus.Unknown
            },
            budget);

    internal static SymbolicProofInfo FromConditionTruth(
        AnalysisProofResult result,
        SymbolicProofStatus status,
        SymbolicBudgetInfo? budget) => FromResult(result, status, budget);

    private static SymbolicProofInfo FromResult(
        AnalysisProofResult result,
        SymbolicProofStatus status,
        SymbolicBudgetInfo? budget) => new(
        status,
        SymbolicProofBackend.Smt,
        status == SymbolicProofStatus.Unknown
            ? SymbolicUnknownReasonClassifier.Classify(result.Reason)
            : SymbolicUnknownReason.None,
        result.Reason,
        false,
        budget,
        status == SymbolicProofStatus.Unknown
            ? result.Reason is "smt_method_budget_exceeded" or
                "smt_path_condition_budget_exceeded" or
                "smt_expression_budget_exceeded" or "smt_disabled"
                ? SymbolicProofStage.Budgeting
                : SymbolicProofStage.SmtExecution
            : SymbolicProofStage.ResultMapping,
        SymbolicProofSupport.Exact)
    { RawResult = result };

    internal static SymbolicProofStatus MapStatus<TStatus>(TStatus value)
        where TStatus : struct, Enum => (object)value switch {
        SymbolicTruthValue.ProvenTrue or SymbolicConditionProofSummaryStatus.AlwaysTrue or
            SymbolicRuntimeHazardStatus.Proven => SymbolicProofStatus.ProvenTrue,
        SymbolicTruthValue.ProvenFalse or SymbolicConditionProofSummaryStatus.AlwaysFalse =>
            SymbolicProofStatus.ProvenFalse,
        SymbolicTruthValue.Unreachable or SymbolicConditionProofSummaryStatus.UnreachableOnly or
            SymbolicRuntimeHazardStatus.Unreachable => SymbolicProofStatus.Unreachable,
        _ => SymbolicProofStatus.Unknown
    };

    internal static SymbolicProofInfo Project(
        SymbolicProofStatus status,
        bool isSolverBacked,
        string reason,
        bool cacheHit,
        SymbolicBudgetInfo? budget,
        string? target = null,
        string? conditionText = null,
        string? displayKind = null,
        string? rawUnknownReason = null) {
        var backend = isSolverBacked ? SymbolicProofBackend.Smt :
            status == SymbolicProofStatus.Unknown ? SymbolicProofBackend.None : SymbolicProofBackend.Syntactic;
        var unknownReason = status == SymbolicProofStatus.Unknown && rawUnknownReason != null
            ? SymbolicUnknownReasonClassifier.Classify(rawUnknownReason)
            : SymbolicUnknownReason.None;
        return new SymbolicProofInfo(
            status,
            backend,
            unknownReason,
            reason,
            cacheHit,
            budget,
            backend switch {
                SymbolicProofBackend.Syntactic => SymbolicProofStage.SyntacticClassification,
                SymbolicProofBackend.Smt => SymbolicProofStage.ResultMapping,
                _ => SymbolicProofStage.None
            },
            unknownReason == SymbolicUnknownReason.UnsupportedIrEncoding
                ? SymbolicProofSupport.Unsupported
                : SymbolicProofSupport.Exact,
            target,
            conditionText,
            displayKind);
    }

    internal static SymbolicProofInfo Project(
        SymbolicProofStatus status,
        SymbolicProofInfo source,
        string reason,
        string? target = null,
        string? conditionText = null,
        string? displayKind = null) => source with {
        Status = status,
        UnknownReason = status == SymbolicProofStatus.Unknown
            ? source.UnknownReason
            : SymbolicUnknownReason.None,
        Reason = reason,
        Target = target ?? string.Empty,
        ConditionText = conditionText ?? string.Empty,
        DisplayKind = displayKind ?? source.Backend.ToString()
    };
}

internal sealed record SymbolicFactInfo(
    string Kind,
    string Text,
    string Provenance,
    string Confidence,
    int SourceSpanStart,
    int SourceSpanLength,
    string? SymbolKey = null,
    string? EvidenceKey = null) {
    internal static SymbolicFactInfo FromFact(SymbolicFact fact) {
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

    internal static IReadOnlyList<SymbolicFactInfo> FromState(SymbolicState state) {
        if (state == null) throw new ArgumentNullException(nameof(state));

        var facts = new List<SymbolicFactInfo>(state.Facts.Length + state.PathConditions.Length);
        foreach (var fact in state.Facts) facts.Add(FromFact(fact));

        foreach (var condition in state.PathConditions) AddConditionFacts(facts, condition);

        return facts;
    }

    internal static IReadOnlyList<SymbolicFactInfo> Distinct(IEnumerable<SymbolicFactInfo> facts) {
        if (facts == null) throw new ArgumentNullException(nameof(facts));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<SymbolicFactInfo>();
        foreach (var fact in facts) {
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

    private static void AddConditionFacts(ICollection<SymbolicFactInfo> facts, SymbolicCondition condition) {
        switch (condition) {
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

    private static string FormatFactText(SymbolicFact fact) {
        if (fact.Confidence != SymbolicFactConfidence.Exact &&
            fact.Atom is SymbolicExceptionPreconditionAtom precondition)
            return FormatUnsupportedExceptionPrecondition(precondition);

        var text = fact.Confidence == SymbolicFactConfidence.Exact &&
                   SymbolicIrFormulaEncoder.TryEncode(fact.Atom, out var formula)
            ? SymbolicFormulaDisplay.Format(formula)
            : fact.Atom.ToString();
        return fact.Polarity ? text : "!(" + text + ")";
    }

    private static string FormatUnsupportedExceptionPrecondition(SymbolicExceptionPreconditionAtom precondition) {
        if (precondition.Subject != null &&
            SymbolicIrFormulaEncoder.TryEncodeTerm(precondition.Subject, out var subjectFormula))
            return "unknown(" + precondition.Kind + " trigger for " +
                   SymbolicFormulaDisplay.Format(subjectFormula) + ")";

        return "unknown(" + precondition.Kind + " trigger)";
    }
}

internal sealed record SymbolicInvariantInfo(
    [property: JsonPropertyOrder(0)] string MergedText,
    [property: JsonPropertyOrder(3)] IReadOnlyList<SymbolicFactInfo> Facts,
    [property: JsonPropertyOrder(4)] IReadOnlyList<SymbolicProofInfo> Proofs,
    [property: JsonPropertyOrder(1)] SymbolicInvariantMergeKind MergeKind,
    [property: JsonPropertyOrder(2)] int ConditionCount);
