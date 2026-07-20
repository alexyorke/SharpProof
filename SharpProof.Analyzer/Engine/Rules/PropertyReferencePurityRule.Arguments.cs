namespace SharpProof.Analyzer.Engine.Rules;

internal partial class PropertyReferencePurityRule {
    private static PurityAnalysisEngine.PurityAnalysisResult CheckArguments(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState) => RuleAnalysisHelper.CheckInstanceAndArguments(
            propertyReferenceOperation.Instance,
            propertyReferenceOperation.Arguments,
            context,
            currentState);
}
