using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using PurelySharp.Analyzer.Engine;

namespace PurelySharp.Analyzer.Engine.Rules
{
    internal class CoalesceOperationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Coalesce);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is ICoalesceOperation coalesceOperation))
            {

                PurityAnalysisEngine.LogDebug($"  [CoalesceRule] WARNING: Incorrect operation type {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"  [CoalesceRule] Checking Coalesce Operation: {coalesceOperation.Syntax}");








            var leftResult = PurityAnalysisEngine.CheckSingleOperation(coalesceOperation.Value, context, currentState);
            if (!leftResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [CoalesceRule] Left side is Impure: {coalesceOperation.Value.Syntax}");
                return leftResult;
            }
            PurityAnalysisEngine.LogDebug($"    [CoalesceRule] Left side is Pure.");

            if (coalesceOperation.Value.ConstantValue.HasValue &&
                coalesceOperation.Value.ConstantValue.Value != null)
            {
                PurityAnalysisEngine.LogDebug($"    [CoalesceRule] Constant non-null left side skips WhenNull. Coalesce Operation is Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.TryGetKnownReferenceNullValueFromPathFacts(
                    currentState,
                    coalesceOperation.Value,
                    context.SmtAnalysis,
                    out var leftIsNull) &&
                !leftIsNull)
            {
                PurityAnalysisEngine.LogDebug($"    [CoalesceRule] Path facts prove non-null left side skips WhenNull. Coalesce Operation is Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!PurityAnalysisEngine.TryCreateReferenceNullAssumptionState(
                    currentState,
                    coalesceOperation.Value,
                    isNull: true,
                    context.SmtAnalysis,
                    out var whenNullState))
            {
                PurityAnalysisEngine.LogDebug($"    [CoalesceRule] SMT proves WhenNull branch is unreachable. Coalesce Operation is Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var rightResult = PurityAnalysisEngine.CheckSingleOperation(coalesceOperation.WhenNull, context, whenNullState);
            if (!rightResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [CoalesceRule] Right side (WhenNull) is Impure: {coalesceOperation.WhenNull.Syntax}");
                return rightResult;
            }
            PurityAnalysisEngine.LogDebug($"    [CoalesceRule] Right side (WhenNull) is Pure.");


            PurityAnalysisEngine.LogDebug($"  [CoalesceRule] Coalesce Operation is Pure: {coalesceOperation.Syntax}");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
