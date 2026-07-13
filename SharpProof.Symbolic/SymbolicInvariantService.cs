using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Purity;
using SearchLib.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicInvariantService
{
    public SymbolicInvariantSnapshot GetInvariantsAt(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default,
        bool includeCurrentStatementCompletionFacts = false)
    {
        var point = CollectProgramPoint(
            site,
            semanticModel,
            cancellationToken,
            includeCurrentStatementCompletionFacts,
            null);
        var facts = FormatFacts(point.Formulas);
        var mergedInvariantText = FormatMergedInvariant(point.Formulas);

        return new SymbolicInvariantSnapshot(point.Position, facts, mergedInvariantText, point.Truncation);
    }

    public SymbolicProgramPointAnalysis AnalyzeAt(
        SyntaxNode site,
        SemanticModel semanticModel,
        SmtAnalysisService? smtAnalysis = null,
        CancellationToken cancellationToken = default,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicState? initialState = null)
    {
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
        CancellationToken cancellationToken = default)
    {
        using var limitScope = SymbolicAnalysisLimitContext.Push(SymbolicAnalysisLimitContext.Limits);
        var pathState = SymbolicProgramPointFacts.CollectForInitialEntryState(
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

    private static IReadOnlyList<SmtFormula> EncodePathState(SymbolicState pathState)
    {
        return SymbolicProofService.TryEncodeStatePathConditions(pathState, out var pathConditions)
            ? pathConditions
            : Array.Empty<SmtFormula>();
    }

    private static CollectedProgramPoint CollectProgramPoint(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeCurrentStatementCompletionFacts,
        SymbolicState? initialState)
    {
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

    public SymbolicInvariantImplicationResult ProveImplicationAt(
        SyntaxNode site,
        SemanticModel semanticModel,
        SymbolicCondition condition,
        SmtAnalysisService? smtAnalysis,
        CancellationToken cancellationToken = default,
        bool includeCurrentStatementCompletionFacts = false)
    {
        if (site == null) throw new ArgumentNullException(nameof(site));

        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        var analysis = AnalyzeAt(
            site,
            semanticModel,
            smtAnalysis,
            cancellationToken,
            includeCurrentStatementCompletionFacts);
        return ProveImplication(analysis, condition, smtAnalysis);
    }

    public static SymbolicInvariantImplicationResult ProveImplication(
        SymbolicProgramPointAnalysis analysis,
        SymbolicCondition condition,
        SmtAnalysisService? smtAnalysis)
    {
        if (analysis == null) throw new ArgumentNullException(nameof(analysis));

        if (condition == null) throw new ArgumentNullException(nameof(condition));

        var conditionText = FormatCondition(condition);
        if (smtAnalysis == null)
            return new SymbolicInvariantImplicationResult(
                analysis.SpanStart,
                conditionText,
                SymbolicTruthValue.Unknown,
                "smt_required",
                analysis.Reachability,
                analysis.ReachabilityReason,
                analysis.SmtDiagnostics);

        if (analysis.Reachability == SymbolicReachability.Unreachable)
            return new SymbolicInvariantImplicationResult(
                analysis.SpanStart,
                conditionText,
                SymbolicTruthValue.Unreachable,
                analysis.ReachabilityReason,
                analysis.Reachability,
                analysis.ReachabilityReason,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));

        var truthProof = SymbolicReachabilityService.ClassifyStateConditionTruth(
            analysis.PathState,
            condition,
            smtAnalysis);
        if (truthProof.Info.Status == SymbolicProofStatus.ProvenTrue)
            return new SymbolicInvariantImplicationResult(
                analysis.SpanStart,
                conditionText,
                SymbolicTruthValue.ProvenTrue,
                truthProof.Info.Reason,
                analysis.Reachability,
                analysis.ReachabilityReason,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));

        if (truthProof.Info.Status == SymbolicProofStatus.ProvenFalse)
            return new SymbolicInvariantImplicationResult(
                analysis.SpanStart,
                conditionText,
                SymbolicTruthValue.ProvenFalse,
                truthProof.Info.Reason,
                analysis.Reachability,
                analysis.ReachabilityReason,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));

        return new SymbolicInvariantImplicationResult(
            analysis.SpanStart,
            conditionText,
            SymbolicTruthValue.Unknown,
            truthProof.Info.Reason,
            analysis.Reachability,
            analysis.ReachabilityReason,
            SymbolicSmtDiagnostics.FromService(smtAnalysis));
    }

    internal static string FormatCondition(SymbolicCondition condition)
    {
        return SymbolicIrFormulaEncoder.TryEncode(condition, out var formula)
            ? SymbolicFormulaDisplay.Format(formula)
            : condition.ToString() ?? string.Empty;
    }

    internal static string FormatMergedInvariant(IReadOnlyList<SmtFormula> pathConditions)
    {
        return SymbolicFormulaDisplay.FormatMergedInvariant(pathConditions);
    }

    public static SymbolicInvariantFactSummary MergeInvariantFacts(IEnumerable<IEnumerable<string>> factSets)
    {
        if (factSets == null) throw new ArgumentNullException(nameof(factSets));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var facts = new List<string>();
        foreach (var factSet in factSets)
        {
            if (factSet == null) continue;

            foreach (var fact in factSet)
                if (!string.IsNullOrWhiteSpace(fact) && seen.Add(fact))
                    facts.Add(fact);
        }

        return new SymbolicInvariantFactSummary(facts);
    }

    public static string FormatMergedInvariantFacts(IReadOnlyList<string> facts)
    {
        if (facts == null) throw new ArgumentNullException(nameof(facts));

        if (facts.Count == 0) return "true";

        if (facts.Count == 1) return facts[0];

        return string.Join(" && ", facts.Select(static fact => "(" + fact + ")"));
    }

    private static IReadOnlyList<string> FormatFacts(IEnumerable<SmtFormula> formulas)
    {
        return FlattenProjectedConjunctions(formulas)
            .Select(static fact => SymbolicFormulaDisplay.Format(fact))
            .ToArray();
    }

    private static SymbolicProgramPointAnalysis CreateAnalysis(
        int spanStart,
        IReadOnlyList<SmtFormula> formulas,
        SymbolicState pathState,
        SmtAnalysisService? smtAnalysis,
        SyntaxNode sourceNode,
        SymbolicAnalysisTruncationInfo truncation)
    {
        formulas = FlattenProjectedConjunctions(formulas);
        if (formulas.Count == 0 &&
            pathState.IsContradictory)
            formulas = new[] { new SmtBooleanConstant(false) };

        var shouldCheckState = HasPathStateFacts(pathState) || formulas.Count != 0;
        var stateProof = smtAnalysis == null || !shouldCheckState
            ? null
            : SymbolicReachabilityService.ClassifyStateFeasibility(pathState, smtAnalysis);
        if (stateProof?.Info.Status == SymbolicProofStatus.Unreachable)
            return new SymbolicProgramPointAnalysis(
                spanStart,
                formulas,
                pathState,
                SymbolicReachability.Unreachable,
                stateProof.Info.Reason,
                SymbolicSmtDiagnostics.FromService(smtAnalysis),
                sourceNode,
                stateProof.RawResult,
                truncation);

        if (formulas.Count == 0)
        {
            if (stateProof != null)
                return new SymbolicProgramPointAnalysis(
                    spanStart,
                    formulas,
                    pathState,
                    MapReachability(stateProof.Info.Status),
                    stateProof.Info.Reason,
                    SymbolicSmtDiagnostics.FromService(smtAnalysis),
                    sourceNode,
                    stateProof.RawResult,
                    truncation);

            return new SymbolicProgramPointAnalysis(
                spanStart,
                formulas,
                pathState,
                SymbolicReachability.Reachable,
                "no_path_conditions",
                SymbolicSmtDiagnostics.FromService(smtAnalysis),
                sourceNode,
                truncation: truncation);
        }

        return new SymbolicProgramPointAnalysis(
            spanStart,
            formulas,
            pathState,
            stateProof == null ? SymbolicReachability.NotChecked : MapReachability(stateProof.Info.Status),
            stateProof?.Info.Reason ?? "reachability_not_checked",
            SymbolicSmtDiagnostics.FromService(smtAnalysis),
            sourceNode,
            stateProof?.RawResult,
            truncation);
    }

    private static IReadOnlyList<SmtFormula> FlattenProjectedConjunctions(IEnumerable<SmtFormula> formulas)
    {
        var projected = new List<SmtFormula>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(SmtFormula formula)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } conjunction)
            {
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

    private static bool HasPathStateFacts(SymbolicState pathState)
    {
        return pathState.Facts.Length != 0 || pathState.PathConditions.Length != 0;
    }

    private static SymbolicReachability MapReachability(SymbolicProofStatus status)
    {
        return status switch
        {
            SymbolicProofStatus.Reachable => SymbolicReachability.Reachable,
            SymbolicProofStatus.Unreachable => SymbolicReachability.Unreachable,
            SymbolicProofStatus.Unknown => SymbolicReachability.Unknown,
            _ => SymbolicReachability.NotChecked
        };
    }

    private readonly record struct CollectedProgramPoint(
        int Position,
        SymbolicState PathState,
        IReadOnlyList<SmtFormula> Formulas,
        SymbolicAnalysisTruncationInfo Truncation);
}

internal sealed class SymbolicInvariantSnapshot
{
    internal SymbolicInvariantSnapshot(
        int spanStart,
        IReadOnlyList<string> facts,
        string mergedInvariantText,
        SymbolicAnalysisTruncationInfo? truncation = null)
    {
        SpanStart = spanStart;
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        MergedInvariantText = mergedInvariantText ?? throw new ArgumentNullException(nameof(mergedInvariantText));
        Truncation = truncation ?? SymbolicAnalysisTruncationInfo.None;
    }

    public int SpanStart { get; }

    public IReadOnlyList<string> Facts { get; }

    public string MergedInvariantText { get; }

    public SymbolicAnalysisTruncationInfo Truncation { get; }
}

public sealed class SymbolicInvariantFactSummary
{
    public SymbolicInvariantFactSummary(IReadOnlyList<string> facts)
    {
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        MergedInvariantText = SymbolicInvariantService.FormatMergedInvariantFacts(facts);
    }

    public IReadOnlyList<string> Facts { get; }

    public string MergedInvariantText { get; }
}

internal sealed class SymbolicInvariantImplicationResult
{
    public SymbolicInvariantImplicationResult(
        int spanStart,
        string condition,
        SymbolicTruthValue truthValue,
        string reason,
        SymbolicReachability reachability,
        string reachabilityReason,
        SymbolicSmtDiagnostics smtDiagnostics)
    {
        SpanStart = spanStart;
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        TruthValue = truthValue;
        Reason = reason ?? string.Empty;
        Reachability = reachability;
        ReachabilityReason = reachabilityReason ?? string.Empty;
        SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
    }

    public int SpanStart { get; }

    public string Condition { get; }

    public SymbolicTruthValue TruthValue { get; }

    public string Reason { get; }

    public SymbolicReachability Reachability { get; }

    public string ReachabilityReason { get; }

    public SymbolicSmtDiagnostics SmtDiagnostics { get; }
}

internal sealed class SymbolicProgramPointAnalysis
{
    public SymbolicProgramPointAnalysis(
        int spanStart,
        IReadOnlyList<SmtFormula> pathConditions,
        SymbolicState? pathState,
        SymbolicReachability reachability,
        string reachabilityReason,
        SymbolicSmtDiagnostics? smtDiagnostics,
        SyntaxNode sourceNode,
        PurityProofResult? reachabilityProof = null,
        SymbolicAnalysisTruncationInfo? truncation = null)
    {
        SpanStart = spanStart;
        PathConditions = pathConditions;
        PathState = pathState ?? new SymbolicState();
        SourceNode = sourceNode ?? throw new ArgumentNullException(nameof(sourceNode));
        Facts = pathConditions.Select(SymbolicFormulaDisplay.Format).ToArray();
        MergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(pathConditions);
        Reachability = reachability;
        ReachabilityReason = reachabilityReason;
        ReachabilityProof = reachabilityProof;
        SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        Truncation = truncation ?? SymbolicAnalysisTruncationInfo.None;
    }

    public int SpanStart { get; }

    internal IReadOnlyList<SmtFormula> PathConditions { get; }

    public SymbolicState PathState { get; }

    internal SyntaxNode SourceNode { get; }

    public IReadOnlyList<string> Facts { get; }

    public string MergedInvariantText { get; }

    public SymbolicReachability Reachability { get; }

    public string ReachabilityReason { get; }

    internal PurityProofResult? ReachabilityProof { get; }

    public SymbolicSmtDiagnostics SmtDiagnostics { get; }

    public SymbolicAnalysisTruncationInfo Truncation { get; }
}

public sealed class SymbolicSmtDiagnostics
{
    private readonly SymbolicSmtDiagnosticsSnapshot snapshot;

    public static readonly SymbolicSmtDiagnostics NotConfigured = new(
        false,
        SmtAnalysisMode.Off,
        false,
        0,
        0,
        0,
        0,
        0,
        0);

    public SymbolicSmtDiagnostics(
        bool isConfigured,
        SmtAnalysisMode mode,
        bool isEnabled,
        int queryTimeoutMs,
        int methodBudgetMs,
        int maxPathConditions,
        int maxExpressionNodes,
        int executedQueryCount,
        int cacheEntryCount)
        : this(new SymbolicSmtDiagnosticsSnapshot(
            isConfigured,
            mode,
            isEnabled,
            queryTimeoutMs,
            methodBudgetMs,
            maxPathConditions,
            maxExpressionNodes,
            executedQueryCount,
            cacheEntryCount,
            new SmtAnalysisHealth(
                isConfigured && isEnabled ? SmtAnalysisHealthState.Ready : SmtAnalysisHealthState.Disabled,
                string.Empty,
                0,
                0,
                0,
                0,
                0),
            SmtSolverLifecycleOptions.Default))
    {
    }

    private SymbolicSmtDiagnostics(SymbolicSmtDiagnosticsSnapshot snapshot)
    {
        this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public bool IsConfigured => snapshot.IsConfigured;

    public SmtAnalysisMode Mode => snapshot.Mode;

    public bool IsEnabled => snapshot.IsEnabled;

    public int QueryTimeoutMs => snapshot.QueryTimeoutMs;

    public int MethodBudgetMs => snapshot.MethodBudgetMs;

    public int MaxPathConditions => snapshot.MaxPathConditions;

    public int MaxExpressionNodes => snapshot.MaxExpressionNodes;

    public int ExecutedQueryCount => snapshot.ExecutedQueryCount;

    public int CacheEntryCount => snapshot.CacheEntryCount;

    public SmtAnalysisHealth Health => snapshot.Health;

    public SmtSolverLifecycleOptions Lifecycle => snapshot.Lifecycle;

    internal SymbolicSmtDiagnosticsSnapshot Snapshot => snapshot;

    public static SymbolicSmtDiagnostics FromService(SmtAnalysisService? smtAnalysis)
    {
        if (smtAnalysis == null) return NotConfigured;

        return new SymbolicSmtDiagnostics(new SymbolicSmtDiagnosticsSnapshot(
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
            smtAnalysis.Options.Lifecycle));
    }

    internal static int ToBoundedMilliseconds(TimeSpan value)
    {
        var totalMilliseconds = value.TotalMilliseconds;
        if (totalMilliseconds >= int.MaxValue) return int.MaxValue;

        if (totalMilliseconds <= int.MinValue) return int.MinValue;

        return (int)totalMilliseconds;
    }

}

internal sealed class SymbolicSmtDiagnosticsSnapshot
{
    public SymbolicSmtDiagnosticsSnapshot(
        bool isConfigured,
        SmtAnalysisMode mode,
        bool isEnabled,
        int queryTimeoutMs,
        int methodBudgetMs,
        int maxPathConditions,
        int maxExpressionNodes,
        int executedQueryCount,
        int cacheEntryCount,
        SmtAnalysisHealth health,
        SmtSolverLifecycleOptions lifecycle)
    {
        IsConfigured = isConfigured;
        Mode = mode;
        IsEnabled = isEnabled;
        QueryTimeoutMs = queryTimeoutMs;
        MethodBudgetMs = methodBudgetMs;
        MaxPathConditions = maxPathConditions;
        MaxExpressionNodes = maxExpressionNodes;
        ExecutedQueryCount = executedQueryCount;
        CacheEntryCount = cacheEntryCount;
        Health = health ?? throw new ArgumentNullException(nameof(health));
        Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    public bool IsConfigured { get; }

    public SmtAnalysisMode Mode { get; }

    public bool IsEnabled { get; }

    public int QueryTimeoutMs { get; }

    public int MethodBudgetMs { get; }

    public int MaxPathConditions { get; }

    public int MaxExpressionNodes { get; }

    public int ExecutedQueryCount { get; }

    public int CacheEntryCount { get; }

    public SmtAnalysisHealth Health { get; }

    public SmtSolverLifecycleOptions Lifecycle { get; }
}

public enum SymbolicReachability
{
    NotChecked,
    Unknown,
    Reachable,
    Unreachable
}
