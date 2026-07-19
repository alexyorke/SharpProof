namespace SharpProof.Analyzer.Engine.Rules;

internal class ConditionalAccessPurityRule : PurityRuleBase<IConditionalAccessOperation>
{
    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(
        IConditionalAccessOperation conditionalAccessOperation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        var operationResult =
            PurityAnalysisEngine.CheckSingleOperation(conditionalAccessOperation.Operation, context, currentState);
        if (!operationResult.IsPure) return operationResult;

        var receiver = PurityAnalysisEngine.SkipImplicitConversions(conditionalAccessOperation.Operation) ??
                       conditionalAccessOperation.Operation;
        if (receiver.ConstantValue.HasValue && receiver.ConstantValue.Value == null)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (!PurityAnalysisEngine.TryCreateReferenceNullAssumptionState(
                currentState,
                receiver,
                false,
                context.SmtAnalysis,
                out var whenNotNullState))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var whenNotNullResult =
            PurityAnalysisEngine.CheckSingleOperation(conditionalAccessOperation.WhenNotNull, context,
                whenNotNullState);
        if (!whenNotNullResult.IsPure) return whenNotNullResult;


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
