using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class SwitchExpressionPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.SwitchExpression);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (!(operation is ISwitchExpressionOperation switchExpression))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;


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