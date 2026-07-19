namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class CoalesceOperationPurityRule : PurityRuleBase<ICoalesceOperation>
{
    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(ICoalesceOperation coalesceOperation,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
    {


        var leftResult = PurityAnalysisEngine.CheckSingleOperation(coalesceOperation.Value, context, currentState);
        if (!leftResult.IsPure) return leftResult;

        if (coalesceOperation.Value.ConstantValue.HasValue &&
            coalesceOperation.Value.ConstantValue.Value != null)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (!PurityAnalysisEngine.TryCreateReferenceNullAssumptionState(
                currentState,
                coalesceOperation.Value,
                true,
                context.SmtAnalysis,
                out var whenNullState))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var rightResult = PurityAnalysisEngine.CheckSingleOperation(coalesceOperation.WhenNull, context, whenNullState);
        if (!rightResult.IsPure) return rightResult;


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
