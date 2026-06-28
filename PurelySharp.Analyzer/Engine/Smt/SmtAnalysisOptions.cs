using System;

namespace PurelySharp.Analyzer.Engine.Smt
{
    internal enum SmtAnalysisMode
    {
        Off,
        Bounded,
        Deep,
    }

    internal sealed class SmtAnalysisOptions
    {
        public static readonly SmtAnalysisOptions Default = new(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(2000),
            maxPathConditions: 96,
            maxExpressionNodes: 512);

        public SmtAnalysisOptions(
            SmtAnalysisMode mode,
            TimeSpan queryTimeout,
            TimeSpan methodBudget,
            int maxPathConditions,
            int maxExpressionNodes)
        {
            Mode = mode;
            QueryTimeout = queryTimeout;
            MethodBudget = methodBudget;
            MaxPathConditions = maxPathConditions;
            MaxExpressionNodes = maxExpressionNodes;
        }

        public SmtAnalysisMode Mode { get; }
        public TimeSpan QueryTimeout { get; }
        public TimeSpan MethodBudget { get; }
        public int MaxPathConditions { get; }
        public int MaxExpressionNodes { get; }
        public bool IsEnabled => Mode != SmtAnalysisMode.Off;
    }
}
