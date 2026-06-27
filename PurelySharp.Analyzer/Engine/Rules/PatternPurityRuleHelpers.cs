using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace PurelySharp.Analyzer.Engine.Rules
{
    internal static class PatternPurityRuleHelpers
    {
        internal static PurityAnalysisEngine.PurityAnalysisResult CheckChildOperationsArePure(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            foreach (var child in operation.ChildOperations)
            {
                var childResult = PurityAnalysisEngine.CheckSingleOperation(child, context, currentState);
                if (!childResult.IsPure)
                {
                    return childResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
