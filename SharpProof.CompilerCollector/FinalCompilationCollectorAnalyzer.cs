namespace SharpProof.CompilerCollector;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FinalCompilationCollectorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [GeneratedDiagnosticDescriptors.CompilerManifestFailureRule];

    public override void Initialize(AnalysisContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze |
            GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationAction(static compilationContext =>
        {
            var configuration =
                AnalyzerConfiguration.FromOptions(compilationContext.Options);
            if (configuration.Profile == SharpProofProfile.Off ||
                ContractRuntimePolicy.IsRuntimeEvaluationEnabled(
                    compilationContext.Compilation,
                    compilationContext.CancellationToken))
            {
                return;
            }

            FinalCompilationCollector.Collect(
                compilationContext,
                configuration);
        });
    }
}
