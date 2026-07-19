namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class WithOperationPurityRule : PurityRuleBase<IWithOperation>
{
    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(IWithOperation withOperation,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
    {


        var targetType = withOperation.Type;

        if (targetType == null) return PurityAnalysisEngine.PurityAnalysisResult.Impure(withOperation.Syntax);


        var operandResult = PurityAnalysisEngine.CheckSingleOperation(withOperation.Operand, context, currentState);
        if (!operandResult.IsPure) return operandResult;


        if (withOperation.Initializer != null)
        {
            var initializerResult =
                PurityAnalysisEngine.CheckSingleOperation(withOperation.Initializer, context, currentState);
            if (!initializerResult.IsPure) return initializerResult;
        }


        if (targetType.IsValueType) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return PurityAnalysisEngine.PurityAnalysisResult.Impure(withOperation.Syntax);
    }
}
