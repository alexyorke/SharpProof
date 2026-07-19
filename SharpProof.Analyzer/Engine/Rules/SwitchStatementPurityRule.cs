namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class SwitchStatementPurityRule : PurityRuleBase<ISwitchOperation>
{
    protected override OperationKind Kind => OperationKind.Switch;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(ISwitchOperation switchOperation,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
    {


        var valueResult = PurityAnalysisEngine.CheckSingleOperation(switchOperation.Value, context, currentState);
        if (!valueResult.IsPure) return valueResult;


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}