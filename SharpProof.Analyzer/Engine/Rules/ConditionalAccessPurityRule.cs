using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class ConditionalAccessPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => new[] { OperationKind.ConditionalAccess };

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (!(operation is IConditionalAccessOperation conditionalAccessOperation))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        var operationResult =
            PurityAnalysisEngine.CheckSingleOperation(conditionalAccessOperation.Operation, context, currentState);
        if (!operationResult.IsPure) return operationResult;

        var receiver = PurityAnalysisEngine.SkipImplicitConversions(conditionalAccessOperation.Operation) ??
                       conditionalAccessOperation.Operation;
        if (receiver.ConstantValue.HasValue && receiver.ConstantValue.Value == null)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (!PurityAnalysisEngine.TryCreateReferenceNullAssumptionState(
                currentState,
                receiver,
                false,
                context.SmtAnalysis,
                out var whenNotNullState))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var whenNotNullResult =
            PurityAnalysisEngine.CheckSingleOperation(conditionalAccessOperation.WhenNotNull, context,
                whenNotNullState);
        if (!whenNotNullResult.IsPure) return whenNotNullResult;


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
