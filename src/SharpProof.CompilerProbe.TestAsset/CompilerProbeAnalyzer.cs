namespace SharpProof.CompilerProbe.TestAsset;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompilerProbeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor s_failureRule = new(
        CompilerProbeContract.FailureDiagnosticId,
        "Final compilation probe failed",
        "Final compilation probe failed: {0}",
        "SharpProof.Testing",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);
    private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
        [s_failureRule];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        s_supportedDiagnostics;

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
        context.RegisterCompilationAction(WriteSnapshot);
    }

    private static void WriteSnapshot(CompilationAnalysisContext context)
    {
        var globalOptions =
            context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (!globalOptions.TryGetValue(
                CompilerProbeContract.OutputPathOptionKey,
                out var outputPath) ||
            string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(outputPath);
            var temporary = SharpProof.Ir.AtomicFile.PrepareStaged(destination);
            try
            {
                SharpProof.Ir.AtomicFile.WriteStagedBytes(
                    temporary,
                    new UTF8Encoding(false).GetBytes(
                        CompilerProbeSnapshot.Create(context)));
                SharpProof.Ir.AtomicFile.PublishStaged(temporary, destination);
            }
            finally
            {
                SharpProof.Ir.AtomicFile.TryDeleteStaged(temporary);
            }
        }
        catch (OperationCanceledException)
            when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
#pragma warning disable CA1031
        }
        catch (Exception exception)
        {
#pragma warning restore CA1031
            context.ReportDiagnostic(
                Diagnostic.Create(
                    s_failureRule,
                    Location.None,
                    exception.GetType().Name + ": " + exception.Message));
        }
    }

}
