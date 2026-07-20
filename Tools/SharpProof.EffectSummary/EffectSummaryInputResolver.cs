internal static class EffectSummaryInputResolver {
    public static string[] ResolveAssemblies(CliOptions options) {
        if (options.AssemblyPaths.Count != 0)
            return options.AssemblyPaths.Select(Path.GetFullPath).ToArray();

        return options.AllRuntimeAssemblies
            ? RuntimeAssemblyResolver.ResolveSystemRuntimeAssemblies(options.Framework)
            : new[] { RuntimeAssemblyResolver.Resolve(options.Framework, options.RuntimeAssemblyName) };
    }

    public static string ResolveDependencyOutputPath(
        string? artifactOutputPath,
        string? outputRoot,
        string artifactSpecDirectory) {
        if (string.IsNullOrWhiteSpace(artifactOutputPath))
            throw new ArgumentException("Artifact spec entries require OutputPath.");

        if (Path.IsPathRooted(artifactOutputPath)) {
            if (!string.IsNullOrWhiteSpace(outputRoot))
                throw new InvalidOperationException(
                    "Artifact spec OutputPath values must be relative when --dependency-output-root is used.");

            return Path.GetFullPath(artifactOutputPath);
        }

        var baseDirectory = string.IsNullOrWhiteSpace(outputRoot)
            ? artifactSpecDirectory
            : Path.GetFullPath(outputRoot);
        return Path.GetFullPath(Path.Combine(baseDirectory, artifactOutputPath));
    }

    public static string GetShardOutputPath(string outputDirectory, string assemblyPath) {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        if (string.IsNullOrWhiteSpace(assemblyName)) assemblyName = "assembly";

        var invalidFileNameCharacters = Path.GetInvalidFileNameChars();
        var safeAssemblyName = new string(
            assemblyName
                .Select(character => invalidFileNameCharacters.Contains(character) ? '_' : character)
                .ToArray());
        var pathHash = EffectSummaryHash.Sha256(Path.GetFullPath(assemblyPath))[..12];
        return Path.Combine(
            outputDirectory,
            $"{safeAssemblyName}.{pathHash}.SharpProof.EffectSummary.json");
    }
}
