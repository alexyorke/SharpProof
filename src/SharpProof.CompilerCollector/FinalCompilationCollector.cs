#pragma warning disable RS1035 // This build-only analyzer emits the selected seal.
namespace SharpProof.CompilerCollector;

// Compiler artifact emission is isolated from the live analyzer entry point.
internal static class FinalCompilationCollector
{
    private const string OutputOption = "build_property._SharpProofCompilerManifestPath",
        TargetFrameworkOption = "build_property._SharpProofCompilationTargetFramework",
        CaptureRequestOption = "build_property._SharpProofCompilerCaptureRequestPath",
        ProjectDirectoryOption = "build_property._SharpProofProjectDirectory",
        ContractsExportOption = "build_property._SharpProofContractsExportPath",
        ContractsExportMetadata = "build_metadata.AdditionalFiles.SharpProofContractsExport",
        SpecificationPacksOption =
            "build_property.SharpProofSpecificationPacks",
        MaximumExpressionDepthOption =
            "build_property.SharpProofVerifyMaximumExpressionDepth",
        TrustExceptionMessagesOption =
            "build_property.SharpProofTrustExceptionMessages";
    internal static void Collect(CompilationAnalysisContext context, CompilerConfiguration configuration)
    {
        try
        {
            var options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (!options.TryGetValue(OutputOption, out var path) ||
                string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (ContractRuntimePolicy.IsReservedSymbolDefined(
                    context.Compilation,
                    context.CancellationToken))
            {
                return;
            }

            if (!CompilerConfigurationValidation.GetIssues(
                    context.Compilation,
                    context.Options,
                    configuration,
                    context.CancellationToken)
                .IsEmpty)
            {
                throw new InvalidOperationException(
                    "analyzer configuration is invalid");
            }
            // The stable request path is a compiler option; its per-build token
            // is not an incremental compiler input or part of the artifact.
            var captureRequest = Get(options, CaptureRequestOption);
            var invocation = string.IsNullOrWhiteSpace(captureRequest)
                ? null : System.IO.File.ReadAllText(captureRequest).Trim();
            if (invocation != null &&
                (!Guid.TryParseExact(invocation, "N", out var parsed) || parsed.ToString("N") != invocation))
            {
                throw new InvalidOperationException("compiler capture invocation is invalid");
            }
            var serialized = Create(context, options, configuration);
            context.CancellationToken.ThrowIfCancellationRequested();
            AtomicFile.WriteUtf8(path, serialized);
            // This project's own export is written before the capture is
            // marked complete: the stamp step trusts only a completed capture.
            var exportPath = Get(options, ContractsExportOption);
            if (!string.IsNullOrWhiteSpace(exportPath) && context.Compilation is CSharpCompilation exporting)
            {
                var export = CompilerContractsExportProducer.Create(exporting, Get(options, TargetFrameworkOption),
                    context.CancellationToken);
                context.CancellationToken.ThrowIfCancellationRequested();
                AtomicFile.WriteUtf8(exportPath, CompilerContractsExportJson.Serialize(export));
            }
            if (invocation != null)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                AtomicFile.WriteUtf8(captureRequest + ".completed", invocation);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AggregateException)
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
                CompilerCollectorDiagnosticDescriptors.CompilerManifestFailureRule,
                Location.None,
                exception.GetType().Name + ": " + exception.Message));
        }
    }

    private static string Create(
        CompilationAnalysisContext context,
        AnalyzerConfigOptions options, CompilerConfiguration configuration)
    {
        var compilation = context.Compilation as CSharpCompilation ??
            throw new InvalidOperationException("compilation must be a C# compilation");
        var targetFramework = Get(options, TargetFrameworkOption);
        var features = configuration.Features == SharpProofFeatures.Effects ? WorkerFeatureSet.Effects :
            configuration.Features == SharpProofFeatures.Contracts ? WorkerFeatureSet.Contracts : WorkerFeatureSet.All;
        // Exports of referenced projects arrive as additional files that the
        // MSBuild targets mark with dedicated item metadata.
        ImmutableArray<AdditionalText> exportFiles = [.. context.Options.AdditionalFiles.Where(file =>
            context.Options.AnalyzerConfigOptionsProvider.GetOptions(file).TryGetValue(ContractsExportMetadata, out var marked) &&
            string.Equals(marked, "true", StringComparison.OrdinalIgnoreCase))];
        var exports = CompilerContractExports.Load(compilation, exportFiles, Get(options, ProjectDirectoryOption),
            context.CancellationToken);
        var trust = Get(options, TrustExceptionMessagesOption);
        if (!string.IsNullOrEmpty(trust) && !bool.TryParse(trust, out _))
        { throw new InvalidOperationException("SharpProofTrustExceptionMessages must be true or false."); }
        var builder = new ClaimManifestBuilder(compilation, features, context.CancellationToken)
        {
            ContractExports = exports,
            TrustExceptionMessages = string.Equals(trust, "true", StringComparison.OrdinalIgnoreCase)
        };
        var discovery = builder.Build();
        if (!int.TryParse(
                Get(options, MaximumExpressionDepthOption),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var maximumExpressionDepth) ||
            maximumExpressionDepth is < 1 or > WorkerBudgets.MaximumAllowedExpressionDepth)
        {
            throw new InvalidOperationException(
                "SharpProofVerifyMaximumExpressionDepth must be between 1 and " + WorkerBudgets.MaximumAllowedExpressionDepth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        var artifact = CompilerManifestArtifactProducer.Create(
            compilation, Get(options, ProjectDirectoryOption),
            targetFramework, features, discovery, maximumExpressionDepth,
            context.CancellationToken,
            context.Options.AdditionalFiles,
            ParseSpecificationPacks(Get(options, SpecificationPacksOption)),
            contractExports: exports);
        return CompilerManifestArtifactJson.SerializeProducerValidated(artifact, context.CancellationToken);
    }

    private static ImmutableArray<string> ParseSpecificationPacks(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var packs = ImmutableArray.CreateBuilder<string>();
        var seen = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var valuePart in value.Split([';'], StringSplitOptions.None))
        {
            var pack = valuePart.Trim();
            if (pack.Length == 0)
            {
                throw new InvalidOperationException(
                    "SharpProofSpecificationPacks must contain a pack identifier.");
            }

            if (seen.Contains(pack))
            {
                throw new InvalidOperationException(
                    "SharpProofSpecificationPacks must not contain duplicate identifiers.");
            }

            seen.Add(pack);
            packs.Add(pack);
        }

        return [.. packs.ToImmutable().OrderBy(static pack => pack, StringComparer.Ordinal)];
    }

    private static string Get(AnalyzerConfigOptions options, string key)
    {
        return options.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
#pragma warning restore RS1035
