namespace SharpProof.Analyzer.Engine.Rules;

/// <summary>
/// Adapts an untyped registry handler to one strongly typed operation.
/// </summary>
internal abstract class PurityRuleBase<TOperation>
    where TOperation : class, IOperation
{
    internal PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is not TOperation typed)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return CheckTyped(typed, context, currentState);
    }

    protected abstract PurityAnalysisEngine.PurityAnalysisResult CheckTyped(
        TOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState);
}
