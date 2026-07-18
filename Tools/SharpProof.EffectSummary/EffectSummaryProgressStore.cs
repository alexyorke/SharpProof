internal static class EffectSummaryProgressStore
{
    public static string ComputeShardedInputFingerprint(
        CliOptions options,
        IReadOnlyList<string> assemblyPaths)
    {
        var payload = JsonSerializer.Serialize(new
        {
            ToolModuleVersionId = ToolModuleVersionId,
            Assemblies = assemblyPaths.Select(path => new
            {
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
        LoadCompletedOutputPaths(
            progressPath,
            "InputFingerprint",
            inputFingerprint,
            $"Unsupported sharded effect-summary progress schema in '{progressPath}'.",
            $"Sharded effect-summary progress '{progressPath}' does not match the current inputs. Delete the progress file or regenerate it.");

    public static void SaveSharded(
        string progressPath,
        string inputFingerprint,
        IEnumerable<string> completedOutputPaths)
    {
        SaveJson(
            progressPath,
            new ShardedEffectSummaryProgressDocument
            {
                SchemaVersion = 1,
                ToolModuleVersionId = ToolModuleVersionId,
                InputFingerprint = inputFingerprint,
                CompletedOutputPaths = completedOutputPaths
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
    }

    public static HashSet<string> LoadArtifactSpec(string progressPath, string artifactSpecSha256) =>
        LoadCompletedOutputPaths(
            progressPath,
            "ArtifactSpecSha256",
            artifactSpecSha256,
            $"Unsupported artifact-spec progress schema in '{progressPath}'.",
            $"Artifact-spec progress '{progressPath}' does not match artifact spec '{artifactSpecSha256}'. Delete the progress file or regenerate it.");

    public static void SaveArtifactSpec(
        string progressPath,
        string artifactSpecSha256,
        IEnumerable<string> completedOutputPaths)
    {
        SaveJson(
            progressPath,
            new ArtifactSpecProgressDocument
            {
                SchemaVersion = 1,
                ArtifactSpecSha256 = artifactSpecSha256,
                CompletedOutputPaths = completedOutputPaths
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
    }

    private static string ToolModuleVersionId =>
        typeof(EffectSummaryProgressStore).Assembly.ManifestModule.ModuleVersionId.ToString("D");

    private static HashSet<string> LoadCompletedOutputPaths(
        string progressPath,
        string fingerprintPropertyName,
        string expectedFingerprint,
        string unsupportedSchemaMessage,
        string fingerprintMismatchMessage)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(progressPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("SchemaVersion", out var schemaVersionElement) ||
            schemaVersionElement.ValueKind != JsonValueKind.Number ||
            schemaVersionElement.GetInt32() != 1)
            throw new InvalidOperationException(unsupportedSchemaMessage);

        var recordedFingerprint = root.TryGetProperty(fingerprintPropertyName, out var fingerprintElement)
            ? fingerprintElement.GetString()
            : null;
        if (!string.Equals(recordedFingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(fingerprintMismatchMessage);

        var completedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("CompletedOutputPaths", out var completedElement) &&
            completedElement.ValueKind == JsonValueKind.Array)
            foreach (var pathElement in completedElement.EnumerateArray())
            {
                var path = pathElement.GetString();
                if (!string.IsNullOrWhiteSpace(path)) completedOutputPaths.Add(Path.GetFullPath(path));
            }

        return completedOutputPaths;
    }

    private static void SaveJson(string progressPath, object progress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(progressPath)!);
        var temporaryPath = progressPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, progressPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
