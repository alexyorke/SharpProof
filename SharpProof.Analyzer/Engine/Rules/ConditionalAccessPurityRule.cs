using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal class ConditionalAccessPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => new[] { OperationKind.ConditionalAccess };

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IConditionalAccessOperation conditionalAccessOperation))
            {
                PurityAnalysisEngine.LogDebug($"  [ConditionalAccessRule] WARNING: Incorrect operation type {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"  [ConditionalAccessRule] Checking Conditional Access Operation: {conditionalAccessOperation.Syntax}");


            var operationResult = PurityAnalysisEngine.CheckSingleOperation(conditionalAccessOperation.Operation, context, currentState);
            if (!operationResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [ConditionalAccessRule] Operation before '?.' is Impure: {conditionalAccessOperation.Operation.Syntax}");
                return operationResult;
            }
            PurityAnalysisEngine.LogDebug($"    [ConditionalAccessRule] Operation before '?.' is Pure.");

            var receiver = PurityAnalysisEngine.SkipImplicitConversions(conditionalAccessOperation.Operation) ??
                conditionalAccessOperation.Operation;
            if (receiver.ConstantValue.HasValue && receiver.ConstantValue.Value == null)
            {
                PurityAnalysisEngine.LogDebug($"    [ConditionalAccessRule] Constant null receiver skips WhenNotNull. Conditional Access Operation is Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.TryGetKnownReferenceNullValueFromPathFacts(
                    currentState,
                    receiver,
                    context.SmtAnalysis,
                    out var receiverIsNull) &&
                receiverIsNull)
            {
                PurityAnalysisEngine.LogDebug($"    [ConditionalAccessRule] Path facts prove null receiver skips WhenNotNull. Conditional Access Operation is Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!PurityAnalysisEngine.TryCreateReferenceNullAssumptionState(
                    currentState,
                    receiver,
                    isNull: false,
                    context.SmtAnalysis,
                    out var whenNotNullState))
            {
                PurityAnalysisEngine.LogDebug($"    [ConditionalAccessRule] SMT proves WhenNotNull branch is unreachable. Conditional Access Operation is Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var whenNotNullResult = PurityAnalysisEngine.CheckSingleOperation(conditionalAccessOperation.WhenNotNull, context, whenNotNullState);
            if (!whenNotNullResult.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [ConditionalAccessRule] Operation after '?.' (WhenNotNull) is Impure: {conditionalAccessOperation.WhenNotNull.Syntax}");
                return whenNotNullResult;
            }
            PurityAnalysisEngine.LogDebug($"    [ConditionalAccessRule] Operation after '?.' (WhenNotNull) is Pure.");


            PurityAnalysisEngine.LogDebug($"  [ConditionalAccessRule] Conditional Access Operation is Pure: {conditionalAccessOperation.Syntax}");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
