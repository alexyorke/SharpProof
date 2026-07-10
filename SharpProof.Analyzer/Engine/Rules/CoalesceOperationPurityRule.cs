using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal class CoalesceOperationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Coalesce);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is ICoalesceOperation coalesceOperation))
            {

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }









            var leftResult = PurityAnalysisEngine.CheckSingleOperation(coalesceOperation.Value, context, currentState);
            if (!leftResult.IsPure)
            {
                return leftResult;
            }

            if (coalesceOperation.Value.ConstantValue.HasValue &&
                coalesceOperation.Value.ConstantValue.Value != null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.TryGetKnownReferenceNullValueFromPathFacts(
                    currentState,
                    coalesceOperation.Value,
                    context.SmtAnalysis,
                    out var leftIsNull) &&
                !leftIsNull)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!PurityAnalysisEngine.TryCreateReferenceNullAssumptionState(
                    currentState,
                    coalesceOperation.Value,
                    isNull: true,
                    context.SmtAnalysis,
                    out var whenNullState))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var rightResult = PurityAnalysisEngine.CheckSingleOperation(coalesceOperation.WhenNull, context, whenNullState);
            if (!rightResult.IsPure)
            {
                return rightResult;
            }


            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
