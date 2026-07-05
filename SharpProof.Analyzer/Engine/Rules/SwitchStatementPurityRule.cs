using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class SwitchStatementPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Switch);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is ISwitchOperation switchOperation))
            {
                PurityAnalysisEngine.LogDebug($"WARNING: SwitchStatementPurityRule called with unexpected operation type: {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"SwitchStatementPurityRule: Analyzing {switchOperation.Syntax}");


            var valueResult = PurityAnalysisEngine.CheckSingleOperation(switchOperation.Value, context, currentState);
            if (!valueResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"SwitchStatementPurityRule: Switch value is impure: {switchOperation.Value.Syntax}");
                return valueResult;
            }



            PurityAnalysisEngine.LogDebug($"SwitchStatementPurityRule: Assuming switch statement structure itself is pure for {switchOperation.Syntax}. Case/Value purity handled elsewhere.");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
