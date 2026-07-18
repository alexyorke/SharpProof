using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class ArrayElementReferencePurityRule : PurityRuleBase<IArrayElementReferenceOperation>
{
    protected override OperationKind Kind => OperationKind.ArrayElementReference;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(
        IArrayElementReferenceOperation arrayElementReference, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        var arrayRefResult =
            PurityAnalysisEngine.CheckSingleOperation(arrayElementReference.ArrayReference, context, currentState);
        if (!arrayRefResult.IsPure) return arrayRefResult;


        foreach (var indexOperation in arrayElementReference.Indices)
        {
            var indexResult = PurityAnalysisEngine.CheckSingleOperation(indexOperation, context, currentState);
            if (!indexResult.IsPure) return indexResult;
        }

        if (RuleAnalysisHelper.IsWriteOnlyAssignmentTarget(arrayElementReference))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
