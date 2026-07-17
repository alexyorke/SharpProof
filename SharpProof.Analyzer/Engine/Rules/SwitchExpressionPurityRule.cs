using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class SwitchExpressionPurityRule : PurityRuleBase<ISwitchExpressionOperation>
{
    protected override OperationKind Kind => OperationKind.SwitchExpression;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(ISwitchExpressionOperation switchExpression,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
    {


        var valueResult = PurityAnalysisEngine.CheckSingleOperation(switchExpression.Value, context, currentState);
        if (!valueResult.IsPure) return valueResult;


        foreach (var arm in switchExpression.Arms)
        {
            if (arm.Pattern != null)
            {
                var patternResult = PurityAnalysisEngine.CheckSingleOperation(arm.Pattern, context, currentState);
                if (!patternResult.IsPure) return patternResult;
            }


            if (arm.Guard != null)
            {
                var guardResult = PurityAnalysisEngine.CheckSingleOperation(arm.Guard, context, currentState);
                if (!guardResult.IsPure) return guardResult;
            }


            var armValueResult = PurityAnalysisEngine.CheckSingleOperation(arm.Value, context, currentState);
            if (!armValueResult.IsPure) return armValueResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}