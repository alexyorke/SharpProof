namespace SharpProof.Symbolic.Smt;

public enum SmtAnalysisMode
{
    Off,
    Bounded,
    Deep
}

public sealed class SmtAnalysisOptions
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

    private SmtAnalysisOptions(
        SmtAnalysisMode mode,
        TimeSpan queryTimeout,
        TimeSpan methodBudget,
        int maxPathConditions,
        int maxExpressionNodes,
        bool useSharedResultCache,
        SmtSolverLifecycleOptions lifecycle)
    {
        Mode = mode;
        QueryTimeout = queryTimeout;
        MethodBudget = methodBudget;
        MaxPathConditions = maxPathConditions;
        MaxExpressionNodes = maxExpressionNodes;
        UseSharedResultCache = useSharedResultCache;
        Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    public SmtAnalysisMode Mode { get; }
    public TimeSpan QueryTimeout { get; }
    public TimeSpan MethodBudget { get; }
    public int MaxPathConditions { get; }
    public int MaxExpressionNodes { get; }
    public bool UseSharedResultCache { get; }
    public SmtSolverLifecycleOptions Lifecycle { get; }
    public bool IsEnabled => Mode != SmtAnalysisMode.Off;

    public static SmtAnalysisOptions ForMode(SmtAnalysisMode mode)
    {
        switch (mode)
        {
            case SmtAnalysisMode.Off:
                return new SmtAnalysisOptions(
                    SmtAnalysisMode.Off,
                    TimeSpan.FromMilliseconds(750),
                    TimeSpan.FromMilliseconds(5000),
                    192,
                    2048,
                    false);
            case SmtAnalysisMode.Deep:
                return new SmtAnalysisOptions(
                    SmtAnalysisMode.Deep,
                    TimeSpan.FromMilliseconds(2000),
                    TimeSpan.FromMilliseconds(15000),
                    512,
                    8192,
                    false);
            default:
                return new SmtAnalysisOptions(
                    SmtAnalysisMode.Bounded,
                    TimeSpan.FromMilliseconds(750),
                    TimeSpan.FromMilliseconds(5000),
                    192,
                    2048,
                    false);
        }
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
