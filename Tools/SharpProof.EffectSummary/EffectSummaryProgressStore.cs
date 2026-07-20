internal static class EffectSummaryProgressStore {
    public static string ComputeShardedInputFingerprint(
        CliOptions options,
        IReadOnlyList<string> assemblyPaths) {
        var payload = JsonSerializer.Serialize(new {
            ToolModuleVersionId = ToolModuleVersionId,
            Assemblies = assemblyPaths.Select(path => new {
                Path = Path.GetFullPath(path),
                Sha256 = EffectSummaryHash.FileSha256(path)
            }),
            options.Limit,
            options.SymbolPrefixes,
            options.ExactSymbols,
            options.CanonicalKeys,
            options.ExcludedSymbolPrefixes,
            options.IncludeCallees,
            options.MaxDepth,
            options.MaxExceptionEdges,
            options.IncludeTransitiveRoots,
            options.IncludePurityClassification,
            options.CompareManualCatalogs,
            options.IncludeBclFallbackInventory
        });
        return EffectSummaryHash.Sha256(payload);
    }

    public static HashSet<string> LoadSharded(string progressPath, string inputFingerprint) =>
        LoadCompletedOutputPaths<ShardedEffectSummaryProgressDocument>(
            progressPath,
            inputFingerprint,
            $"Unsupported sharded effect-summary progress schema in '{progressPath}'.",
            $"Sharded effect-summary progress '{progressPath}' does not match the current inputs. Delete the progress file or regenerate it.");

    public static void SaveSharded(
        string progressPath,
        string inputFingerprint,
        IEnumerable<string> completedOutputPaths) => SaveJson(
            progressPath,
            new ShardedEffectSummaryProgressDocument(
                1,
                ToolModuleVersionId,
                inputFingerprint,
                completedOutputPaths
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()));

    public static HashSet<string> LoadArtifactSpec(string progressPath, string artifactSpecSha256) =>
        LoadCompletedOutputPaths<ArtifactSpecProgressDocument>(
            progressPath,
            artifactSpecSha256,
            $"Unsupported artifact-spec progress schema in '{progressPath}'.",
            $"Artifact-spec progress '{progressPath}' does not match artifact spec '{artifactSpecSha256}'. Delete the progress file or regenerate it.");

    public static void SaveArtifactSpec(
        string progressPath,
        string artifactSpecSha256,
        IEnumerable<string> completedOutputPaths) => SaveJson(
            progressPath,
            new ArtifactSpecProgressDocument(
                1,
                artifactSpecSha256,
                completedOutputPaths
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()));

    private static string ToolModuleVersionId =>
        typeof(EffectSummaryProgressStore).Assembly.ManifestModule.ModuleVersionId.ToString("D");

    private static HashSet<string> LoadCompletedOutputPaths<TProgress>(
        string progressPath,
        string expectedFingerprint,
        string unsupportedSchemaMessage,
        string fingerprintMismatchMessage)
        where TProgress : IEffectSummaryProgressDocument {
        var progress = JsonSerializer.Deserialize<TProgress>(File.ReadAllText(progressPath));
        if (progress == null || progress.SchemaVersion != 1)
            throw new InvalidOperationException(unsupportedSchemaMessage);

        if (!string.Equals(progress.Fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(fingerprintMismatchMessage);

        return (progress.CompletedOutputPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveJson(string progressPath, object progress) {
        Directory.CreateDirectory(Path.GetDirectoryName(progressPath)!);
        var temporaryPath = progressPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, progressPath, true);
        }
        finally {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
