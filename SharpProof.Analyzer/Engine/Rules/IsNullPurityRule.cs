using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class IsNullPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(
            OperationKind.IsNull
            );

        public PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisState currentState)
        {
            if (operation is IIsNullOperation isNullOperation)
            {
                LogDebug($"  [IsNullRule] Checking null-test operation: {operation.Syntax}");
                var operandResult = CheckSingleOperation(isNullOperation.Operand, context, currentState);
                if (!operandResult.IsPure)
                {
                    LogDebug($"    [IsNullRule] Operand is Impure: {isNullOperation.Operand.Syntax}");
                    return operandResult;
                }

                LogDebug($"    [IsNullRule] Operand was pure. Operation is pure. Syntax: '{operation.Syntax?.ToString() ?? "N/A"}'");
                return PurityAnalysisResult.Pure;
            }

            return PurityAnalysisResult.Pure;
        }
    }
}
