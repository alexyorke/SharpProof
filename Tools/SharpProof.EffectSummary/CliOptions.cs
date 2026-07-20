internal sealed class CliOptions {
    public const int DefaultMaxExceptionEdges = 4096;

    public List<string> AssemblyPaths { get; } = new();

    public List<string> SymbolPrefixes { get; } = new();

    public List<string> ExactSymbols { get; } = new();

    public List<string> CanonicalKeys { get; } = new();

    public List<string> ExcludedSymbolPrefixes { get; } = new();

    public string? ArtifactSpecPath { get; private set; }

    public bool WriteArtifactSpecDependencyManifests { get; private set; }

    public string? InputManifestPath { get; private set; }

    public string? OutputManifestPath { get; private set; }

    public string? DependencyOutputRoot { get; private set; }

    public string? SourceSummaryPath { get; private set; }

    public string Framework { get; private set; } = "net8.0";

    public string RuntimeAssemblyName { get; private set; } = "System.Private.CoreLib.dll";

    public string? OutputPath { get; private set; }

    public int? Limit { get; private set; }

    public bool IncludeCallees { get; private set; }

    public int MaxDepth { get; private set; } = 1;

    public int MaxExceptionEdges { get; private set; } = DefaultMaxExceptionEdges;

    public string? ProgressPath { get; private set; }

    public string? ShardOutputPath { get; private set; }

    public bool Resume { get; private set; }

    public bool IncludeTransitiveRoots { get; private set; }

    public bool IncludePurityClassification { get; private set; }

    public bool CompareManualCatalogs { get; private set; }

    public bool IncludeBclFallbackInventory { get; private set; }

    public bool AllRuntimeAssemblies { get; private set; }

    public bool ShowHelp { get; private set; }

    private bool IsFromArtifactSpec { get; set; }

    private string? PackageAssemblyPath { get; set; }

    private string? PackageId { get; set; }

    private string? PackageVersion { get; set; }

    private string? PackageAssemblyRelativePath { get; set; }

    private static readonly ToolOptionSet<CliOptions> OptionSet = new ToolOptionSet<CliOptions>()
        .Add(static (o, r, a) => o.AssemblyPaths.Add(r.RequiredValue(a, $"Missing value for {a}.")), "--assembly")
        .Add(static (o, r, a) => o.ArtifactSpecPath = r.RequiredValue(a, $"Missing value for {a}."), "--artifact-spec")
        .Add(static (o, r, a) => {
            o.ArtifactSpecPath = r.RequiredValue(a, $"Missing value for {a}.");
            o.WriteArtifactSpecDependencyManifests = true;
        }, "--artifact-spec-dependencies")
        .Add(static (o, r, a) => o.InputManifestPath = r.RequiredValue(a, $"Missing value for {a}."), "--input-manifest")
        .Add(static (o, r, a) => o.OutputManifestPath = r.RequiredValue(a, $"Missing value for {a}."), "--output-manifest")
        .Add(static (o, r, a) => o.DependencyOutputRoot = r.RequiredValue(a, $"Missing value for {a}."), "--dependency-output-root")
        .Add(static (o, r, a) => o.Framework = r.RequiredValue(a, $"Missing value for {a}."), "--framework")
        .Add(static (o, r, a) => o.RuntimeAssemblyName = r.RequiredValue(a, $"Missing value for {a}."), "--runtime-assembly")
        .Add(static (o, _, _) => o.AllRuntimeAssemblies = true, "--all-runtime-assemblies")
        .Add(static (o, r, a) => o.SymbolPrefixes.Add(r.RequiredValue(a, $"Missing value for {a}.")), "--symbol-prefix")
        .Add(static (o, _, _) => o.IncludeCallees = true, "--include-callees")
        .Add(static (o, r, a) => o.MaxDepth = ReadInt(r, a), "--max-depth")
        .Add(static (o, r, a) => o.MaxExceptionEdges = ReadPositiveInt(r, a), "--max-exception-edges")
        .Add(static (o, _, _) => o.IncludeTransitiveRoots = true, "--transitive-roots")
        .Add(static (o, r, a) => o.ProgressPath = r.RequiredValue(a, $"Missing value for {a}."), "--progress")
        .Add(static (o, r, a) => o.ShardOutputPath = r.RequiredValue(a, $"Missing value for {a}."), "--shard-output")
        .Add(static (o, _, _) => o.Resume = true, "--resume")
        .Add(static (o, _, _) => o.IncludePurityClassification = true, "--classify-purity")
        .Add(static (o, _, _) => o.IncludeBclFallbackInventory = true, "--bcl-fallback-inventory")
        .Add(static (o, _, _) => {
            o.IncludePurityClassification = true;
            o.CompareManualCatalogs = true;
        }, "--compare-manual-catalogs")
        .Add(static (o, r, a) => o.OutputPath = r.RequiredValue(a, $"Missing value for {a}."), "--output")
        .Add(static (o, r, a) => o.Limit = ReadInt(r, a), "--limit")
        .Add(static (o, _, _) => o.ShowHelp = true, "--help", "-h", "/?");

    public static CliOptions Parse(string[] args) {
        var options = new CliOptions();
        OptionSet.Parse(args, options);

        if (!options.ShowHelp) options.Validate();

        return options;
    }

    public static CliOptions FromArtifactSpec(
        ArtifactSpecDefaults? defaults,
        ArtifactSpecEntry artifact,
        string? artifactSpecDirectory = null) {
        var options = new CliOptions {
            Framework = artifact.Framework ?? defaults?.Framework ?? "net8.0",
            RuntimeAssemblyName = artifact.RuntimeAssemblyName ??
                                  defaults?.RuntimeAssemblyName ?? "System.Private.CoreLib.dll",
            OutputPath = ResolveArtifactSpecOutputPath(artifact.OutputPath, artifactSpecDirectory),
            Limit = artifact.Limit ?? defaults?.Limit,
            IncludeCallees = artifact.IncludeCallees ?? defaults?.IncludeCallees ?? false,
            MaxDepth = artifact.MaxDepth ?? defaults?.MaxDepth ?? 1,
            MaxExceptionEdges = artifact.MaxExceptionEdges ?? defaults?.MaxExceptionEdges ?? DefaultMaxExceptionEdges,
            IncludeTransitiveRoots = artifact.IncludeTransitiveRoots ?? defaults?.IncludeTransitiveRoots ?? false,
            IncludePurityClassification =
                artifact.IncludePurityClassification ?? defaults?.IncludePurityClassification ?? false,
            CompareManualCatalogs = artifact.CompareManualCatalogs ?? defaults?.CompareManualCatalogs ?? false,
            IncludeBclFallbackInventory =
                artifact.IncludeBclFallbackInventory ?? defaults?.IncludeBclFallbackInventory ?? false,
            AllRuntimeAssemblies = artifact.AllRuntimeAssemblies ?? defaults?.AllRuntimeAssemblies ?? false,
            IsFromArtifactSpec = true,
            PackageId = artifact.PackageId?.Trim(),
            PackageVersion = artifact.PackageVersion?.Trim(),
            PackageAssemblyRelativePath = artifact.PackageAssemblyRelativePath?.Trim()
        };

        var explicitAssemblyPaths = artifact.AssemblyPaths ?? Array.Empty<string>();
        var hasPackageAssembly = HasPackageAssembly(artifact);
        var packageAssemblyPath = hasPackageAssembly ? ResolveNuGetPackageAssemblyPath(artifact) : null;
        options.PackageAssemblyPath = packageAssemblyPath;
        var hasExplicitRuntimeAssembly = !string.IsNullOrWhiteSpace(artifact.RuntimeAssemblyName);
        if (!options.AllRuntimeAssemblies && hasExplicitRuntimeAssembly &&
            (explicitAssemblyPaths.Length > 0 || hasPackageAssembly))
            options.AssemblyPaths.Add(RuntimeAssemblyResolver.Resolve(options.Framework, options.RuntimeAssemblyName));

        if (artifact.AssemblyPaths != null)
            options.AssemblyPaths.AddRange(ResolveArtifactSpecAssemblyPaths(artifact.AssemblyPaths,
                artifactSpecDirectory));

        if (packageAssemblyPath != null) options.AssemblyPaths.Add(packageAssemblyPath);

        if (options.AssemblyPaths.Count > 1) {
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var distinctResolvedPaths = options.AssemblyPaths
                .Select(Path.GetFullPath)
                .Distinct(pathComparer)
                .ToArray();
            options.AssemblyPaths.Clear();
            options.AssemblyPaths.AddRange(distinctResolvedPaths);
        }

        if (artifact.SymbolPrefixes != null) options.SymbolPrefixes.AddRange(artifact.SymbolPrefixes);

        if (artifact.ExcludedSymbolPrefixes != null)
            options.ExcludedSymbolPrefixes.AddRange(artifact.ExcludedSymbolPrefixes);
        else if (defaults?.ExcludedSymbolPrefixes != null)
            options.ExcludedSymbolPrefixes.AddRange(defaults.ExcludedSymbolPrefixes);

        if (!string.IsNullOrWhiteSpace(artifact.SourceSummaryPath)) {
            options.SourceSummaryPath = ResolveArtifactSpecInputPath(
                artifact.SourceSummaryPath!,
                artifactSpecDirectory);
            var sourceSymbols = ArtifactSpecSymbolSource.LoadSymbols(
                options.SourceSummaryPath,
                options.ExcludedSymbolPrefixes,
                options.SymbolPrefixes);
            options.ExactSymbols.AddRange(sourceSymbols.Symbols);
            options.CanonicalKeys.AddRange(sourceSymbols.CanonicalKeys);
        }

        if (options.CompareManualCatalogs) options.IncludePurityClassification = true;

        options.Validate();

        return options;
    }

    internal EffectSummaryArtifactSource? GetArtifactSource(string assemblyPath) {
        if (!IsFromArtifactSpec) return null;

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedAssemblyPath = Path.GetFullPath(assemblyPath);
        if (!string.IsNullOrWhiteSpace(PackageAssemblyPath) &&
            string.Equals(
                normalizedAssemblyPath,
                Path.GetFullPath(PackageAssemblyPath!),
                pathComparison))
            return new EffectSummaryArtifactSource(
                "package",
                null,
                PackageId,
                PackageVersion,
                PackageAssemblyRelativePath);

        if (AllRuntimeAssemblies || AssemblyPaths.Count == 0)
            return new EffectSummaryArtifactSource("framework", Framework, null, null, null);

        var runtimeAssemblyPath = RuntimeAssemblyResolver.Resolve(Framework, RuntimeAssemblyName);
        return string.Equals(
            normalizedAssemblyPath,
            Path.GetFullPath(runtimeAssemblyPath),
            pathComparison)
            ? new EffectSummaryArtifactSource("framework", Framework, null, null, null)
            : null;
    }

    private void Validate() {
        if (MaxExceptionEdges <= 0) throw new ArgumentException("MaxExceptionEdges must be greater than zero.");

        if (!WriteArtifactSpecDependencyManifests) {
            if (InputManifestPath != null || OutputManifestPath != null || DependencyOutputRoot != null)
                throw new ArgumentException(
                    "--input-manifest, --output-manifest, and --dependency-output-root require --artifact-spec-dependencies.");

            return;
        }

        if (string.IsNullOrWhiteSpace(InputManifestPath) || string.IsNullOrWhiteSpace(OutputManifestPath))
            throw new ArgumentException(
                "--artifact-spec-dependencies requires --input-manifest and --output-manifest.");

        if (AssemblyPaths.Count != 0 || !string.IsNullOrWhiteSpace(ShardOutputPath) ||
            !string.IsNullOrWhiteSpace(OutputPath) || !string.IsNullOrWhiteSpace(ProgressPath) || Resume)
            throw new ArgumentException(
                "--artifact-spec-dependencies cannot be combined with generation, sharding, output, progress, or resume options.");
    }

    private static bool HasPackageAssembly(ArtifactSpecEntry artifact) => !string.IsNullOrWhiteSpace(artifact.PackageId) ||
               !string.IsNullOrWhiteSpace(artifact.PackageVersion) ||
               !string.IsNullOrWhiteSpace(artifact.PackageAssemblyRelativePath);

    private static IEnumerable<string> ResolveArtifactSpecAssemblyPaths(
        IEnumerable<string>? assemblyPaths,
        string? artifactSpecDirectory) {
        if (assemblyPaths == null) return Array.Empty<string>();

        return assemblyPaths.Select(path => ResolveArtifactSpecInputPath(path, artifactSpecDirectory));
    }

    private static string? ResolveArtifactSpecOutputPath(string? path, string? artifactSpecDirectory) {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
            string.IsNullOrWhiteSpace(artifactSpecDirectory)) return path;

        var specRelativeCandidate = Path.GetFullPath(Path.Combine(artifactSpecDirectory, path));
        var currentRelativeCandidate = Path.GetFullPath(path);
        var specRelativeDirectory = Path.GetDirectoryName(specRelativeCandidate);
        var currentRelativeDirectory = Path.GetDirectoryName(currentRelativeCandidate);
        var specDirectoryExists =
            !string.IsNullOrWhiteSpace(specRelativeDirectory) && Directory.Exists(specRelativeDirectory);
        var currentDirectoryExists = !string.IsNullOrWhiteSpace(currentRelativeDirectory) &&
                                     Directory.Exists(currentRelativeDirectory);

        if (specDirectoryExists || !currentDirectoryExists) return specRelativeCandidate;

        return currentRelativeCandidate;
    }

    private static string ResolveArtifactSpecInputPath(string path, string? artifactSpecDirectory) {
        if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(artifactSpecDirectory)) return path;

        var specRelativeCandidate = Path.GetFullPath(Path.Combine(artifactSpecDirectory, path));
        if (File.Exists(specRelativeCandidate) || Directory.Exists(specRelativeCandidate)) return specRelativeCandidate;

        var currentRelativeCandidate = Path.GetFullPath(path);
        if (File.Exists(currentRelativeCandidate) || Directory.Exists(currentRelativeCandidate))
            return currentRelativeCandidate;

        return specRelativeCandidate;
    }

    private static string ResolveNuGetPackageAssemblyPath(ArtifactSpecEntry artifact) {
        if (string.IsNullOrWhiteSpace(artifact.PackageId) ||
            string.IsNullOrWhiteSpace(artifact.PackageVersion) ||
            string.IsNullOrWhiteSpace(artifact.PackageAssemblyRelativePath))
            throw new InvalidOperationException(
                "Artifact spec package assembly resolution requires PackageId, PackageVersion, and PackageAssemblyRelativePath.");

        var packageRoot = ResolveNuGetPackageRoot();
        var packageIdDirectory = Path.Combine(packageRoot, artifact.PackageId!.Trim().ToLowerInvariant());
        var packageVersionDirectory =
            ResolveNuGetPackageVersionDirectoryPath(packageIdDirectory, artifact.PackageVersion!.Trim());
        var relativePath = NormalizePackageAssemblyRelativePath(artifact.PackageAssemblyRelativePath!);
        var assemblyPath = Path.GetFullPath(Path.Combine(packageVersionDirectory, relativePath));
        if (!IsPathWithinDirectory(assemblyPath, packageVersionDirectory))
            throw new InvalidOperationException(
                $"Artifact spec package assembly path '{artifact.PackageAssemblyRelativePath}' must stay within package '{artifact.PackageId} {artifact.PackageVersion}'.");

        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException(
                $"Artifact spec package assembly '{artifact.PackageId} {artifact.PackageVersion} {artifact.PackageAssemblyRelativePath}' was not found at '{assemblyPath}'.",
                assemblyPath);

        return assemblyPath;
    }

    private static string ResolveNuGetPackageVersionDirectoryPath(string packageIdDirectory, string packageVersion) {
        var exactDirectory = Path.Combine(packageIdDirectory, packageVersion.Trim().ToLowerInvariant());
        if (Directory.Exists(exactDirectory)) return Path.GetFullPath(exactDirectory);

        if (!Directory.Exists(packageIdDirectory))
            throw new DirectoryNotFoundException(
                $"NuGet package directory '{packageIdDirectory}' was not found.");

        var normalizedRequestedVersion = NormalizeNuGetVersionIdentity(packageVersion);
        foreach (var candidateDirectory in Directory.EnumerateDirectories(packageIdDirectory)) {
            var candidateVersion = Path.GetFileName(candidateDirectory);
            if (string.Equals(
                    NormalizeNuGetVersionIdentity(candidateVersion),
                    normalizedRequestedVersion,
                    StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(candidateDirectory);
        }

        throw new DirectoryNotFoundException(
            $"NuGet package version directory for '{Path.GetFileName(packageIdDirectory)} {packageVersion}' was not found under '{packageIdDirectory}'.");
    }

    private static string NormalizePackageAssemblyRelativePath(string relativePath) {
        var normalizedPath = relativePath
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            throw new InvalidOperationException("Artifact spec package assembly path cannot be empty.");

        if (Path.IsPathRooted(normalizedPath))
            throw new InvalidOperationException(
                $"Artifact spec package assembly path '{relativePath}' must be a relative path.");

        return normalizedPath;
    }

    private static bool IsPathWithinDirectory(string candidatePath, string directoryPath) {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedDirectoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        var normalizedCandidatePath = Path.GetFullPath(candidatePath);
        return string.Equals(normalizedCandidatePath, normalizedDirectoryPath, comparison) ||
               normalizedCandidatePath.StartsWith(normalizedDirectoryPath + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizeNuGetVersionIdentity(string version) {
        var trimmed = version.Trim();
        if (trimmed.Length == 0) return string.Empty;

        var metadataSeparatorIndex = trimmed.IndexOf('+');
        if (metadataSeparatorIndex >= 0) trimmed = trimmed[..metadataSeparatorIndex];

        var prereleaseSeparatorIndex = trimmed.IndexOf('-');
        var releasePart = prereleaseSeparatorIndex >= 0 ? trimmed[..prereleaseSeparatorIndex] : trimmed;
        var prereleasePart = prereleaseSeparatorIndex >= 0 ? trimmed[(prereleaseSeparatorIndex + 1)..] : string.Empty;

        var releaseSegments = releasePart
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => int.TryParse(segment, out var numericSegment) ? numericSegment.ToString() : segment)
            .ToList();
        while (releaseSegments.Count > 1 && string.Equals(releaseSegments[^1], "0", StringComparison.Ordinal))
            releaseSegments.RemoveAt(releaseSegments.Count - 1);

        var normalizedRelease = releaseSegments.Count == 0 ? "0" : string.Join(".", releaseSegments);
        if (prereleasePart.Length == 0) return normalizedRelease;

        return normalizedRelease + "-" + prereleasePart.ToLowerInvariant();
    }

    private static string ResolveNuGetPackageRoot() {
        var configuredRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configuredRoot)) return Path.GetFullPath(configuredRoot.Trim());

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            throw new InvalidOperationException(
                "Unable to resolve the NuGet package root because NUGET_PACKAGES is unset and the user profile directory is unavailable.");

        return Path.Combine(userProfile, ".nuget", "packages");
    }

    private static int ReadPositiveInt(ToolArgumentReader reader, string option) {
        var value = ReadInt(reader, option);
        if (value <= 0) throw new ArgumentException($"{option} must be greater than zero.");

        return value;
    }

    private static int ReadInt(ToolArgumentReader reader, string option) {
        var text = reader.RequiredValue(option, $"Missing value for {option}.");
        if (!int.TryParse(text, out var value))
            throw new ArgumentException($"{option} requires an integer value, but received '{text}'.");

        return value;
    }
}
