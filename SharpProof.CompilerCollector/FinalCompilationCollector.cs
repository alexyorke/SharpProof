#pragma warning disable RS1035 // This build-only analyzer emits the selected seal.
namespace SharpProof.Analyzer;

// Compiler artifact emission is isolated from the live analyzer entry point.
internal static class FinalCompilationCollector
{
    private const string OutputOption = "build_property._SharpProofCompilerManifestPath",
        TargetFrameworkOption = "build_property._SharpProofCompilationTargetFramework",
        ProjectDirectoryOption = "build_property._SharpProofProjectDirectory",
        SpecificationPacksOption =
            "build_property.SharpProofSpecificationPacks",
        MaximumExpressionDepthOption =
            "build_property.SharpProofVerifyMaximumExpressionDepth";
    internal static void Collect(CompilationAnalysisContext context, AnalyzerConfiguration configuration)
    {
        try
        {
            var options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (!options.TryGetValue(OutputOption, out var path) ||
                string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (ContractRuntimePolicy.IsRuntimeEvaluationEnabled(
                    context.Compilation,
                    context.CancellationToken))
            {
                return;
            }

            if (!SharpProofAnalyzerEngine.GetConfigurationDiagnostics(
                    context.Compilation,
                    context.Options,
                    configuration,
                    context.CancellationToken)
                .IsEmpty)
            {
                throw new InvalidOperationException(
                    "analyzer configuration is invalid");
            }
            AtomicFile.WriteUtf8(path, Create(context, options, configuration));
        }
        catch (OperationCanceledException)
        {
            throw;
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
            context.CancellationToken,
            context.Options.AdditionalFiles,
            ParseSpecificationPacks(Get(options, SpecificationPacksOption)));
        return CompilerManifestArtifactJson.SerializeValidated(artifact);
    }

    private static ImmutableArray<string> ParseSpecificationPacks(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var packs = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var valuePart in value.Split([';'], StringSplitOptions.None))
        {
            var pack = valuePart.Trim();
            if (pack.Length == 0)
            {
                throw new InvalidOperationException(
                    "SharpProofSpecificationPacks must contain a pack identifier.");
            }

            if (!seen.Add(pack))
            {
                throw new InvalidOperationException(
                    "SharpProofSpecificationPacks must not contain duplicate identifiers.");
            }

            packs.Add(pack);
        }

        packs.Sort(StringComparer.Ordinal);
        return [.. packs];
    }

    private static string Get(AnalyzerConfigOptions options, string key)
    {
        return options.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
#pragma warning restore RS1035
