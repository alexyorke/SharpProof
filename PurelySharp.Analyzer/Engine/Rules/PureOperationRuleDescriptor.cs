using Microsoft.CodeAnalysis;

namespace PurelySharp.Analyzer.Engine.Rules
{
    internal readonly struct PureOperationRuleDescriptor
    {
        public PureOperationRuleDescriptor(
            OperationKind operationKind,
            string ruleName,
            string operationDescription,
            bool includeSyntaxInLog = true)
        {
            OperationKind = operationKind;
            RuleName = ruleName;
            OperationDescription = operationDescription;
            IncludeSyntaxInLog = includeSyntaxInLog;
        }

        public OperationKind OperationKind { get; }

        public string RuleName { get; }

        public string OperationDescription { get; }

        public bool IncludeSyntaxInLog { get; }
    }
}
