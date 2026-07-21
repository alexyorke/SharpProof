using System.Text.Json.Serialization;

namespace SharpProof.Symbolic;

internal sealed class SymbolicInvariantService {
    internal SymbolicProgramPointQueryContext Analyze(
        SemanticModel semanticModel,
        int position,
        SyntaxNode node,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicState? initialState = null) =>
        new(
            semanticModel,
            position,
            node,
            node is ForStatementSyntax forStatement
            ? AnalyzeForInitialEntry(forStatement, semanticModel, smtAnalysis, cancellationToken)
            : AnalyzeAt(
                node,
                semanticModel,
                smtAnalysis,
                cancellationToken,
                includeCurrentStatementCompletionFacts,
                initialState));

    public SymbolicProgramPointAnalysis AnalyzeAt(
        SyntaxNode site,
        SemanticModel semanticModel,
        SmtAnalysisService? smtAnalysis = null,
        CancellationToken cancellationToken = default,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicState? initialState = null) {
        var point = CollectProgramPoint(
            site,
            semanticModel,
            cancellationToken,
            includeCurrentStatementCompletionFacts,
            initialState);
        return CreateAnalysis(
            point.Position,
            point.Formulas,
            point.PathState,
            smtAnalysis,
            site,
            point.Truncation);
    }

    public SymbolicProgramPointAnalysis AnalyzeForInitialEntry(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        SmtAnalysisService? smtAnalysis = null,
        CancellationToken cancellationToken = default) {
        using var limitScope = SymbolicAnalysisLimitContext.Push(SymbolicAnalysisLimitContext.Limits);
        var pathState = SymbolicReachabilityService.CollectForInitialEntryState(
            forStatement,
            semanticModel,
            cancellationToken);
        var formulas = EncodePathState(pathState);

        return CreateAnalysis(
            forStatement.SpanStart,
            formulas,
            pathState,
            smtAnalysis,
            forStatement,
            limitScope.Snapshot());
    }

    private static IReadOnlyList<SmtFormula> EncodePathState(SymbolicState pathState) {
        pathState = SymbolicProofStateFacts.NormalizeState(pathState);
        return SymbolicProofEncoder.EncodeState(pathState) is { Success: true } encoded
            ? encoded.PathConditions
            : [];
    }

    private static CollectedProgramPoint CollectProgramPoint(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeCurrentStatementCompletionFacts,
        SymbolicState? initialState) {
        using var limitScope = SymbolicAnalysisLimitContext.Push(SymbolicAnalysisLimitContext.Limits);
        var pathState = SymbolicReachabilityService.CollectPathStateAt(
            site,
            semanticModel,
            cancellationToken,
            initialState,
            includeCurrentStatementCompletionFacts);
        return new CollectedProgramPoint(
            site.SpanStart,
            pathState,
            EncodePathState(pathState),
            limitScope.Snapshot());
    }

    private static SymbolicProgramPointAnalysis CreateAnalysis(
        int spanStart,
        IReadOnlyList<SmtFormula> formulas,
        SymbolicState pathState,
        SmtAnalysisService? smtAnalysis,
        SyntaxNode sourceNode,
        SymbolicAnalysisTruncationInfo truncation) {
        formulas = FlattenProjectedConjunctions(formulas);
        if (formulas.Count == 0 &&
            pathState.IsContradictory)
            formulas = new[] { new SmtBooleanConstant(false) };

        var shouldCheckState = (pathState.Facts.Length != 0 || pathState.PathConditions.Length != 0) || formulas.Count != 0;
        var stateProof = smtAnalysis == null || !shouldCheckState
            ? null
            : new SymbolicProofService(smtAnalysis).ClassifyReachability(pathState);
        if (stateProof?.Status == SymbolicProofStatus.Unreachable)
            return new SymbolicProgramPointAnalysis(
                spanStart,
                formulas,
                pathState,
                SymbolicReachability.Unreachable,
                stateProof.Reason,
                SymbolicSmtDiagnostics.FromService(smtAnalysis),
                sourceNode,
                truncation,
                stateProof.RawResult);

        if (formulas.Count == 0) {
            if (stateProof != null)
                return new SymbolicProgramPointAnalysis(
                    spanStart,
                    formulas,
                    pathState,
                    MapReachability(stateProof.Status),
                    stateProof.Reason,
                    SymbolicSmtDiagnostics.FromService(smtAnalysis),
                    sourceNode,
                    truncation,
                    stateProof.RawResult);

            return new SymbolicProgramPointAnalysis(
                spanStart,
                formulas,
                pathState,
                SymbolicReachability.Reachable,
                "no_path_conditions",
                SymbolicSmtDiagnostics.FromService(smtAnalysis),
                sourceNode,
                truncation);
        }

        return new SymbolicProgramPointAnalysis(
            spanStart,
            formulas,
            pathState,
            stateProof == null ? SymbolicReachability.NotChecked : MapReachability(stateProof.Status),
            stateProof?.Reason ?? "reachability_not_checked",
            SymbolicSmtDiagnostics.FromService(smtAnalysis),
            sourceNode,
            truncation,
            stateProof?.RawResult);
    }

    private static IReadOnlyList<SmtFormula> FlattenProjectedConjunctions(IEnumerable<SmtFormula> formulas) {
        var projected = new List<SmtFormula>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(SmtFormula formula) {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } conjunction) {
                Add(conjunction.Left);
                Add(conjunction.Right);
                return;
            }

            if (seen.Add(SmtFormulaStructuralKey.Create(formula))) projected.Add(formula);
        }

        foreach (var formula in formulas)
            if (formula != null)
                Add(formula);

        return projected;
    }

    private static SymbolicReachability MapReachability(SymbolicProofStatus status) {
        return status switch {
            SymbolicProofStatus.Reachable => SymbolicReachability.Reachable,
            SymbolicProofStatus.Unreachable => SymbolicReachability.Unreachable,
            SymbolicProofStatus.Unknown => SymbolicReachability.Unknown,
            _ => SymbolicReachability.NotChecked
        };
    }

    readonly record struct CollectedProgramPoint(
        int Position,
        SymbolicState PathState,
        IReadOnlyList<SmtFormula> Formulas,
        SymbolicAnalysisTruncationInfo Truncation);
}

internal sealed record SymbolicInvariantFactSummary(IReadOnlyList<string> Facts) {
    public string MergedInvariantText { get; } = FormatMergedInvariantFacts(Facts);

    internal static SymbolicInvariantFactSummary Merge(IEnumerable<IEnumerable<string>> factSets) {
        if (factSets == null) throw new ArgumentNullException(nameof(factSets));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var facts = new List<string>();
        foreach (var factSet in factSets) {
            if (factSet == null) continue;

            foreach (var fact in factSet)
                if (!string.IsNullOrWhiteSpace(fact) && seen.Add(fact))
                    facts.Add(fact);
        }

        return new SymbolicInvariantFactSummary(facts);
    }

    internal static string FormatMergedInvariantFacts(IReadOnlyList<string> facts) {
        if (facts == null) throw new ArgumentNullException(nameof(facts));
        return facts.Count switch {
            0 => "true",
            1 => facts[0],
            _ => string.Join(" && ", facts.Select(static fact => "(" + fact + ")"))
        };
    }
}

internal sealed record SymbolicProgramPointAnalysis(
    int SpanStart,
    [property: JsonIgnore] IReadOnlyList<SmtFormula> PathConditions,
    SymbolicState PathState,
    SymbolicReachability Reachability,
    string ReachabilityReason,
    SymbolicSmtDiagnostics SmtDiagnostics,
    [property: JsonIgnore] SyntaxNode SourceNode,
    [property: JsonIgnore] SymbolicAnalysisTruncationInfo AnalysisTruncation,
    [property: JsonIgnore] AnalysisProofResult? ReachabilityProof = null) {
    internal SymbolicProgramPointAnalysis(
        int spanStart,
        IReadOnlyList<SmtFormula> pathConditions,
        SymbolicState pathState,
        SymbolicReachability reachability,
        string reachabilityReason,
        SymbolicSmtDiagnostics smtDiagnostics,
        SyntaxNode sourceNode)
        : this(spanStart, pathConditions, pathState, reachability, reachabilityReason, smtDiagnostics, sourceNode,
            SymbolicAnalysisTruncationInfo.None) {
    }

    public IReadOnlyList<string> Facts { get; } = PathConditions.Select(SymbolicFormulaDisplay.Format).ToArray();

    public string MergedInvariantText { get; } = SymbolicFormulaDisplay.FormatMergedInvariant(PathConditions);

    public SymbolicAnalysisTruncationInfo Truncation => AnalysisTruncation;

}

internal sealed record SymbolicSmtDiagnostics(
    bool IsConfigured,
    SmtAnalysisMode Mode,
    bool IsEnabled,
    int QueryTimeoutMs,
    int MethodBudgetMs,
    int MaxPathConditions,
    int MaxExpressionNodes,
    int ExecutedQueryCount,
    int CacheEntryCount,
    SmtAnalysisHealth Health,
    SmtSolverLifecycleOptions Lifecycle) {
    public static readonly SymbolicSmtDiagnostics NotConfigured = new(
        false,
        SmtAnalysisMode.Off,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        new SmtAnalysisHealth(
            SmtAnalysisHealthState.Disabled,
            string.Empty,
            0,
            0,
            0,
            0,
            0),
        SmtSolverLifecycleOptions.Default);

    internal SymbolicSmtDiagnostics Snapshot => this;

    public static SymbolicSmtDiagnostics FromService(SmtAnalysisService? smtAnalysis) {
        if (smtAnalysis == null) return NotConfigured;

        return new SymbolicSmtDiagnostics(
            true,
            smtAnalysis.Options.Mode,
            smtAnalysis.Options.IsEnabled,
            ToBoundedMilliseconds(smtAnalysis.Options.QueryTimeout),
            ToBoundedMilliseconds(smtAnalysis.Options.MethodBudget),
            smtAnalysis.Options.MaxPathConditions,
            smtAnalysis.Options.MaxExpressionNodes,
            smtAnalysis.ExecutedQueryCount,
            smtAnalysis.CacheEntryCount,
            smtAnalysis.Health,
            smtAnalysis.Options.Lifecycle);
    }

    internal static int ToBoundedMilliseconds(TimeSpan value) {
        var totalMilliseconds = value.TotalMilliseconds;
        if (totalMilliseconds >= int.MaxValue) return int.MaxValue;

        if (totalMilliseconds <= int.MinValue) return int.MinValue;

        return (int)totalMilliseconds;
    }

}

internal enum SymbolicReachability {
    NotChecked,
    Unknown,
    Reachable,
    Unreachable
}
