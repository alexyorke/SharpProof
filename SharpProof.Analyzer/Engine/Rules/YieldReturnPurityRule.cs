using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class YieldReturnPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.YieldReturn);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IReturnOperation yieldReturnOperation))
            {

                PurityAnalysisEngine.LogDebug($"    [YieldReturnRule] Unexpected operation type. Assuming impure.");
                return PurityAnalysisEngine.ImpureResult(operation.Syntax);
            }


            if (yieldReturnOperation.ReturnedValue != null)
            {
                PurityAnalysisEngine.LogDebug($"    [YieldReturnRule] Checking yielded value: {yieldReturnOperation.ReturnedValue.Syntax} ({yieldReturnOperation.ReturnedValue.Kind})");
                var valueResult = PurityAnalysisEngine.CheckSingleOperation(yieldReturnOperation.ReturnedValue, context, currentState);
                if (!valueResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [YieldReturnRule] Yielded value is IMPURE. Yield return operation is Impure.");
                    return valueResult;
                }
                PurityAnalysisEngine.LogDebug($"    [YieldReturnRule] Yielded value is PURE.");
            }
            else
            {

                PurityAnalysisEngine.LogDebug($"    [YieldReturnRule] Yield return has no value. Operation is Pure.");
            }



            PurityAnalysisEngine.LogDebug($"    [YieldReturnRule] Yield return operation itself (excluding other method code) is Pure.");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}