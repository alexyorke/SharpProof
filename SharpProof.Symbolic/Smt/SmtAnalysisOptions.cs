namespace SharpProof.Symbolic.Smt;

internal enum SmtAnalysisMode {
    Bounded,
    Deep
}
internal sealed record SmtAnalysisOptions(
    SmtAnalysisMode Mode,
    TimeSpan QueryTimeout,
    TimeSpan MethodBudget,
    int MaxPathConditions,
    int MaxExpressionNodes,
    bool UseSharedResultCache,
    SmtSolverLifecycleOptions Lifecycle) {
    public static readonly SmtAnalysisOptions Default = ForMode(SmtAnalysisMode.Bounded);

    public SmtAnalysisOptions(
        SmtAnalysisMode mode,
        TimeSpan queryTimeout,
        TimeSpan methodBudget,
        int maxPathConditions,
        int maxExpressionNodes,
        bool useSharedResultCache = false)
        : this(
            mode,
            queryTimeout,
            methodBudget,
            maxPathConditions,
            maxExpressionNodes,
            useSharedResultCache,
            SmtSolverLifecycleOptions.Default) {
    }
    public static SmtAnalysisOptions ForMode(SmtAnalysisMode mode) => mode switch {
        SmtAnalysisMode.Deep => new SmtAnalysisOptions(
                            SmtAnalysisMode.Deep,
                            TimeSpan.FromMilliseconds(2000),
                            TimeSpan.FromMilliseconds(15000),
                            512,
                            8192,
                            false),
        _ => CreateBoundedDefaults(),
    };

    private static SmtAnalysisOptions CreateBoundedDefaults() => new(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMilliseconds(5000),
            192,
            2048,
            false);

    public SmtAnalysisOptions WithOverrides(
        TimeSpan? queryTimeout = null,
        TimeSpan? methodBudget = null,
        int? maxPathConditions = null,
        int? maxExpressionNodes = null) => this with {
            QueryTimeout = queryTimeout ?? QueryTimeout,
            MethodBudget = methodBudget ?? MethodBudget,
            MaxPathConditions = maxPathConditions ?? MaxPathConditions,
            MaxExpressionNodes = maxExpressionNodes ?? MaxExpressionNodes
        };

    public SmtAnalysisOptions WithLifecycle(SmtSolverLifecycleOptions lifecycle) =>
        this with { Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle)) };
}
