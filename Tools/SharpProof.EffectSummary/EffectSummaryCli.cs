internal static class EffectSummaryCli
{
    public static int Run(string[] args)
    {
        var options = CliOptions.Parse(args);
        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(options.ArtifactSpecPath) && !string.IsNullOrWhiteSpace(options.ShardOutputPath))
            throw new ArgumentException("--artifact-spec and --shard-output cannot be combined.");

        if (!string.IsNullOrWhiteSpace(options.ShardOutputPath) && !string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("--shard-output cannot be combined with --output.");

        if (!string.IsNullOrWhiteSpace(options.ProgressPath) &&
            string.IsNullOrWhiteSpace(options.ArtifactSpecPath) &&
            string.IsNullOrWhiteSpace(options.ShardOutputPath))
            throw new ArgumentException("--progress requires --artifact-spec or --shard-output.");

        if (options.Resume && string.IsNullOrWhiteSpace(options.ProgressPath))
            throw new ArgumentException("--resume requires --progress.");

        if (options.WriteArtifactSpecDependencyManifests)
            return WriteArtifactSpecDependencyManifests(options);

        if (!string.IsNullOrWhiteSpace(options.ArtifactSpecPath))
            return RunArtifactSpec(options.ArtifactSpecPath!, options.ProgressPath, options.Resume);

        if (!string.IsNullOrWhiteSpace(options.ShardOutputPath)) return RunSharded(options);

        EffectSummaryOutputWriter.WriteDocument(EffectSummaryAnalysisPipeline.Analyze(options), options.OutputPath);
        return 0;
    }

    private static int WriteArtifactSpecDependencyManifests(CliOptions options)
    {
        var artifactSpecPath = Path.GetFullPath(options.ArtifactSpecPath!);
        var artifactSpecDirectory = Path.GetDirectoryName(artifactSpecPath)
                                    ?? throw new InvalidOperationException(
                                        $"Unable to resolve artifact spec directory for '{artifactSpecPath}'.");
        var document = ArtifactSpecDocument.Load(artifactSpecPath);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var inputs = new HashSet<string>(pathComparer)
        {
            artifactSpecPath,
            Path.GetFullPath(typeof(EffectSummaryCli).Assembly.Location)
        };
        var outputs = new HashSet<string>(pathComparer);

        foreach (var artifact in document.Artifacts)
        {
            var artifactOptions = CliOptions.FromArtifactSpec(document.Defaults, artifact, artifactSpecDirectory);
            foreach (var assemblyPath in EffectSummaryInputResolver.ResolveAssemblies(artifactOptions))
                inputs.Add(Path.GetFullPath(assemblyPath));

            if (!string.IsNullOrWhiteSpace(artifactOptions.SourceSummaryPath))
                inputs.Add(Path.GetFullPath(artifactOptions.SourceSummaryPath!));

            var outputPath = EffectSummaryInputResolver.ResolveDependencyOutputPath(
                artifact.OutputPath,
                options.DependencyOutputRoot,
                artifactSpecDirectory);
            if (!outputs.Add(outputPath))
                throw new InvalidOperationException(
                    $"Artifact spec '{artifactSpecPath}' maps more than one artifact to output '{outputPath}'.");
        }

        EffectSummaryOutputWriter.WriteManifestIfChanged(options.InputManifestPath!, inputs);
        EffectSummaryOutputWriter.WriteManifestIfChanged(options.OutputManifestPath!, outputs);
        return 0;
    }

    private static int RunArtifactSpec(string artifactSpecPath, string? progressPath, bool resume)
    {
        var artifactSpecDirectory = Path.GetDirectoryName(Path.GetFullPath(artifactSpecPath))
                                    ?? throw new InvalidOperationException(
                                        $"Unable to resolve artifact spec directory for '{artifactSpecPath}'.");
        var document = ArtifactSpecDocument.Load(artifactSpecPath);
        var normalizedProgressPath = string.IsNullOrWhiteSpace(progressPath)
            ? null
            : Path.GetFullPath(progressPath);
        var artifactSpecSha256 = EffectSummaryHash.FileSha256(artifactSpecPath);
        var completedOutputPaths = normalizedProgressPath == null || !resume || !File.Exists(normalizedProgressPath)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : EffectSummaryProgressStore.LoadArtifactSpec(normalizedProgressPath, artifactSpecSha256);
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> resolvedPurityEntries =
            new Dictionary<string, GeneratedPurityCatalogEntry>(StringComparer.Ordinal);

        foreach (var artifact in document.Artifacts)
        {
            var options = CliOptions.FromArtifactSpec(document.Defaults, artifact, artifactSpecDirectory);
            if (string.IsNullOrWhiteSpace(options.OutputPath))
                throw new ArgumentException("Artifact spec entries require OutputPath.");

            var outputPath = Path.GetFullPath(options.OutputPath!);
            if (completedOutputPaths.Contains(outputPath) && File.Exists(outputPath))
            {
                resolvedPurityEntries = EffectSummaryCatalogReporting.MergeGeneratedPurityEntries(
                    resolvedPurityEntries.Values.Concat(GeneratedPurityCatalogReader.ReadEntries(outputPath)));
                continue;
            }

            completedOutputPaths.Remove(outputPath);
            var effectSummary = EffectSummaryAnalysisPipeline.Analyze(
                options,
                externalGeneratedPurityEntries: resolvedPurityEntries);
            EffectSummaryOutputWriter.WriteDocument(effectSummary, options.OutputPath);
            if (effectSummary.GeneratedPurityCatalog != null)
                resolvedPurityEntries = EffectSummaryCatalogReporting.MergeGeneratedPurityEntries(
                    resolvedPurityEntries.Values.Concat(effectSummary.GeneratedPurityCatalog.Entries));
            completedOutputPaths.Add(outputPath);
            if (normalizedProgressPath != null)
                EffectSummaryProgressStore.SaveArtifactSpec(
                    normalizedProgressPath,
                    artifactSpecSha256,
                    completedOutputPaths);
        }

        if (normalizedProgressPath != null && File.Exists(normalizedProgressPath)) File.Delete(normalizedProgressPath);

        return 0;
    }

    private static int RunSharded(CliOptions options)
    {
        var assemblyPaths = EffectSummaryInputResolver.ResolveAssemblies(options);
        var outputDirectory = Path.GetFullPath(options.ShardOutputPath!);
        Directory.CreateDirectory(outputDirectory);

        var normalizedProgressPath = string.IsNullOrWhiteSpace(options.ProgressPath)
            ? null
            : Path.GetFullPath(options.ProgressPath);
        var inputFingerprint = EffectSummaryProgressStore.ComputeShardedInputFingerprint(options, assemblyPaths);
        var completedOutputPaths = normalizedProgressPath == null || !options.Resume || !File.Exists(normalizedProgressPath)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : EffectSummaryProgressStore.LoadSharded(normalizedProgressPath, inputFingerprint);

        foreach (var assemblyPath in assemblyPaths)
        {
            var outputPath = EffectSummaryInputResolver.GetShardOutputPath(outputDirectory, assemblyPath);
            if (completedOutputPaths.Contains(outputPath) && File.Exists(outputPath)) continue;

            completedOutputPaths.Remove(outputPath);
            EffectSummaryOutputWriter.WriteDocument(
                EffectSummaryAnalysisPipeline.Analyze(options, new[] { assemblyPath }),
                outputPath);
            completedOutputPaths.Add(outputPath);
            if (normalizedProgressPath != null)
                EffectSummaryProgressStore.SaveSharded(
                    normalizedProgressPath,
                    inputFingerprint,
                    completedOutputPaths);
        }

        if (normalizedProgressPath != null && File.Exists(normalizedProgressPath)) File.Delete(normalizedProgressPath);

        return 0;
    }

    private static void PrintHelp()
    {
        Console.Error.WriteLine("SharpProof.EffectSummary");
        Console.Error.WriteLine("Summarizes IL effects from .NET assemblies for evidence-based purity catalog work.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet run --project Tools/SharpProof.EffectSummary -- [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --assembly <path>          Assembly to summarize. Can be repeated.");
        Console.Error.WriteLine(
            "  --artifact-spec <path>     Generate one or more output files from a JSON artifact spec.");
        Console.Error.WriteLine(
            "  --artifact-spec-dependencies <path> Resolve artifact inputs and outputs without generating summaries.");
        Console.Error.WriteLine(
            "  --input-manifest <path>    Write resolved dependency input paths, one per line.");
        Console.Error.WriteLine(
            "  --output-manifest <path>   Write resolved dependency output paths, one per line.");
        Console.Error.WriteLine(
            "  --dependency-output-root <path> Rebase relative artifact outputs for dependency manifests.");
        Console.Error.WriteLine(
            "  --framework <net8.0>       Runtime framework to inspect when --assembly is omitted.");
        Console.Error.WriteLine(
            "  --runtime-assembly <name>  Runtime assembly name when --assembly is omitted. Default: System.Private.CoreLib.dll");
        Console.Error.WriteLine(
            "  --all-runtime-assemblies   Inspect all System runtime assemblies for the target framework.");
        Console.Error.WriteLine(
            "  --shard-output <directory> Write one summary document per input assembly, retaining bounded per-assembly memory.");
        Console.Error.WriteLine(
            "  --symbol-prefix <prefix>   Emit only methods whose decoded symbol starts with this prefix. Can be repeated.");
        Console.Error.WriteLine(
            "  --include-callees          Also emit same-assembly callees reachable from matched symbols.");
        Console.Error.WriteLine(
            "  --max-depth <count>        Maximum same-assembly callee depth when --include-callees is used. Use -1 for unbounded depth. Default: 1.");
        Console.Error.WriteLine(
            "  --max-exception-edges <count> Maximum transitive thrown-exception edges retained per method. Default: 4096.");
        Console.Error.WriteLine(
            "  --transitive-roots         Propagate root candidate labels through same-assembly calls.");
        Console.Error.WriteLine(
            "  --progress <path>          Record completed artifact-spec outputs for resumable generation.");
        Console.Error.WriteLine(
            "  --resume                   Resume completed artifact-spec outputs recorded by --progress.");
        Console.Error.WriteLine(
            "  --classify-purity         Add report-only fixed-point purity classifications to the JSON output.");
        Console.Error.WriteLine(
            "  --bcl-fallback-inventory  Add report-only low-confidence fallback guesses for unresolved BCL members.");
        Console.Error.WriteLine(
            "  --compare-manual-catalogs Compare emitted methods against the current reviewed manual catalogs.");
        Console.Error.WriteLine("  --output <path>            Write JSON to a file instead of stdout.");
        Console.Error.WriteLine("  --limit <count>            Limit emitted method summaries for smoke testing.");
        Console.Error.WriteLine("  --help                     Show this help.");
    }
}
