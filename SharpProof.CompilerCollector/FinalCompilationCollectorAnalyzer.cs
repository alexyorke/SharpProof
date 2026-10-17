namespace SharpProof.CompilerCollector;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FinalCompilationCollectorAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
        [GeneratedDiagnosticDescriptors.CompilerManifestFailureRule];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        s_supportedDiagnostics;

    public override void Initialize(AnalysisContext context)
    {
        context = ArgumentNullGuard.NotNull(context, nameof(context));

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze |
            GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationAction(static compilationContext =>
        {
            if (compilationContext.Compilation is null)
            { throw new System.ArgumentException("Value must be initialized.", nameof(compilationContext)); }

            var configuration =
                AnalyzerConfiguration.FromOptions(compilationContext.Options);
            if (configuration.Profile == SharpProofProfile.Off)
            {
                return;
            }

            FinalCompilationCollector.Collect(
                compilationContext,
                configuration);
        });
    }
}
