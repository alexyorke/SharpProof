namespace SharpProof.Symbolic;

internal sealed class SymbolicQueryContext(
    SymbolicSourceInput source,
    SharpProofTarget target,
    SymbolicQueryOptions? options = null) {
    public SymbolicSourceInput Source { get; } = source ?? throw new ArgumentNullException(nameof(source));
    public SharpProofTarget Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public SymbolicQueryOptions Options { get; } = options ?? SymbolicQueryOptions.Default;
}

internal sealed class SymbolicQueryOptions(
    SmtAnalysisService? smtAnalysis = null,
    SharpProofAnalysisBudget? analysisLimits = null) {
    public static readonly SymbolicQueryOptions Default = new();

    public SharpProofAnalysisBudget AnalysisLimits { get; } =
        (analysisLimits ?? SharpProofAnalysisBudget.Default).Validate();

    public SmtAnalysisService? SmtAnalysis { get; } = smtAnalysis;
}
