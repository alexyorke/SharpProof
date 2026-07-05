using System;

namespace SharpProof.Symbolic.Smt
{
    public enum SmtAnalysisMode
    {
        Off,
        Bounded,
        Deep,
    }

    public sealed class SmtAnalysisOptions
    {
        public static readonly SmtAnalysisOptions Default = ForMode(SmtAnalysisMode.Bounded);

        public static SmtAnalysisOptions ForMode(SmtAnalysisMode mode)
        {
            switch (mode)
            {
                case SmtAnalysisMode.Off:
                    return new SmtAnalysisOptions(
                        SmtAnalysisMode.Off,
                        TimeSpan.FromMilliseconds(750),
                        TimeSpan.FromMilliseconds(5000),
                        maxPathConditions: 192,
                        maxExpressionNodes: 2048,
                        useSharedResultCache: false);
                case SmtAnalysisMode.Deep:
                    return new SmtAnalysisOptions(
                        SmtAnalysisMode.Deep,
                        TimeSpan.FromMilliseconds(2000),
                        TimeSpan.FromMilliseconds(15000),
                        maxPathConditions: 512,
                        maxExpressionNodes: 8192,
                        useSharedResultCache: false);
                default:
                    return new SmtAnalysisOptions(
                        SmtAnalysisMode.Bounded,
                        TimeSpan.FromMilliseconds(750),
                        TimeSpan.FromMilliseconds(5000),
                        maxPathConditions: 192,
                        maxExpressionNodes: 2048,
                        useSharedResultCache: false);
            }
        }

        public SmtAnalysisOptions(
            SmtAnalysisMode mode,
            TimeSpan queryTimeout,
            TimeSpan methodBudget,
            int maxPathConditions,
            int maxExpressionNodes,
            bool useSharedResultCache = false)
        {
            Mode = mode;
            QueryTimeout = queryTimeout;
            MethodBudget = methodBudget;
            MaxPathConditions = maxPathConditions;
            MaxExpressionNodes = maxExpressionNodes;
            UseSharedResultCache = useSharedResultCache;
        }

        public SmtAnalysisMode Mode { get; }
        public TimeSpan QueryTimeout { get; }
        public TimeSpan MethodBudget { get; }
        public int MaxPathConditions { get; }
        public int MaxExpressionNodes { get; }
        public bool UseSharedResultCache { get; }
        public bool IsEnabled => Mode != SmtAnalysisMode.Off;

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
                UseSharedResultCache);
        }

        public SmtAnalysisOptions WithSharedResultCache(bool enabled = true)
        {
            return new SmtAnalysisOptions(
                Mode,
                QueryTimeout,
                MethodBudget,
                MaxPathConditions,
                MaxExpressionNodes,
                enabled);
        }
    }
}
