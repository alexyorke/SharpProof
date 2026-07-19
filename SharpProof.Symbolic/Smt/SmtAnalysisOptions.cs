namespace SharpProof.Symbolic.Smt;

internal enum SmtAnalysisMode
{
    Off,
    Bounded,
    Deep
}

internal sealed class SmtAnalysisOptions(
    SmtAnalysisMode mode,
    TimeSpan queryTimeout,
    TimeSpan methodBudget,
    int maxPathConditions,
    int maxExpressionNodes,
    bool useSharedResultCache,
    SmtSolverLifecycleOptions lifecycle)
{
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
            SmtSolverLifecycleOptions.Default)
    {
    }

    public SmtAnalysisMode Mode { get; } = mode;
    public TimeSpan QueryTimeout { get; } = queryTimeout;
    public TimeSpan MethodBudget { get; } = methodBudget;
    public int MaxPathConditions { get; } = maxPathConditions;
    public int MaxExpressionNodes { get; } = maxExpressionNodes;
    public bool UseSharedResultCache { get; } = useSharedResultCache;
    public SmtSolverLifecycleOptions Lifecycle { get; } = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    public bool IsEnabled => Mode != SmtAnalysisMode.Off;

    public static SmtAnalysisOptions ForMode(SmtAnalysisMode mode)
    {
        switch (mode)
        {
            case SmtAnalysisMode.Off:
                return CreateBoundedDefaults(SmtAnalysisMode.Off);
            case SmtAnalysisMode.Deep:
                return new SmtAnalysisOptions(
                    SmtAnalysisMode.Deep,
                    TimeSpan.FromMilliseconds(2000),
                    TimeSpan.FromMilliseconds(15000),
                    512,
                    8192,
                    false);
            default:
                return CreateBoundedDefaults(SmtAnalysisMode.Bounded);
        }
    }

    private static SmtAnalysisOptions CreateBoundedDefaults(SmtAnalysisMode mode)
    {
        return new SmtAnalysisOptions(
            mode,
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMilliseconds(5000),
            192,
            2048,
            false);
    }

    public SmtAnalysisOptions WithOverrides(
        TimeSpan? queryTimeout = null,
        TimeSpan? methodBudget = null,
        int? maxPathConditions = null,
        int? maxExpressionNodes = null)
    {
        return new SmtAnalysisOptions(
            Mode,
            queryTimeout ?? QueryTimeout,
            methodBudget ?? MethodBudget,
            maxPathConditions ?? MaxPathConditions,
            maxExpressionNodes ?? MaxExpressionNodes,
            UseSharedResultCache,
            Lifecycle);
    }

    public SmtAnalysisOptions WithLifecycle(SmtSolverLifecycleOptions lifecycle)
    {
        return new SmtAnalysisOptions(
            Mode,
            QueryTimeout,
            MethodBudget,
            MaxPathConditions,
            MaxExpressionNodes,
            UseSharedResultCache,
            lifecycle);
    }
}
