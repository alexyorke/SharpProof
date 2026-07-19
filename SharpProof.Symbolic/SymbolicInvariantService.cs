namespace SharpProof.Symbolic;

internal sealed class SymbolicInvariantService
{
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

    private static IReadOnlyList<SmtFormula> EncodePathState(SymbolicState pathState)
    {
        pathState = SymbolicProofStateFacts.NormalizeState(pathState);
        return SymbolicProofEncoder.EncodeState(pathState) is { Success: true } encoded
            ? encoded.PathConditions
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
                stateProof.RawResult,
                truncation);

        if (formulas.Count == 0)
        {
            if (stateProof != null)
                return new SymbolicProgramPointAnalysis(
                    spanStart,
                    formulas,
                    pathState,
                    MapReachability(stateProof.Status),
                    stateProof.Reason,
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
            stateProof == null ? SymbolicReachability.NotChecked : MapReachability(stateProof.Status),
            stateProof?.Reason ?? "reachability_not_checked",
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

internal sealed class SymbolicInvariantFactSummary(IReadOnlyList<string> facts)
{
    public IReadOnlyList<string> Facts { get; } = facts ?? throw new ArgumentNullException(nameof(facts));

    public string MergedInvariantText { get; } = FormatMergedInvariantFacts(facts!);

    internal static SymbolicInvariantFactSummary Merge(IEnumerable<IEnumerable<string>> factSets)
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

    internal static string FormatMergedInvariantFacts(IReadOnlyList<string> facts)
    {
        if (facts == null) throw new ArgumentNullException(nameof(facts));
        return facts.Count switch
        {
            0 => "true",
            1 => facts[0],
            _ => string.Join(" && ", facts.Select(static fact => "(" + fact + ")"))
        };
    }
}

internal sealed class SymbolicProgramPointAnalysis(
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
    public int SpanStart { get; } = spanStart;

    internal IReadOnlyList<SmtFormula> PathConditions { get; } = pathConditions;

    public SymbolicState PathState { get; } = pathState ?? new SymbolicState();

    internal SyntaxNode SourceNode { get; } = sourceNode ?? throw new ArgumentNullException(nameof(sourceNode));

    public IReadOnlyList<string> Facts { get; } = pathConditions.Select(SymbolicFormulaDisplay.Format).ToArray();

    public string MergedInvariantText { get; } = SymbolicFormulaDisplay.FormatMergedInvariant(pathConditions);

    public SymbolicReachability Reachability { get; } = reachability;

    public string ReachabilityReason { get; } = reachabilityReason;

    internal PurityProofResult? ReachabilityProof { get; } = reachabilityProof;

    public SymbolicSmtDiagnostics SmtDiagnostics { get; } = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;

    public SymbolicAnalysisTruncationInfo Truncation { get; } = truncation ?? SymbolicAnalysisTruncationInfo.None;
}

internal sealed class SymbolicSmtDiagnostics
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

internal sealed class SymbolicSmtDiagnosticsSnapshot(
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
    public bool IsConfigured { get; } = isConfigured;

    public SmtAnalysisMode Mode { get; } = mode;

    public bool IsEnabled { get; } = isEnabled;

    public int QueryTimeoutMs { get; } = queryTimeoutMs;

    public int MethodBudgetMs { get; } = methodBudgetMs;

    public int MaxPathConditions { get; } = maxPathConditions;

    public int MaxExpressionNodes { get; } = maxExpressionNodes;

    public int ExecutedQueryCount { get; } = executedQueryCount;

    public int CacheEntryCount { get; } = cacheEntryCount;

    public SmtAnalysisHealth Health { get; } = health ?? throw new ArgumentNullException(nameof(health));

    public SmtSolverLifecycleOptions Lifecycle { get; } = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
}

internal enum SymbolicReachability
{
    NotChecked,
    Unknown,
    Reachable,
    Unreachable
}
