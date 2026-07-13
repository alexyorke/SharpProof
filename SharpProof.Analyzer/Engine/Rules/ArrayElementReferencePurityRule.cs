using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class ArrayElementReferencePurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds =>
        ImmutableArray.Create(OperationKind.ArrayElementReference);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (!(operation is IArrayElementReferenceOperation arrayElementReference))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

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
