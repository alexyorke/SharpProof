namespace SharpProof.Analyzer.Engine.Rules;

internal static class ChildOperationsPurityRule
{
    internal static PurityAnalysisEngine.PurityAnalysisResult CheckChildOperationsArePure(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        foreach (var child in operation.ChildOperations)
        {
            var childResult = PurityAnalysisEngine.CheckSingleOperation(child, context, currentState);
            if (!childResult.IsPure) return childResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
