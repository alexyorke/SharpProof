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
                var operandResult = CheckSingleOperation(isNullOperation.Operand, context, currentState);
                if (!operandResult.IsPure)
                {
                    return operandResult;
                }

                return PurityAnalysisResult.Pure;
            }

            return PurityAnalysisResult.Pure;
        }
    }
}
