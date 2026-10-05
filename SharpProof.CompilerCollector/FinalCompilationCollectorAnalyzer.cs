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
