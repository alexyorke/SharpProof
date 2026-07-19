namespace SharpProof.Analyzer.Engine.Rules;

internal delegate PurityAnalysisEngine.PurityAnalysisResult PurityRuleHandler(
    IOperation operation,
    PurityAnalysisContext context,
    PurityAnalysisEngine.PurityAnalysisState currentState);
