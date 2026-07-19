namespace SharpProof.Analyzer.Engine.Rules;

internal class ThrowOperationPurityRule
{
    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (!(operation is IThrowOperation throwOperation))
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);


        if (throwOperation.Exception != null)
        {
            var exceptionResult =
                PurityAnalysisEngine.CheckSingleOperation(throwOperation.Exception, context, currentState);
            if (!exceptionResult.IsPure) return exceptionResult;
        }

        return PurityAnalysisEngine.ImpureResult(
            operation,
            "throw",
            nameof(ThrowOperationPurityRule));
    }
}
