using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine.Rules;

internal class IsNullPurityRule : PurityRuleBase<IIsNullOperation>
{
    protected override OperationKind Kind => OperationKind.IsNull;

    protected override PurityAnalysisResult CheckTyped(IIsNullOperation isNullOperation,
        PurityAnalysisContext context, PurityAnalysisState currentState)
    {
        var operandResult = CheckSingleOperation(isNullOperation.Operand, context, currentState);
        if (!operandResult.IsPure) return operandResult;

        return PurityAnalysisResult.Pure;
    }
}