using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class PropertyReferencePurityRule
{
    private static PurityAnalysisEngine.PurityAnalysisResult CheckArguments(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (propertyReferenceOperation.Instance != null)
        {
            var instanceResult = PurityAnalysisEngine.CheckSingleOperation(
                propertyReferenceOperation.Instance,
                context,
                currentState);
            if (!instanceResult.IsPure) return instanceResult;
        }

        foreach (var argument in propertyReferenceOperation.Arguments)
        {
            if (argument.Value == null) return PurityAnalysisEngine.PurityAnalysisResult.Impure(argument.Syntax);

            var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
            if (!argumentResult.IsPure) return argumentResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}