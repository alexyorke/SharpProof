namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class DynamicOperationPurityRule {
    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext _,
        PurityAnalysisEngine.PurityAnalysisState __) => PurityAnalysisEngine.ImpureResult(
            operation,
            "dynamic_dispatch",
            nameof(DynamicOperationPurityRule));
}
