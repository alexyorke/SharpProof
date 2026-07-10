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

        if (IsPartOfAssignmentTarget(arrayElementReference)) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static bool IsPartOfAssignmentTarget(IOperation operation)
    {
        var current = operation;
        while (current != null)
        {
            if (current.Parent is IAssignmentOperation assignment && assignment.Target == current) return true;


            if (!(current.Parent is IMemberReferenceOperation || current.Parent is IPropertyReferenceOperation ||
                  current.Parent is IArrayElementReferenceOperation)) break;
            current = current.Parent;
        }

        return false;
    }
}