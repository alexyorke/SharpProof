using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class PropertyReferencePurityRule
{
    private static PurityAnalysisEngine.PurityAnalysisResult CheckArguments(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        return RuleAnalysisHelper.CheckInstanceAndArguments(
            propertyReferenceOperation.Instance,
            propertyReferenceOperation.Arguments,
            context,
            currentState);
    }
}
