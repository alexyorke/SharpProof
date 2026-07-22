namespace SharpProof.Analyzer;

internal sealed class AnalyzerProofService(SmtAnalysisOptions smtOptions, SharpProofAnalysisBudget analysisLimits) : IDisposable {
    internal SmtAnalysisService SmtAnalysis { get; } = new(smtOptions);

    internal SharpProofAnalysisBudget AnalysisLimits { get; } =
        analysisLimits ?? throw new ArgumentNullException(nameof(analysisLimits));

    public void Dispose() => SmtAnalysis.Dispose();
}
