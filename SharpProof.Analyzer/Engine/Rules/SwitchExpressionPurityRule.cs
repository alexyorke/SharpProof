using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class SwitchExpressionPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.SwitchExpression);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is ISwitchExpressionOperation switchExpression))
            {
                PurityAnalysisEngine.LogDebug($"WARNING: SwitchExpressionPurityRule called with unexpected operation type: {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"  [SwitchExprRule] Checking Switch Expression: {switchExpression.Syntax}");


            var valueResult = PurityAnalysisEngine.CheckSingleOperation(switchExpression.Value, context, currentState);
            if (!valueResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [SwitchExprRule] Value expression is Impure: {switchExpression.Value.Syntax}");
                return valueResult;
            }


            foreach (var arm in switchExpression.Arms)
            {
                PurityAnalysisEngine.LogDebug($"    [SwitchExprRule] Checking Arm: {arm.Syntax}");


                if (arm.Pattern != null)
                {
                    PurityAnalysisEngine.LogDebug($"      [SwitchExprRule.Arm] Checking Pattern: {arm.Pattern.Syntax} ({arm.Pattern.Kind})");
                    var patternResult = PurityAnalysisEngine.CheckSingleOperation(arm.Pattern, context, currentState);
                    if (!patternResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"      [SwitchExprRule.Arm] Pattern is IMPURE. Switch expression is Impure.");
                        return patternResult;
                    }
                }


                if (arm.Guard != null)
                {
                    PurityAnalysisEngine.LogDebug($"      [SwitchExprRule.Arm] Checking Guard: {arm.Guard.Syntax} ({arm.Guard.Kind})");
                    var guardResult = PurityAnalysisEngine.CheckSingleOperation(arm.Guard, context, currentState);
                    if (!guardResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"      [SwitchExprRule.Arm] Guard is IMPURE. Switch expression is Impure.");
                        return guardResult;
                    }
                }


                PurityAnalysisEngine.LogDebug($"      [SwitchExprRule.Arm] Checking Value: {arm.Value.Syntax} ({arm.Value.Kind})");
                var armValueResult = PurityAnalysisEngine.CheckSingleOperation(arm.Value, context, currentState);
                if (!armValueResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"      [SwitchExprRule.Arm] Value is IMPURE. Switch expression is Impure.");
                    return armValueResult;
                }
            }

            PurityAnalysisEngine.LogDebug($"SwitchExpressionPurityRule: All parts checked and pure (or impurity overridden) for {switchExpression.Syntax}.");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}