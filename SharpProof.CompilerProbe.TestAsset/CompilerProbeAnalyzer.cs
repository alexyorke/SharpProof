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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [s_failureRule];

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
            WriteAtomically(
                outputPath,
                CompilerProbeSnapshot.Create(context));
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

    private static void WriteAtomically(string path, string content)
    {
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException(
                "The probe output path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            "." + Path.GetFileName(destination) + "." +
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) +
            ".tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destination))
            {
                File.Replace(temporaryPath, destination, null);
            }
            else
            {
                File.Move(temporaryPath, destination);
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
