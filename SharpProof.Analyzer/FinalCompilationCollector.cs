#pragma warning disable RS1035 // This build-only analyzer emits the selected seal.
namespace SharpProof.Analyzer;
internal static class FinalCompilationCollector
{
    private const string OutputOption = "build_property._SharpProofCompilerManifestPath",
        TargetFrameworkOption = "build_property._SharpProofCompilationTargetFramework",
        ProjectDirectoryOption = "build_property._SharpProofProjectDirectory",
        MaximumExpressionDepthOption =
            "build_property.SharpProofVerifyMaximumExpressionDepth";
    internal static void Collect(CompilationAnalysisContext context, AnalyzerConfiguration configuration)
    {
        var options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (!options.TryGetValue(OutputOption, out var path) ||
            string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            AtomicFile.WriteUtf8(path, Create(context, options, configuration));
        }
#pragma warning disable CA1031
        catch (Exception exception)
            when (!context.CancellationToken.IsCancellationRequested)
        {
#pragma warning restore CA1031
            exception = exception.GetBaseException();
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratedDiagnosticDescriptors.CompilerManifestFailureRule,
                Location.None,
                exception.GetType().Name + ": " + exception.Message));
        }
    }
    private static string Create(
        CompilationAnalysisContext context,
        AnalyzerConfigOptions options, AnalyzerConfiguration configuration)
    {
        var compilation = (CSharpCompilation)context.Compilation;
        var targetFramework = Get(options, TargetFrameworkOption);
        var features = configuration.Features == SharpProofFeatures.Effects ? WorkerFeatureSet.Effects :
            configuration.Features == SharpProofFeatures.Contracts ? WorkerFeatureSet.Contracts : WorkerFeatureSet.All;
        var discovery = new ClaimManifestBuilder(
            compilation, features, context.CancellationToken).Build();
        if (!int.TryParse(
                Get(options, MaximumExpressionDepthOption),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var maximumExpressionDepth) ||
            maximumExpressionDepth is < 1 or > 256)
        {
            throw new InvalidOperationException(
                "SharpProofVerifyMaximumExpressionDepth must be between 1 and 256.");
        }

        var artifact = CompilerManifestArtifactProducer.Create(
            compilation, Get(options, ProjectDirectoryOption),
            targetFramework, features, discovery, maximumExpressionDepth,
            context.CancellationToken, context.Options.AdditionalFiles);
        return CompilerManifestArtifactJson.Serialize(artifact);
    }
    private static string Get(AnalyzerConfigOptions options, string key)
    {
        return options.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
#pragma warning restore RS1035
