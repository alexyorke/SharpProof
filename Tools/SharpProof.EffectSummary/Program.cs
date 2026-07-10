using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

return EffectSummaryCli.Run(args);

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

        if (!string.IsNullOrWhiteSpace(options.ArtifactSpecPath))
        {
            return RunArtifactSpec(options.ArtifactSpecPath!);
        }

        WriteDocument(BuildDocument(options), options.OutputPath);
        return 0;
    }

    private static int RunArtifactSpec(string artifactSpecPath)
    {
        var artifactSpecDirectory = Path.GetDirectoryName(Path.GetFullPath(artifactSpecPath))
            ?? throw new InvalidOperationException($"Unable to resolve artifact spec directory for '{artifactSpecPath}'.");
        var document = ArtifactSpecDocument.Load(artifactSpecPath);
        foreach (var artifact in document.Artifacts)
        {
            var options = CliOptions.FromArtifactSpec(document.Defaults, artifact, artifactSpecDirectory);
            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                throw new ArgumentException("Artifact spec entries require OutputPath.");
            }

            WriteDocument(BuildDocument(options), options.OutputPath);
        }

        return 0;
    }

    private static EffectSummaryDocument BuildDocument(CliOptions options)
    {
        var assemblies = ResolveInputAssemblies(options);

        var reports = assemblies
            .Select(path => AssemblyEffectSummarizer.Summarize(
                path,
                options.Limit,
                options.SymbolPrefixes,
                options.ExactSymbols,
                options.ExactSymbolKeys,
                options.IncludeCallees,
                options.MaxDepth,
                options.IncludeTransitiveRoots,
                options.MaxExceptionEdges))
            .ToArray();

        if (options.ExcludedSymbolPrefixes.Count > 0)
        {
            reports = reports
                .Select(report => ArtifactSpecSymbolFilter.Exclude(report, options.ExcludedSymbolPrefixes))
                .ToArray();
        }

        PurityClassificationReport? purityClassificationReport = null;
        GeneratedPurityCatalogDocument? generatedPurityCatalog = null;
        if (options.IncludePurityClassification || options.CompareManualCatalogs)
        {
            var classificationOutput = PurityClassificationEngine.Classify(
                reports,
                includeCatalogComparison: options.CompareManualCatalogs);
            reports = classificationOutput.Assemblies;
            purityClassificationReport = classificationOutput.Report;
            generatedPurityCatalog = classificationOutput.GeneratedPurityCatalog;
        }

        var bclFallbackInventory = options.IncludeBclFallbackInventory
            ? BclFallbackInventoryBuilder.Build(reports)
            : null;

        var document = new EffectSummaryDocument(
            SchemaVersion: bclFallbackInventory == null
                ? (purityClassificationReport == null ? 1 : 3)
                : 4,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Assemblies: reports,
            PurityReport: purityClassificationReport,
            GeneratedPurityCatalog: generatedPurityCatalog,
            BclFallbackInventory: bclFallbackInventory);

        return document;
    }

    private static string[] ResolveInputAssemblies(CliOptions options)
    {
        if (options.AssemblyPaths.Count != 0)
        {
            return options.AssemblyPaths.Select(Path.GetFullPath).ToArray();
        }

        return options.AllRuntimeAssemblies
            ? RuntimeAssemblyResolver.ResolveSystemRuntimeAssemblies(options.Framework)
            : new[] { RuntimeAssemblyResolver.Resolve(options.Framework, options.RuntimeAssemblyName) };
    }
    private static void WriteDocument(EffectSummaryDocument document, string? outputPath)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };

        var json = JsonSerializer.Serialize(document, jsonOptions);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.WriteLine(json);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            File.WriteAllText(outputPath, json);
        }
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
        Console.Error.WriteLine("  --artifact-spec <path>     Generate one or more output files from a JSON artifact spec.");
        Console.Error.WriteLine("  --framework <net8.0>       Runtime framework to inspect when --assembly is omitted.");
        Console.Error.WriteLine("  --runtime-assembly <name>  Runtime assembly name when --assembly is omitted. Default: System.Private.CoreLib.dll");
        Console.Error.WriteLine("  --all-runtime-assemblies   Inspect all System runtime assemblies for the target framework.");
        Console.Error.WriteLine("  --symbol-prefix <prefix>   Emit only methods whose decoded symbol starts with this prefix. Can be repeated.");
        Console.Error.WriteLine("  --include-callees          Also emit same-assembly callees reachable from matched symbols.");
        Console.Error.WriteLine("  --max-depth <count>        Maximum same-assembly callee depth when --include-callees is used. Use -1 for unbounded depth. Default: 1.");
        Console.Error.WriteLine("  --max-exception-edges <count> Maximum transitive thrown-exception edges retained per method. Default: 4096.");
        Console.Error.WriteLine("  --transitive-roots         Propagate root candidate labels through same-assembly calls.");
        Console.Error.WriteLine("  --classify-purity         Add report-only fixed-point purity classifications to the JSON output.");
        Console.Error.WriteLine("  --bcl-fallback-inventory  Add report-only low-confidence fallback guesses for unresolved BCL members.");
        Console.Error.WriteLine("  --compare-manual-catalogs Compare emitted methods against the current reviewed manual catalogs.");
        Console.Error.WriteLine("  --output <path>            Write JSON to a file instead of stdout.");
        Console.Error.WriteLine("  --limit <count>            Limit emitted method summaries for smoke testing.");
        Console.Error.WriteLine("  --help                     Show this help.");
    }
}

internal sealed class CliOptions
{
    public const int DefaultMaxExceptionEdges = 4096;

    public List<string> AssemblyPaths { get; } = new();

    public List<string> SymbolPrefixes { get; } = new();

    public List<string> ExactSymbols { get; } = new();

    public List<string> ExactSymbolKeys { get; } = new();

    public List<string> ExcludedSymbolPrefixes { get; } = new();

    public string? ArtifactSpecPath { get; private set; }

    public string Framework { get; private set; } = "net8.0";

    public string RuntimeAssemblyName { get; private set; } = "System.Private.CoreLib.dll";

    public string? OutputPath { get; private set; }

    public int? Limit { get; private set; }

    public bool IncludeCallees { get; private set; }

    public int MaxDepth { get; private set; } = 1;

    public int MaxExceptionEdges { get; private set; } = DefaultMaxExceptionEdges;

    public bool IncludeTransitiveRoots { get; private set; }

    public bool IncludePurityClassification { get; private set; }

    public bool CompareManualCatalogs { get; private set; }

    public bool IncludeBclFallbackInventory { get; private set; }

    public bool AllRuntimeAssemblies { get; private set; }

    public bool ShowHelp { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--assembly":
                    options.AssemblyPaths.Add(ReadRequiredValue(args, ref i, arg));
                    break;
                case "--artifact-spec":
                    options.ArtifactSpecPath = ReadRequiredValue(args, ref i, arg);
                    break;
                case "--framework":
                    options.Framework = ReadRequiredValue(args, ref i, arg);
                    break;
                case "--runtime-assembly":
                    options.RuntimeAssemblyName = ReadRequiredValue(args, ref i, arg);
                    break;
                case "--all-runtime-assemblies":
                    options.AllRuntimeAssemblies = true;
                    break;
                case "--symbol-prefix":
                    options.SymbolPrefixes.Add(ReadRequiredValue(args, ref i, arg));
                    break;
                case "--include-callees":
                    options.IncludeCallees = true;
                    break;
                case "--max-depth":
                    options.MaxDepth = int.Parse(ReadRequiredValue(args, ref i, arg));
                    break;
                case "--max-exception-edges":
                    options.MaxExceptionEdges = ReadPositiveInt(args, ref i, arg);
                    break;
                case "--transitive-roots":
                    options.IncludeTransitiveRoots = true;
                    break;
                case "--classify-purity":
                    options.IncludePurityClassification = true;
                    break;
                case "--bcl-fallback-inventory":
                    options.IncludeBclFallbackInventory = true;
                    break;
                case "--compare-manual-catalogs":
                    options.IncludePurityClassification = true;
                    options.CompareManualCatalogs = true;
                    break;
                case "--output":
                    options.OutputPath = ReadRequiredValue(args, ref i, arg);
                    break;
                case "--limit":
                    options.Limit = int.Parse(ReadRequiredValue(args, ref i, arg));
                    break;
                case "--help":
                case "-h":
                case "/?":
                    options.ShowHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        if (!options.ShowHelp)
        {
            options.Validate();
        }

        return options;
    }

    public static CliOptions FromArtifactSpec(
        ArtifactSpecDefaults? defaults,
        ArtifactSpecEntry artifact,
        string? artifactSpecDirectory = null)
    {
        var options = new CliOptions
        {
            Framework = artifact.Framework ?? defaults?.Framework ?? "net8.0",
            RuntimeAssemblyName = artifact.RuntimeAssemblyName ?? defaults?.RuntimeAssemblyName ?? "System.Private.CoreLib.dll",
            OutputPath = ResolveArtifactSpecOutputPath(artifact.OutputPath, artifactSpecDirectory),
            Limit = artifact.Limit ?? defaults?.Limit,
            IncludeCallees = artifact.IncludeCallees ?? defaults?.IncludeCallees ?? false,
            MaxDepth = artifact.MaxDepth ?? defaults?.MaxDepth ?? 1,
            MaxExceptionEdges = artifact.MaxExceptionEdges ?? defaults?.MaxExceptionEdges ?? DefaultMaxExceptionEdges,
            IncludeTransitiveRoots = artifact.IncludeTransitiveRoots ?? defaults?.IncludeTransitiveRoots ?? false,
            IncludePurityClassification = artifact.IncludePurityClassification ?? defaults?.IncludePurityClassification ?? false,
            CompareManualCatalogs = artifact.CompareManualCatalogs ?? defaults?.CompareManualCatalogs ?? false,
            IncludeBclFallbackInventory = artifact.IncludeBclFallbackInventory ?? defaults?.IncludeBclFallbackInventory ?? false,
            AllRuntimeAssemblies = artifact.AllRuntimeAssemblies ?? defaults?.AllRuntimeAssemblies ?? false,
        };

        var explicitAssemblyPaths = artifact.AssemblyPaths ?? Array.Empty<string>();
        var hasPackageAssembly = HasPackageAssembly(artifact);
        var hasExplicitRuntimeAssembly = !string.IsNullOrWhiteSpace(artifact.RuntimeAssemblyName);
        if (!options.AllRuntimeAssemblies && hasExplicitRuntimeAssembly && (explicitAssemblyPaths.Length > 0 || hasPackageAssembly))
        {
            options.AssemblyPaths.Add(RuntimeAssemblyResolver.Resolve(options.Framework, options.RuntimeAssemblyName));
        }

        if (artifact.AssemblyPaths != null)
        {
            options.AssemblyPaths.AddRange(ResolveArtifactSpecAssemblyPaths(artifact.AssemblyPaths, artifactSpecDirectory));
        }

        if (hasPackageAssembly)
        {
            options.AssemblyPaths.Add(ResolveNuGetPackageAssemblyPath(artifact));
        }

        if (options.AssemblyPaths.Count > 1)
        {
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            options.AssemblyPaths.Clear();
            options.AssemblyPaths.AddRange(
                (!options.AllRuntimeAssemblies && hasExplicitRuntimeAssembly && (explicitAssemblyPaths.Length > 0 || hasPackageAssembly)
                    ? new[] { RuntimeAssemblyResolver.Resolve(options.Framework, options.RuntimeAssemblyName) }
                    : Array.Empty<string>())
                .Concat(explicitAssemblyPaths)
                .Concat(hasPackageAssembly ? new[] { ResolveNuGetPackageAssemblyPath(artifact) } : Array.Empty<string>())
                .Select(Path.GetFullPath)
                .Distinct(pathComparer));
        }

        if (artifact.SymbolPrefixes != null)
        {
            options.SymbolPrefixes.AddRange(artifact.SymbolPrefixes);
        }

        if (artifact.ExcludedSymbolPrefixes != null)
        {
            options.ExcludedSymbolPrefixes.AddRange(artifact.ExcludedSymbolPrefixes);
        }
        else if (defaults?.ExcludedSymbolPrefixes != null)
        {
            options.ExcludedSymbolPrefixes.AddRange(defaults.ExcludedSymbolPrefixes);
        }

        if (!string.IsNullOrWhiteSpace(artifact.SourceSummaryPath))
        {
            var sourceSymbols = ArtifactSpecSymbolSource.LoadSymbols(
                ResolveArtifactSpecInputPath(artifact.SourceSummaryPath!, artifactSpecDirectory),
                options.ExcludedSymbolPrefixes,
                options.SymbolPrefixes);
            options.ExactSymbols.AddRange(sourceSymbols.Symbols);
            options.ExactSymbolKeys.AddRange(sourceSymbols.ExactSymbolKeys);
        }

        if (options.CompareManualCatalogs)
        {
            options.IncludePurityClassification = true;
        }

        options.Validate();

        return options;
    }

    private void Validate()
    {
        if (MaxExceptionEdges <= 0)
        {
            throw new ArgumentException("MaxExceptionEdges must be greater than zero.");
        }
    }

    private static bool HasPackageAssembly(ArtifactSpecEntry artifact)
    {
        return !string.IsNullOrWhiteSpace(artifact.PackageId) ||
            !string.IsNullOrWhiteSpace(artifact.PackageVersion) ||
            !string.IsNullOrWhiteSpace(artifact.PackageAssemblyRelativePath);
    }

    private static IEnumerable<string> ResolveArtifactSpecAssemblyPaths(
        IEnumerable<string>? assemblyPaths,
        string? artifactSpecDirectory)
    {
        if (assemblyPaths == null)
        {
            return Array.Empty<string>();
        }

        return assemblyPaths.Select(path => ResolveArtifactSpecInputPath(path, artifactSpecDirectory));
    }

    private static string? ResolveArtifactSpecOutputPath(string? path, string? artifactSpecDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(artifactSpecDirectory))
        {
            return path;
        }

        var specRelativeCandidate = Path.GetFullPath(Path.Combine(artifactSpecDirectory, path));
        var currentRelativeCandidate = Path.GetFullPath(path);
        var specRelativeDirectory = Path.GetDirectoryName(specRelativeCandidate);
        var currentRelativeDirectory = Path.GetDirectoryName(currentRelativeCandidate);
        var specDirectoryExists = !string.IsNullOrWhiteSpace(specRelativeDirectory) && Directory.Exists(specRelativeDirectory);
        var currentDirectoryExists = !string.IsNullOrWhiteSpace(currentRelativeDirectory) && Directory.Exists(currentRelativeDirectory);

        if (specDirectoryExists || !currentDirectoryExists)
        {
            return specRelativeCandidate;
        }

        return currentRelativeCandidate;
    }

    private static string ResolveArtifactSpecInputPath(string path, string? artifactSpecDirectory)
    {
        if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(artifactSpecDirectory))
        {
            return path;
        }

        var specRelativeCandidate = Path.GetFullPath(Path.Combine(artifactSpecDirectory, path));
        if (File.Exists(specRelativeCandidate) || Directory.Exists(specRelativeCandidate))
        {
            return specRelativeCandidate;
        }

        var currentRelativeCandidate = Path.GetFullPath(path);
        if (File.Exists(currentRelativeCandidate) || Directory.Exists(currentRelativeCandidate))
        {
            return currentRelativeCandidate;
        }

        return specRelativeCandidate;
    }

    private static string ResolveNuGetPackageAssemblyPath(ArtifactSpecEntry artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.PackageId) ||
            string.IsNullOrWhiteSpace(artifact.PackageVersion) ||
            string.IsNullOrWhiteSpace(artifact.PackageAssemblyRelativePath))
        {
            throw new InvalidOperationException(
                "Artifact spec package assembly resolution requires PackageId, PackageVersion, and PackageAssemblyRelativePath.");
        }

        var packageRoot = ResolveNuGetPackageRoot();
        var packageIdDirectory = Path.Combine(packageRoot, artifact.PackageId!.Trim().ToLowerInvariant());
        var packageVersionDirectory = ResolveNuGetPackageVersionDirectoryPath(packageIdDirectory, artifact.PackageVersion!.Trim());
        var relativePath = NormalizePackageAssemblyRelativePath(artifact.PackageAssemblyRelativePath!);
        var assemblyPath = Path.GetFullPath(Path.Combine(packageVersionDirectory, relativePath));
        if (!IsPathWithinDirectory(assemblyPath, packageVersionDirectory))
        {
            throw new InvalidOperationException(
                $"Artifact spec package assembly path '{artifact.PackageAssemblyRelativePath}' must stay within package '{artifact.PackageId} {artifact.PackageVersion}'.");
        }

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(
                $"Artifact spec package assembly '{artifact.PackageId} {artifact.PackageVersion} {artifact.PackageAssemblyRelativePath}' was not found at '{assemblyPath}'.",
                assemblyPath);
        }

        return assemblyPath;
    }

    private static string ResolveNuGetPackageVersionDirectoryPath(string packageIdDirectory, string packageVersion)
    {
        var exactDirectory = Path.Combine(packageIdDirectory, packageVersion.Trim().ToLowerInvariant());
        if (Directory.Exists(exactDirectory))
        {
            return Path.GetFullPath(exactDirectory);
        }

        if (!Directory.Exists(packageIdDirectory))
        {
            throw new DirectoryNotFoundException(
                $"NuGet package directory '{packageIdDirectory}' was not found.");
        }

        var normalizedRequestedVersion = NormalizeNuGetVersionIdentity(packageVersion);
        foreach (var candidateDirectory in Directory.EnumerateDirectories(packageIdDirectory))
        {
            var candidateVersion = Path.GetFileName(candidateDirectory);
            if (string.Equals(
                NormalizeNuGetVersionIdentity(candidateVersion),
                normalizedRequestedVersion,
                StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(candidateDirectory);
            }
        }

        throw new DirectoryNotFoundException(
            $"NuGet package version directory for '{Path.GetFileName(packageIdDirectory)} {packageVersion}' was not found under '{packageIdDirectory}'.");
    }

    private static string NormalizePackageAssemblyRelativePath(string relativePath)
    {
        var normalizedPath = relativePath
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new InvalidOperationException("Artifact spec package assembly path cannot be empty.");
        }

        if (Path.IsPathRooted(normalizedPath))
        {
            throw new InvalidOperationException(
                $"Artifact spec package assembly path '{relativePath}' must be a relative path.");
        }

        return normalizedPath;
    }

    private static bool IsPathWithinDirectory(string candidatePath, string directoryPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedDirectoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        var normalizedCandidatePath = Path.GetFullPath(candidatePath);
        return string.Equals(normalizedCandidatePath, normalizedDirectoryPath, comparison) ||
            normalizedCandidatePath.StartsWith(normalizedDirectoryPath + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizeNuGetVersionIdentity(string version)
    {
        var trimmed = version.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var metadataSeparatorIndex = trimmed.IndexOf('+');
        if (metadataSeparatorIndex >= 0)
        {
            trimmed = trimmed[..metadataSeparatorIndex];
        }

        var prereleaseSeparatorIndex = trimmed.IndexOf('-');
        var releasePart = prereleaseSeparatorIndex >= 0 ? trimmed[..prereleaseSeparatorIndex] : trimmed;
        var prereleasePart = prereleaseSeparatorIndex >= 0 ? trimmed[(prereleaseSeparatorIndex + 1)..] : string.Empty;

        var releaseSegments = releasePart
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => int.TryParse(segment, out var numericSegment) ? numericSegment.ToString() : segment)
            .ToList();
        while (releaseSegments.Count > 1 && string.Equals(releaseSegments[^1], "0", StringComparison.Ordinal))
        {
            releaseSegments.RemoveAt(releaseSegments.Count - 1);
        }

        var normalizedRelease = releaseSegments.Count == 0 ? "0" : string.Join(".", releaseSegments);
        if (prereleasePart.Length == 0)
        {
            return normalizedRelease;
        }

        return normalizedRelease + "-" + prereleasePart.ToLowerInvariant();
    }

    private static string ResolveNuGetPackageRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.GetFullPath(configuredRoot.Trim());
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException(
                "Unable to resolve the NuGet package root because NUGET_PACKAGES is unset and the user profile directory is unavailable.");
        }

        return Path.Combine(userProfile, ".nuget", "packages");
    }

    private static string ReadRequiredValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {option}.");
        }

        index++;
        return args[index];
    }

    private static int ReadPositiveInt(string[] args, ref int index, string option)
    {
        var value = int.Parse(ReadRequiredValue(args, ref index, option));
        if (value <= 0)
        {
            throw new ArgumentException($"{option} must be greater than zero.");
        }

        return value;
    }
}

internal sealed class ArtifactSpecDocument
{
    public int SchemaVersion { get; set; }

    public ArtifactSpecDefaults? Defaults { get; set; }

    public ArtifactSpecEntry[] Artifacts { get; set; } = Array.Empty<ArtifactSpecEntry>();

    public static ArtifactSpecDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<ArtifactSpecDocument>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidOperationException($"Failed to deserialize artifact spec '{path}'.");

        if (document.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported artifact spec schema version '{document.SchemaVersion}' in '{path}'.");
        }

        if (document.Artifacts.Length == 0)
        {
            throw new InvalidOperationException($"Artifact spec '{path}' does not contain any artifacts.");
        }

        return document;
    }
}

internal sealed class ArtifactSpecDefaults
{
    public string? Framework { get; set; }

    public string? RuntimeAssemblyName { get; set; }

    public int? Limit { get; set; }

    public bool? IncludeCallees { get; set; }

    public int? MaxDepth { get; set; }

    public int? MaxExceptionEdges { get; set; }

    public bool? IncludeTransitiveRoots { get; set; }

    public bool? IncludePurityClassification { get; set; }

    public bool? CompareManualCatalogs { get; set; }

    public bool? IncludeBclFallbackInventory { get; set; }

    public bool? AllRuntimeAssemblies { get; set; }

    public string[]? ExcludedSymbolPrefixes { get; set; }
}

internal sealed class ArtifactSpecEntry
{
    public string? OutputPath { get; set; }

    public string? SourceSummaryPath { get; set; }

    public string? Framework { get; set; }

    public string? RuntimeAssemblyName { get; set; }

    public string[]? AssemblyPaths { get; set; }

    public string? PackageId { get; set; }

    public string? PackageVersion { get; set; }

    public string? PackageAssemblyRelativePath { get; set; }

    public string[]? SymbolPrefixes { get; set; }

    public int? Limit { get; set; }

    public bool? IncludeCallees { get; set; }

    public int? MaxDepth { get; set; }

    public int? MaxExceptionEdges { get; set; }

    public bool? IncludeTransitiveRoots { get; set; }

    public bool? IncludePurityClassification { get; set; }

    public bool? CompareManualCatalogs { get; set; }

    public bool? IncludeBclFallbackInventory { get; set; }

    public bool? AllRuntimeAssemblies { get; set; }

    public string[]? ExcludedSymbolPrefixes { get; set; }
}

internal static class ArtifactSpecSymbolSource
{
    public static ArtifactSpecSymbolSet LoadSymbols(
        string path,
        IReadOnlyList<string>? excludedSymbolPrefixes = null,
        IReadOnlyList<string>? includedSymbolPrefixes = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var exactSymbolKeys = new HashSet<string>(StringComparer.Ordinal);
        var exclusionPrefixes = excludedSymbolPrefixes ?? Array.Empty<string>();
        var inclusionPrefixes = includedSymbolPrefixes ?? Array.Empty<string>();

        if (TryCollectReachableSourceSummaryMethods(document.RootElement, inclusionPrefixes, exclusionPrefixes, symbols, exactSymbolKeys))
        {
            return new ArtifactSpecSymbolSet(
                Symbols: symbols.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray(),
                ExactSymbolKeys: exactSymbolKeys.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray());
        }

        if (document.RootElement.TryGetProperty("GeneratedPurityCatalog", out var generatedPurityCatalog) &&
            generatedPurityCatalog.ValueKind == JsonValueKind.Object &&
            generatedPurityCatalog.TryGetProperty("Entries", out var entriesElement) &&
            entriesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entryElement in entriesElement.EnumerateArray())
            {
                var symbol = GetTrimmedStringProperty(entryElement, "Symbol");
                var included = MatchesIncludedPrefix(symbol, inclusionPrefixes);
                if (!string.IsNullOrWhiteSpace(symbol) &&
                    included &&
                    !ArtifactSpecSymbolFilter.MatchesExcludedPrefix(symbol, exclusionPrefixes))
                {
                    symbols.Add(symbol);
                }

                var exactSymbolKey = GetTrimmedStringProperty(entryElement, "ExactSymbolKey");
                if (included && !string.IsNullOrWhiteSpace(exactSymbolKey))
                {
                    exactSymbolKeys.Add(exactSymbolKey);
                }
            }
        }

        if (symbols.Count == 0 &&
            document.RootElement.TryGetProperty("Assemblies", out var assembliesElement) &&
            assembliesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var assemblyElement in assembliesElement.EnumerateArray())
            {
                if (!assemblyElement.TryGetProperty("Methods", out var methodsElement) ||
                    methodsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var methodElement in methodsElement.EnumerateArray())
                {
                    var symbol = GetTrimmedStringProperty(methodElement, "Symbol");
                    var included = MatchesIncludedPrefix(symbol, inclusionPrefixes);
                    if (!string.IsNullOrWhiteSpace(symbol) &&
                        included &&
                        !ArtifactSpecSymbolFilter.MatchesExcludedPrefix(symbol, exclusionPrefixes))
                    {
                        symbols.Add(symbol);
                    }

                    var exactSymbolKey = GetTrimmedStringProperty(methodElement, "ExactSymbolKey");
                    if (included && !string.IsNullOrWhiteSpace(exactSymbolKey))
                    {
                        exactSymbolKeys.Add(exactSymbolKey);
                    }
                }
            }
        }

        if (symbols.Count == 0 && inclusionPrefixes.Count == 0)
        {
            throw new InvalidOperationException($"Artifact source summary '{path}' did not contain any symbols.");
        }

        return new ArtifactSpecSymbolSet(
            Symbols: symbols.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray(),
            ExactSymbolKeys: exactSymbolKeys.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray());
    }

    private static string? GetTrimmedStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString()?.Trim();
    }

    private static bool MatchesIncludedPrefix(string? symbol, IReadOnlyList<string> includedSymbolPrefixes)
    {
        if (includedSymbolPrefixes.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        return includedSymbolPrefixes.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool TryCollectReachableSourceSummaryMethods(
        JsonElement rootElement,
        IReadOnlyList<string> includedSymbolPrefixes,
        IReadOnlyList<string> excludedSymbolPrefixes,
        HashSet<string> symbols,
        HashSet<string> exactSymbolKeys)
    {
        if (includedSymbolPrefixes.Count == 0 ||
            !rootElement.TryGetProperty("Assemblies", out var assembliesElement) ||
            assembliesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var methodEntries = new List<SourceSummaryMethodEntry>();
        foreach (var assemblyElement in assembliesElement.EnumerateArray())
        {
            if (!assemblyElement.TryGetProperty("Methods", out var methodsElement) ||
                methodsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var methodElement in methodsElement.EnumerateArray())
            {
                var symbol = GetTrimmedStringProperty(methodElement, "Symbol");
                var exactSymbolKey = GetTrimmedStringProperty(methodElement, "ExactSymbolKey");
                if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(exactSymbolKey))
                {
                    continue;
                }

                var calls = methodElement.TryGetProperty("Calls", out var callsElement) && callsElement.ValueKind == JsonValueKind.Array
                    ? callsElement.EnumerateArray()
                        .Select(call => call.ValueKind == JsonValueKind.String ? call.GetString()?.Trim() : null)
                        .Where(call => !string.IsNullOrWhiteSpace(call))
                        .Cast<string>()
                        .ToArray()
                    : Array.Empty<string>();

                methodEntries.Add(new SourceSummaryMethodEntry(symbol, exactSymbolKey, calls));
            }
        }

        if (methodEntries.Count == 0)
        {
            return false;
        }

        var includedMemberTokens = includedSymbolPrefixes
            .Select(TryGetMemberToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (includedMemberTokens.Count == 0)
        {
            return false;
        }

        var byExactSymbolKey = methodEntries
            .GroupBy(entry => entry.ExactSymbolKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new SourceSummaryMethodEntry(
                    group.First().Symbol,
                    group.Key,
                    group.SelectMany(entry => entry.Calls)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()),
                StringComparer.Ordinal);
        var queue = new Queue<SourceSummaryMethodEntry>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in methodEntries.Where(entry =>
                     MatchesIncludedPrefix(entry.Symbol, includedSymbolPrefixes) ||
                     includedMemberTokens.Contains(TryGetMemberToken(entry.Symbol) ?? string.Empty)))
        {
            if (visited.Add(entry.ExactSymbolKey))
            {
                queue.Enqueue(entry);
            }
        }

        if (queue.Count == 0)
        {
            return false;
        }

        while (queue.Count > 0)
        {
            var entry = queue.Dequeue();
            if (!ArtifactSpecSymbolFilter.MatchesExcludedPrefix(entry.Symbol, excludedSymbolPrefixes))
            {
                symbols.Add(entry.Symbol);
                exactSymbolKeys.Add(entry.ExactSymbolKey);
            }

            foreach (var call in entry.Calls)
            {
                if (byExactSymbolKey.TryGetValue(call, out var callee) && visited.Add(callee.ExactSymbolKey))
                {
                    queue.Enqueue(callee);
                }
            }
        }

        return symbols.Count > 0;
    }

    private static string? TryGetMemberToken(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var parameterIndex = symbol.IndexOf('(', StringComparison.Ordinal);
        var beforeParameters = parameterIndex >= 0
            ? symbol.Substring(0, parameterIndex)
            : symbol;
        var lastDot = beforeParameters.LastIndexOf('.');
        if (lastDot < 0 || lastDot == beforeParameters.Length - 1)
        {
            return null;
        }

        var memberName = beforeParameters.Substring(lastDot + 1);
        if (memberName.StartsWith("get_", StringComparison.Ordinal) ||
            memberName.StartsWith("set_", StringComparison.Ordinal))
        {
            memberName = memberName.Substring(4);
        }
        else if (memberName.StartsWith("Get", StringComparison.Ordinal) && memberName.Length > 3)
        {
            memberName = memberName.Substring(3);
        }
        else if (memberName.StartsWith("Set", StringComparison.Ordinal) && memberName.Length > 3)
        {
            memberName = memberName.Substring(3);
        }

        return memberName;
    }
}

internal sealed record ArtifactSpecSymbolSet(
    string[] Symbols,
    string[] ExactSymbolKeys);

internal sealed record SourceSummaryMethodEntry(
    string Symbol,
    string ExactSymbolKey,
    string[] Calls);

internal static class ArtifactSpecSymbolFilter
{
    public static AssemblyEffectReport Exclude(
        AssemblyEffectReport report,
        IReadOnlyList<string> excludedSymbolPrefixes)
    {
        var filteredMethods = report.Methods
            .Where(method => !MatchesExcludedPrefix(method.Symbol, excludedSymbolPrefixes))
            .ToArray();

        return report with
        {
            EmittedMethodCount = filteredMethods.Length,
            Methods = filteredMethods,
        };
    }

    public static bool MatchesExcludedPrefix(string symbol, IReadOnlyList<string> excludedSymbolPrefixes)
    {
        if (string.IsNullOrWhiteSpace(symbol) || excludedSymbolPrefixes.Count == 0)
        {
            return false;
        }

        return excludedSymbolPrefixes.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal));
    }
}

internal static class RuntimeAssemblyResolver
{
    public static string Resolve(string framework, string assemblyName)
    {
        foreach (var assemblyPath in EnumerateCandidateAssemblyPaths(framework, assemblyName))
        {
            if (File.Exists(assemblyPath))
            {
                return assemblyPath;
            }
        }

        throw new FileNotFoundException(
            $"Runtime assembly '{assemblyName}' was not found for {framework}. Checked the current runtime directory, TRUSTED_PLATFORM_ASSEMBLIES, and shared runtime locations.",
            assemblyName);
    }

    public static string[] ResolveSystemRuntimeAssemblies(string framework)
    {
        var coreLibPath = Resolve(framework, "System.Private.CoreLib.dll");
        var runtimeDirectory = Path.GetDirectoryName(coreLibPath)
            ?? throw new DirectoryNotFoundException($"Unable to resolve runtime directory from '{coreLibPath}'.");
        return Directory
            .EnumerateFiles(runtimeDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(IsSystemRuntimeAssemblyFile)
            .Where(HasManagedMetadata)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSystemRuntimeAssemblyFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("System.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasManagedMetadata(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.HasMetadata;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int ParseMajorFrameworkVersion(string framework)
    {
        if (!framework.StartsWith("net", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported framework moniker '{framework}'. Expected netX.Y.");
        }

        var digits = new string(framework.Skip(3).TakeWhile(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            throw new ArgumentException($"Unsupported framework moniker '{framework}'. Expected netX.Y.");
        }

        return int.Parse(digits);
    }

    private static Version? TryParseVersion(string text)
    {
        return Version.TryParse(text, out var version) ? version : null;
    }

    private static IEnumerable<string> EnumerateCandidateAssemblyPaths(string framework, string assemblyName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCurrentRuntimeCandidates(assemblyName))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        foreach (var candidate in EnumerateTrustedPlatformAssemblyCandidates(assemblyName))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        foreach (var candidate in EnumerateSharedRuntimeCandidates(framework, assemblyName))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateCurrentRuntimeCandidates(string assemblyName)
    {
        var directories = new[]
        {
            Path.GetDirectoryName(typeof(object).Assembly.Location),
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            AppContext.BaseDirectory,
        };

        foreach (var directory in directories)
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                yield return Path.Combine(directory, assemblyName);
            }
        }
    }

    private static IEnumerable<string> EnumerateTrustedPlatformAssemblyCandidates(string assemblyName)
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            yield break;
        }

        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            if (string.Equals(Path.GetFileName(path), assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateSharedRuntimeCandidates(string framework, string assemblyName)
    {
        var major = ParseMajorFrameworkVersion(framework);
        foreach (var runtimeRoot in EnumerateSharedRuntimeRoots())
        {
            if (!Directory.Exists(runtimeRoot))
            {
                continue;
            }

            var versionDirectory = Directory
                .EnumerateDirectories(runtimeRoot)
                .Select(path => (Path: path, Version: TryParseVersion(Path.GetFileName(path))))
                .Where(item => item.Version is not null && item.Version.Major == major)
                .OrderByDescending(item => item.Version)
                .Select(item => item.Path)
                .FirstOrDefault();
            if (versionDirectory is not null)
            {
                yield return Path.Combine(versionDirectory, assemblyName);
            }
        }
    }

    private static IEnumerable<string> EnumerateSharedRuntimeRoots()
    {
        var sharedDirectories = new[]
        {
            CombineIfRooted(Environment.GetEnvironmentVariable("DOTNET_ROOT"), "shared"),
            CombineIfRooted(Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"), "shared"),
            CombineIfRooted(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared"),
            CombineIfRooted(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "shared"),
            Path.Combine(Path.DirectorySeparatorChar.ToString(), "usr", "share", "dotnet", "shared"),
            Path.Combine(Path.DirectorySeparatorChar.ToString(), "usr", "local", "share", "dotnet", "shared"),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sharedDirectory in sharedDirectories)
        {
            if (string.IsNullOrWhiteSpace(sharedDirectory) || !Directory.Exists(sharedDirectory))
            {
                continue;
            }

            foreach (var runtimeRoot in Directory.EnumerateDirectories(sharedDirectory))
            {
                if (seen.Add(runtimeRoot))
                {
                    yield return runtimeRoot;
                }
            }
        }
    }

    private static string? CombineIfRooted(string? root, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var combined = root;
        foreach (var segment in segments)
        {
            combined = Path.Combine(combined, segment);
        }

        return combined;
    }
}

internal static class AssemblyEffectSummarizer
{
    private static readonly ConcurrentDictionary<string, Type?> RuntimeTypeCache =
        new ConcurrentDictionary<string, Type?>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<int, StaticFieldFact> EmptyStaticFieldFacts =
        new Dictionary<int, StaticFieldFact>();

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

    public static AssemblyEffectReport Summarize(
        string assemblyPath,
        int? limit,
        IReadOnlyList<string> symbolPrefixes,
        IReadOnlyList<string> exactSymbols,
        IReadOnlyList<string> exactSymbolKeys,
        bool includeCallees,
        int maxDepth,
        bool includeTransitiveRoots,
        int maxExceptionEdges)
    {
        var assemblySha256 = ComputeFileSha256(assemblyPath);
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            throw new InvalidOperationException($"Assembly does not contain managed metadata: {assemblyPath}");
        }

        var reader = peReader.GetMetadataReader();
        var module = reader.GetModuleDefinition();
        var assemblyName = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : Path.GetFileNameWithoutExtension(assemblyPath);
        var moduleVersionId = reader.GetGuid(module.Mvid).ToString("D");

        var methodDefinitionHandlesByExactKey = new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
        var fieldDefinitionHandlesBySymbol = new Dictionary<string, FieldDefinitionHandle>(StringComparer.Ordinal);
        var fieldDefinitionHandlesByExactKey = new Dictionary<string, FieldDefinitionHandle>(StringComparer.Ordinal);
        var knownMethodReturnValues = new Dictionary<int, TrackedStackValue>();
        var knownMethodReturnValueVisiting = new HashSet<int>();
        var allSummaries = new List<MethodEffectSummary>();
        foreach (var handle in reader.FieldDefinitions)
        {
            fieldDefinitionHandlesBySymbol[GetFieldDefinitionSymbol(reader, handle)] = handle;
            fieldDefinitionHandlesByExactKey[GetFieldExactKey(reader, handle)] = handle;
        }

        foreach (var handle in reader.MethodDefinitions)
        {
            methodDefinitionHandlesByExactKey[GetMethodExactKey(reader, handle)] = handle;
        }

        var handlesToSummarize = GetMethodHandlesToSummarize(
            peReader,
            reader,
            methodDefinitionHandlesByExactKey,
            symbolPrefixes,
            exactSymbols,
            exactSymbolKeys,
            includeCallees,
            includeTransitiveRoots);
        if (handlesToSummarize is { Count: 0 })
        {
            return new AssemblyEffectReport(
                AssemblyName: assemblyName,
                AssemblyPath: assemblyPath,
                AssemblySha256: assemblySha256,
                ModuleVersionId: moduleVersionId,
                MethodCount: reader.MethodDefinitions.Count,
                EmittedMethodCount: 0,
                Methods: Array.Empty<MethodEffectSummary>())
            {
                ClassificationMethods = Array.Empty<MethodEffectSummary>()
            };
        }

        var staticFieldFacts = BuildStaticFieldFacts(
            peReader,
            reader,
            methodDefinitionHandlesByExactKey,
            fieldDefinitionHandlesBySymbol,
            fieldDefinitionHandlesByExactKey,
            knownMethodReturnValues,
            knownMethodReturnValueVisiting);
        foreach (var handle in reader.MethodDefinitions)
        {
            if (handlesToSummarize is not null && !handlesToSummarize.Contains(handle))
            {
                continue;
            }

            allSummaries.Add(SummarizeMethod(
                peReader,
                reader,
                handle,
                moduleVersionId,
                methodDefinitionHandlesByExactKey,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                staticFieldFacts,
                knownMethodReturnValues,
                knownMethodReturnValueVisiting));
        }

        if (includeTransitiveRoots)
        {
            allSummaries = AddTransitiveRootCandidates(allSummaries, maxExceptionEdges);
        }

        var summaries = SelectSummaries(
            allSummaries,
            symbolPrefixes,
            exactSymbols,
            exactSymbolKeys,
            includeCallees,
            maxDepth,
            limit);

        return new AssemblyEffectReport(
            AssemblyName: assemblyName,
            AssemblyPath: assemblyPath,
            AssemblySha256: assemblySha256,
            ModuleVersionId: moduleVersionId,
            MethodCount: reader.MethodDefinitions.Count,
            EmittedMethodCount: summaries.Length,
            Methods: summaries)
        {
            ClassificationMethods = allSummaries.ToArray()
        };
    }

    private static HashSet<MethodDefinitionHandle>? GetMethodHandlesToSummarize(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyList<string> symbolPrefixes,
        IReadOnlyList<string> exactSymbols,
        IReadOnlyList<string> exactSymbolKeys,
        bool includeCallees,
        bool includeTransitiveRoots)
    {
        if (symbolPrefixes.Count == 0 && exactSymbols.Count == 0 && exactSymbolKeys.Count == 0)
        {
            return null;
        }

        var rootHandles = GetRootMethodHandles(reader, symbolPrefixes, exactSymbols, exactSymbolKeys);
        if (!includeCallees && !includeTransitiveRoots)
        {
            return rootHandles;
        }

        return CollectReachableMethodHandles(
            peReader,
            reader,
            methodDefinitionHandlesByExactKey,
            rootHandles);
    }

    private static HashSet<MethodDefinitionHandle> GetRootMethodHandles(
        MetadataReader reader,
        IReadOnlyList<string> symbolPrefixes,
        IReadOnlyList<string> exactSymbols,
        IReadOnlyList<string> exactSymbolKeys)
    {
        var exactSymbolSet = exactSymbols.Count == 0
            ? null
            : new HashSet<string>(exactSymbols, StringComparer.Ordinal);
        var exactSymbolKeySet = exactSymbolKeys.Count == 0
            ? null
            : new HashSet<string>(exactSymbolKeys, StringComparer.Ordinal);
        var rootHandles = new HashSet<MethodDefinitionHandle>();
        foreach (var handle in reader.MethodDefinitions)
        {
            var symbol = GetMethodDisplaySymbol(reader, handle);
            if (MatchesSymbolPrefix(symbol, symbolPrefixes))
            {
                rootHandles.Add(handle);
                continue;
            }

            if (exactSymbolSet != null && exactSymbolSet.Contains(symbol))
            {
                rootHandles.Add(handle);
                continue;
            }

            if (exactSymbolKeySet != null && exactSymbolKeySet.Contains(GetMethodExactKey(reader, handle)))
            {
                rootHandles.Add(handle);
            }
        }

        return rootHandles;
    }

    private static HashSet<MethodDefinitionHandle> CollectReachableMethodHandles(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyCollection<MethodDefinitionHandle> rootHandles)
    {
        var included = new HashSet<MethodDefinitionHandle>();
        if (rootHandles.Count == 0)
        {
            return included;
        }

        var queue = new Queue<MethodDefinitionHandle>(rootHandles);
        var calleeCache = new Dictionary<MethodDefinitionHandle, MethodDefinitionHandle[]>();
        while (queue.Count > 0)
        {
            var handle = queue.Dequeue();
            if (!included.Add(handle))
            {
                continue;
            }

            foreach (var calleeHandle in GetSameAssemblyCallees(
                         peReader,
                         reader,
                         handle,
                         methodDefinitionHandlesByExactKey,
                         calleeCache))
            {
                if (!included.Contains(calleeHandle))
                {
                    queue.Enqueue(calleeHandle);
                }
            }
        }

        return included;
    }

    private static MethodDefinitionHandle[] GetSameAssemblyCallees(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle handle,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        Dictionary<MethodDefinitionHandle, MethodDefinitionHandle[]> calleeCache)
    {
        if (calleeCache.TryGetValue(handle, out var cached))
        {
            return cached;
        }

        var definition = reader.GetMethodDefinition(handle);
        if (definition.RelativeVirtualAddress == 0)
        {
            return calleeCache[handle] = Array.Empty<MethodDefinitionHandle>();
        }

        var body = peReader.GetMethodBody(definition.RelativeVirtualAddress);
        var il = body.GetILBytes();
        if (il is null || il.Length == 0)
        {
            return calleeCache[handle] = Array.Empty<MethodDefinitionHandle>();
        }

        var callees = new HashSet<MethodDefinitionHandle>();
        var offset = 0;
        while (offset < il.Length)
        {
            var opCode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            var operandSize = GetOperandSize(opCode.OperandType, il, operandOffset);
            var operandToken = operandSize == 4 && IsMetadataTokenOperand(opCode.OperandType)
                ? BitConverter.ToInt32(il, operandOffset)
                : (int?)null;
            offset += operandSize;

            if (operandToken is null ||
                (opCode != OpCodes.Call &&
                 opCode != OpCodes.Callvirt &&
                 opCode != OpCodes.Newobj &&
                 opCode != OpCodes.Ldftn &&
                 opCode != OpCodes.Ldvirtftn))
            {
                continue;
            }

            if (TryResolveSameAssemblyMethodDefinitionHandle(
                    reader,
                    operandToken.Value,
                    methodDefinitionHandlesByExactKey,
                    out var calleeHandle))
            {
                callees.Add(calleeHandle);
            }
        }

        cached = callees.ToArray();
        calleeCache[handle] = cached;
        return cached;
    }

    private static bool MatchesSymbolPrefix(string symbol, IReadOnlyList<string> symbolPrefixes)
    {
        return symbolPrefixes.Count == 0 ||
            symbolPrefixes.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static MethodEffectSummary[] SelectSummaries(
        IReadOnlyList<MethodEffectSummary> allSummaries,
        IReadOnlyList<string> symbolPrefixes,
        IReadOnlyList<string> exactSymbols,
        IReadOnlyList<string> exactSymbolKeys,
        bool includeCallees,
        int maxDepth,
        int? limit)
    {
        var hasPrefixRoots = symbolPrefixes.Count > 0;
        var hasExactRoots = exactSymbols.Count > 0 || exactSymbolKeys.Count > 0;

        IEnumerable<MethodEffectSummary> selected = Array.Empty<MethodEffectSummary>();
        if (hasPrefixRoots)
        {
            selected = !includeCallees
                ? allSummaries.Where(summary => MatchesSymbolPrefix(summary.Symbol, symbolPrefixes))
                : SelectWithCallees(allSummaries, symbolPrefixes, maxDepth);
        }
        else if (!hasExactRoots)
        {
            selected = allSummaries;
        }

        if (hasExactRoots)
        {
            selected = UnionByExactSymbolKey(
                selected,
                SelectExactSummaries(allSummaries, exactSymbols, exactSymbolKeys));
        }

        if (limit is not null)
        {
            selected = selected.Take(limit.Value);
        }

        return selected.ToArray();
    }

    private static IEnumerable<MethodEffectSummary> SelectExactSummaries(
        IReadOnlyList<MethodEffectSummary> allSummaries,
        IReadOnlyList<string> exactSymbols,
        IReadOnlyList<string> exactSymbolKeys)
    {
        var exactSymbolSet = exactSymbols.Count == 0
            ? null
            : new HashSet<string>(exactSymbols, StringComparer.Ordinal);
        var exactSymbolKeySet = exactSymbolKeys.Count == 0
            ? null
            : new HashSet<string>(exactSymbolKeys, StringComparer.Ordinal);

        return allSummaries.Where(summary =>
            (exactSymbolSet != null && exactSymbolSet.Contains(summary.Symbol)) ||
            (exactSymbolKeySet != null && exactSymbolKeySet.Contains(summary.ExactSymbolKey)));
    }

    private static IEnumerable<MethodEffectSummary> UnionByExactSymbolKey(
        IEnumerable<MethodEffectSummary> first,
        IEnumerable<MethodEffectSummary> second)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var summary in first)
        {
            if (seen.Add(summary.ExactSymbolKey))
            {
                yield return summary;
            }
        }

        foreach (var summary in second)
        {
            if (seen.Add(summary.ExactSymbolKey))
            {
                yield return summary;
            }
        }
    }

    private static IEnumerable<MethodEffectSummary> SelectWithCallees(
        IReadOnlyList<MethodEffectSummary> allSummaries,
        IReadOnlyList<string> symbolPrefixes,
        int maxDepth)
    {
        var bySymbol = allSummaries
            .GroupBy(summary => summary.ExactSymbolKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var included = new HashSet<string>(StringComparer.Ordinal);
        var orderedExactSymbolKeys = new List<string>();
        var queue = new Queue<(string ExactSymbolKey, int Depth)>();
        foreach (var summary in allSummaries.Where(summary => MatchesSymbolPrefix(summary.Symbol, symbolPrefixes)))
        {
            if (included.Add(summary.ExactSymbolKey))
            {
                orderedExactSymbolKeys.Add(summary.ExactSymbolKey);
                queue.Enqueue((summary.ExactSymbolKey, 0));
            }
        }

        while (queue.Count > 0)
        {
            var (exactSymbolKey, depth) = queue.Dequeue();
            if ((maxDepth >= 0 && depth >= maxDepth) ||
                !TryResolveSummaryExactSymbolKey(exactSymbolKey, bySymbol, out _, out var summary))
            {
                continue;
            }

            foreach (var call in summary.Calls)
            {
                if (TryResolveSummaryExactSymbolKey(call, bySymbol, out var resolvedCallKey, out _) &&
                    included.Add(resolvedCallKey))
                {
                    orderedExactSymbolKeys.Add(resolvedCallKey);
                    queue.Enqueue((resolvedCallKey, depth + 1));
                }
            }
        }

        return orderedExactSymbolKeys.Select(exactSymbolKey => bySymbol[exactSymbolKey]);
    }

    private static MethodEffectSummary SummarizeMethod(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle handle,
        string moduleVersionId,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        var definition = reader.GetMethodDefinition(handle);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var calls = new SortedSet<string>(StringComparer.Ordinal);
        var fields = new SortedSet<string>(StringComparer.Ordinal);
        var staticFields = new SortedSet<string>(StringComparer.Ordinal);
        var sameAssemblyStaticReadFieldTokens = new SortedSet<int>();
        var thrownExceptionTypes = new SortedSet<string>(StringComparer.Ordinal);
        var callSites = new List<CallSiteSummary>();
        var exceptionPropagationSites = new List<ExceptionPropagationSite>();
        string? methodBodySha256 = null;

        if ((definition.Attributes & MethodAttributes.Abstract) != 0)
        {
            effects.Add("abstract");
        }

        if ((definition.Attributes & MethodAttributes.PinvokeImpl) != 0)
        {
            effects.Add("pinvoke");
        }

        if ((definition.ImplAttributes & MethodImplAttributes.InternalCall) != 0 ||
            (definition.ImplAttributes & MethodImplAttributes.Native) != 0)
        {
            effects.Add("native_or_internal_call");
        }

        if (definition.RelativeVirtualAddress == 0)
        {
            effects.Add("no_il_body");
        }
        else
        {
            var body = peReader.GetMethodBody(definition.RelativeVirtualAddress);
            var il = body.GetILBytes();
            if (il is not null)
            {
                methodBodySha256 = ComputeSha256(il);
                AnalyzeIl(
                    peReader,
                    reader,
                    il,
                    body.ExceptionRegions,
                    effects,
                    calls,
                    callSites,
                    fields,
                    staticFields,
                    sameAssemblyStaticReadFieldTokens,
                    thrownExceptionTypes,
                    exceptionPropagationSites,
                    methodDefinitionHandlesByExactKey,
                    fieldDefinitionHandlesBySymbol,
                    fieldDefinitionHandlesByExactKey,
                    staticFieldFacts,
                    knownMethodReturnValues,
                    knownMethodReturnValueVisiting);
            }
        }

        var metadataToken = $"0x{MetadataTokens.GetToken(handle):X8}";
        var cacheKey = $"mvid:{moduleVersionId}|token:{metadataToken}|il:{methodBodySha256 ?? "no-il"}";
        var isConstructor = string.Equals(reader.GetString(definition.Name), ".ctor", StringComparison.Ordinal);
        var symbol = GetMethodDisplaySymbol(reader, handle);
        var directThrownExceptionSources = thrownExceptionTypes
            .Select(exceptionType => new ExceptionSourcePath(exceptionType, symbol))
            .ToArray();
        return new MethodEffectSummary(
            Symbol: symbol,
            ExactSymbolKey: GetMethodExactKey(reader, handle),
            MetadataToken: metadataToken,
            RelativeVirtualAddress: definition.RelativeVirtualAddress,
            MethodBodySha256: methodBodySha256,
            CacheKey: cacheKey,
            Effects: effects.ToArray(),
            RootCandidates: GetRootCandidates(
                    effects,
                    calls,
                    fields,
                    staticFields,
                    sameAssemblyStaticReadFieldTokens,
                    staticFieldFacts,
                    isConstructor)
                .ToArray(),
            TransitiveRootCandidates: Array.Empty<string>(),
            ThrownExceptionTypes: thrownExceptionTypes.ToArray(),
            TransitiveThrownExceptionTypes: Array.Empty<string>(),
            ThrownExceptionSourcePaths: directThrownExceptionSources,
            TransitiveThrownExceptionSourcePaths: Array.Empty<ExceptionSourcePath>(),
            Calls: calls.ToArray(),
            Fields: fields.ToArray())
        {
            IsStatic = (definition.Attributes & MethodAttributes.Static) != 0,
            CallSites = callSites
                .GroupBy(GetCallSiteDeduplicationKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(site => site.ExactSymbolKey, StringComparer.Ordinal)
                .ThenBy(GetCallSiteDeduplicationKey, StringComparer.Ordinal)
                .ToArray()
            ,
            ExceptionPropagationSites = exceptionPropagationSites
                .Distinct()
                .OrderBy(site => site.ExactSymbolKey, StringComparer.Ordinal)
                .ThenBy(site => site.InstructionOffset)
                .ToArray()
        };
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static List<MethodEffectSummary> AddTransitiveRootCandidates(
        IReadOnlyList<MethodEffectSummary> summaries,
        int maxExceptionEdges)
    {
        var bySymbol = summaries
            .GroupBy(summary => summary.ExactSymbolKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var rootMemo = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var rootVisiting = new HashSet<string>(StringComparer.Ordinal);
        var exceptionMemo = new Dictionary<string, ThrownExceptionTraversalResult>(StringComparer.Ordinal);
        var exceptionVisiting = new HashSet<string>(StringComparer.Ordinal);

        return summaries
            .Select(summary =>
            {
                var transitiveExceptionResult = VisitThrownExceptionEdges(
                    summary.ExactSymbolKey,
                    bySymbol,
                    exceptionMemo,
                    exceptionVisiting,
                    maxExceptionEdges);
                var transitiveExceptionEdges = transitiveExceptionResult.Result;
                var transitiveExceptionSources = OrderExceptionSourcePaths(
                    transitiveExceptionEdges
                        .Select(edge => new ExceptionSourcePath(edge.ExceptionType, edge.SourcePath))
                        .DistinctBy(CreateExceptionSourcePathKey));
                return summary with
                {
                    TransitiveRootCandidates = VisitRootCandidates(summary.ExactSymbolKey, bySymbol, rootMemo, rootVisiting).Result,
                    TransitiveThrownExceptionTypes = transitiveExceptionSources
                        .Select(source => source.ExceptionType)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(type => type, StringComparer.Ordinal)
                        .ToArray(),
                    TransitiveThrownExceptionSourcePaths = transitiveExceptionSources,
                    TransitiveThrownExceptionEdges = transitiveExceptionEdges,
                    TransitiveThrownExceptionEdgesTruncated = transitiveExceptionResult.IsTruncated,
                };
            })
            .ToList();
    }

    private static (string[] Result, bool DependsOnCycle) VisitRootCandidates(
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        Dictionary<string, string[]> memo,
        HashSet<string> visiting)
    {
        if (memo.TryGetValue(symbol, out var cached))
        {
            return (cached, false);
        }

        if (!TryResolveSummaryExactSymbolKey(symbol, bySymbol, out _, out var summary))
        {
            return (Array.Empty<string>(), false);
        }

        var roots = new SortedSet<string>(summary.RootCandidates, StringComparer.Ordinal);
        if (!visiting.Add(symbol))
        {
            return (roots.ToArray(), true);
        }

        var dependsOnCycle = false;
        foreach (var call in summary.Calls)
        {
            if (TryResolveSummaryExactSymbolKey(call, bySymbol, out var resolvedCallKey, out _))
            {
                var nestedResult = VisitRootCandidates(resolvedCallKey, bySymbol, memo, visiting);
                roots.UnionWith(nestedResult.Result);
                dependsOnCycle |= nestedResult.DependsOnCycle;
            }
        }

        visiting.Remove(symbol);
        var result = roots.ToArray();
        if (!dependsOnCycle)
        {
            memo[symbol] = result;
        }

        return (result, dependsOnCycle);
    }

    private static ThrownExceptionTraversalResult VisitThrownExceptionEdges(
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        Dictionary<string, ThrownExceptionTraversalResult> memo,
        HashSet<string> visiting,
        int maxExceptionEdges)
    {
        if (memo.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        if (!TryResolveSummaryExactSymbolKey(symbol, bySymbol, out _, out var summary))
        {
            return new ThrownExceptionTraversalResult(
                Array.Empty<ThrownExceptionEdgeSummary>(),
                DependsOnCycle: false,
                IsTruncated: false);
        }

        var thrownSources = new Dictionary<string, ThrownExceptionEdgeSummary>(StringComparer.Ordinal);
        var isTruncated = false;
        foreach (var directSource in summary.ThrownExceptionSourcePaths)
        {
            var directEdge = new ThrownExceptionEdgeSummary(
                directSource.ExceptionType,
                directSource.SourcePath,
                CalleeExactSymbolKey: null,
                Depth: 0);
            TryAddThrownExceptionEdge(
                thrownSources,
                directEdge,
                maxExceptionEdges,
                ref isTruncated);
        }

        if (!visiting.Add(symbol))
        {
            return new ThrownExceptionTraversalResult(
                OrderThrownExceptionEdges(thrownSources.Values),
                DependsOnCycle: true,
                IsTruncated: isTruncated);
        }

        var dependsOnCycle = false;
        foreach (var propagationSite in summary.ExceptionPropagationSites)
        {
            if (TryResolveSummaryExactSymbolKey(propagationSite.ExactSymbolKey, bySymbol, out var resolvedPropagationKey, out _))
            {
                var nestedResult = VisitThrownExceptionEdges(
                    resolvedPropagationKey,
                    bySymbol,
                    memo,
                    visiting,
                    maxExceptionEdges);
                dependsOnCycle |= nestedResult.DependsOnCycle;
                isTruncated |= nestedResult.IsTruncated;
                foreach (var nestedSource in nestedResult.Result)
                {
                    if (!ExceptionEscapesPropagationSite(propagationSite, nestedSource.ExceptionType))
                    {
                        continue;
                    }

                    var chainedSourcePath = summary.Symbol + " -> " + nestedSource.SourcePath;
                    if (!string.IsNullOrWhiteSpace(nestedSource.CalleeExactSymbolKey))
                    {
                        var inheritedEdge = new ThrownExceptionEdgeSummary(
                            nestedSource.ExceptionType,
                            chainedSourcePath,
                            nestedSource.CalleeExactSymbolKey,
                            nestedSource.Depth + 1);
                        TryAddThrownExceptionEdge(
                            thrownSources,
                            inheritedEdge,
                            maxExceptionEdges,
                            ref isTruncated);
                    }
                    else
                    {
                        var immediateCalleeEdge = new ThrownExceptionEdgeSummary(
                            nestedSource.ExceptionType,
                            chainedSourcePath,
                            CalleeExactSymbolKey: propagationSite.ExactSymbolKey,
                            Depth: 1);
                        TryAddThrownExceptionEdge(
                            thrownSources,
                            immediateCalleeEdge,
                            maxExceptionEdges,
                            ref isTruncated);
                    }
                }
            }
        }

        visiting.Remove(symbol);
        var result = OrderThrownExceptionEdges(thrownSources.Values);
        var traversalResult = new ThrownExceptionTraversalResult(result, dependsOnCycle, isTruncated);
        if (!dependsOnCycle)
        {
            memo[symbol] = traversalResult;
        }

        return traversalResult;
    }

    private static void TryAddThrownExceptionEdge(
        Dictionary<string, ThrownExceptionEdgeSummary> thrownSources,
        ThrownExceptionEdgeSummary edge,
        int maxExceptionEdges,
        ref bool isTruncated)
    {
        var key = CreateThrownExceptionEdgeKey(edge);
        if (thrownSources.ContainsKey(key))
        {
            return;
        }

        if (thrownSources.Count >= maxExceptionEdges)
        {
            isTruncated = true;
            return;
        }

        thrownSources[key] = edge;
    }

    private static string CreateExceptionSourcePathKey(ExceptionSourcePath sourcePath)
    {
        return sourcePath.ExceptionType + "|" + sourcePath.SourcePath;
    }

    private static ExceptionSourcePath[] OrderExceptionSourcePaths(IEnumerable<ExceptionSourcePath> sourcePaths)
    {
        return sourcePaths
            .OrderBy(sourcePath => sourcePath.ExceptionType, StringComparer.Ordinal)
            .ThenBy(sourcePath => sourcePath.SourcePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryResolveSummaryExactSymbolKey(
        string exactSymbolKey,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        out string resolvedKey,
        out MethodEffectSummary summary)
    {
        if (bySymbol.TryGetValue(exactSymbolKey, out var directSummary) &&
            directSummary is not null)
        {
            resolvedKey = exactSymbolKey;
            summary = directSummary;
            return true;
        }

        var normalizedKey = EffectSummaryExactSymbolKeyNormalizer.NormalizeConstructedReceiverType(exactSymbolKey);
        if (!string.Equals(normalizedKey, exactSymbolKey, StringComparison.Ordinal) &&
            bySymbol.TryGetValue(normalizedKey, out var normalizedSummary) &&
            normalizedSummary is not null)
        {
            resolvedKey = normalizedKey;
            summary = normalizedSummary;
            return true;
        }

        resolvedKey = string.Empty;
        summary = default!;
        return false;
    }

    private static string CreateThrownExceptionEdgeKey(ThrownExceptionEdgeSummary edge)
    {
        return edge.ExceptionType + "|" +
               edge.SourcePath + "|" +
               (edge.CalleeExactSymbolKey ?? string.Empty) + "|" +
               edge.Depth.ToString();
    }

    private static ThrownExceptionEdgeSummary[] OrderThrownExceptionEdges(IEnumerable<ThrownExceptionEdgeSummary> edges)
    {
        return edges
            .OrderBy(edge => edge.ExceptionType, StringComparer.Ordinal)
            .ThenBy(edge => edge.SourcePath, StringComparer.Ordinal)
            .ThenBy(edge => edge.CalleeExactSymbolKey, StringComparer.Ordinal)
            .ThenBy(edge => edge.Depth)
            .ToArray();
    }

    private static IEnumerable<string> GetRootCandidates(
        IEnumerable<string> effects,
        IEnumerable<string> calls,
        IEnumerable<string> fields,
        IEnumerable<string> staticReadFields,
        IReadOnlySet<int> sameAssemblyStaticReadFieldTokens,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        bool isConstructor)
    {
        var roots = new SortedSet<string>(StringComparer.Ordinal);
        var effectSet = new HashSet<string>(effects, StringComparer.Ordinal);
        var callSet = new HashSet<string>(calls, StringComparer.Ordinal);
        var fieldSet = new HashSet<string>(fields, StringComparer.Ordinal);
        var staticReadFieldSet = new HashSet<string>(staticReadFields, StringComparer.Ordinal);
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case "pinvoke":
                    roots.Add("pinvoke");
                    break;
                case "native_or_internal_call":
                    roots.Add("runtime_native_or_internal");
                    break;
                case "no_il_body":
                    roots.Add("metadata_only_or_external");
                    break;
                case "reads_static_field":
                    if (IsSafeStaticConstantRead(staticReadFieldSet, sameAssemblyStaticReadFieldTokens, staticFieldFacts))
                    {
                        roots.Add("safe_static_constant_read");
                    }
                    else if (IsSafeStaticCacheRead(staticReadFieldSet, callSet, sameAssemblyStaticReadFieldTokens, staticFieldFacts))
                    {
                        roots.Add("safe_static_cache_read");
                    }
                    else
                    {
                        roots.Add("global_state_read");
                    }
                    break;
                case "reads_instance_field":
                    if (IsThreadingRuntimeStateRead(fieldSet))
                    {
                        roots.Add("global_state_read");
                    }
                    break;
                case "writes_static_field":
                    roots.Add("global_state_write");
                    break;
                case "writes_instance_field":
                    roots.Add(IsFreshOwnedObjectWrite(effectSet, callSet, isConstructor)
                        ? "fresh_owned_object_write"
                        : "object_state_write");
                    break;
                case "writes_indirect_memory":
                    roots.Add(IsFreshOwnedMemoryWrite(effectSet, callSet)
                        ? "fresh_owned_memory_write"
                        : "caller_visible_memory_write");
                    break;
                case "indirect_call":
                case "virtual_call":
                    roots.Add("dynamic_dispatch");
                    break;
                case "throws":
                    roots.Add("throw");
                    break;
                case "block_memory_write":
                    roots.Add("unsafe_or_block_memory_write");
                    break;
            }
        }

        return roots;
    }

    private static bool IsThreadingRuntimeStateRead(IReadOnlySet<string> fields)
    {
        foreach (var field in fields)
        {
            if (!(field.StartsWith("System.Threading.", StringComparison.Ordinal) ||
                  field.StartsWith("System.Threading.Tasks.", StringComparison.Ordinal)))
            {
                continue;
            }

            if (field.EndsWith("._state", StringComparison.Ordinal) ||
                field.EndsWith(".m_stateFlags", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSafeStaticCacheRead(
        IReadOnlySet<string> fields,
        IReadOnlySet<string> calls,
        IReadOnlySet<int> sameAssemblyStaticReadFieldTokens,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts)
    {
        if (fields.Count > 0 &&
            HasOnlySameAssemblyFieldFacts(
                fields,
                sameAssemblyStaticReadFieldTokens,
                staticFieldFacts,
                static kind => kind is StaticFieldFactKind.Constant or StaticFieldFactKind.StableIdentity,
                IsKnownExternalSafeStaticCacheField))
        {
            return true;
        }

        return calls.Count == 1 && calls.Any(static call =>
            call.StartsWith("System.ReadOnlySpan`1<byte>..ctor(void*, int)", StringComparison.Ordinal));
    }

    private static bool IsKnownExternalSafeStaticCacheField(string field)
    {
        if (
            field.StartsWith("System.Array+EmptyArray`1", StringComparison.Ordinal) &&
            field.EndsWith(".Value", StringComparison.Ordinal))
        {
            return true;
        }

        if (
            string.Equals(field, "System.Globalization.CultureInfo.s_InvariantCultureInfo", StringComparison.Ordinal) ||
            string.Equals(field, "System.String.Empty", StringComparison.Ordinal) ||
            string.Equals(field, "System.Text.ASCIIEncoding.s_default", StringComparison.Ordinal) ||
            string.Equals(field, "System.UriHelper.Unreserved", StringComparison.Ordinal) ||
            string.Equals(field, "System.Globalization.TextInfo.Invariant", StringComparison.Ordinal) ||
            string.Equals(field, "System.Globalization.CompareInfo.Invariant", StringComparison.Ordinal) ||
            string.Equals(field, "System.Net.IPAddress.IPv6Loopback", StringComparison.Ordinal) ||
            string.Equals(field, "System.Net.IPAddress.Loopback", StringComparison.Ordinal) ||
            string.Equals(field, "System.Net.IPAddress.s_loopbackMappedToIPv6", StringComparison.Ordinal) ||
            field.StartsWith("System.Linq.EmptyPartition`1", StringComparison.Ordinal) &&
            field.EndsWith(".Instance", StringComparison.Ordinal))
        {
            return true;
        }

        if (
            (field.StartsWith("System.Collections.Generic.Comparer`1", StringComparison.Ordinal) ||
             field.StartsWith("System.Collections.Generic.EqualityComparer`1", StringComparison.Ordinal)) &&
            field.EndsWith(".<Default>k__BackingField", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool IsSafeStaticConstantRead(
        IReadOnlySet<string> fields,
        IReadOnlySet<int> sameAssemblyStaticReadFieldTokens,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts)
    {
        return fields.Count > 0 &&
            HasOnlySameAssemblyFieldFacts(
                fields,
                sameAssemblyStaticReadFieldTokens,
                staticFieldFacts,
                static kind => kind == StaticFieldFactKind.Constant,
                static field =>
                    string.Equals(field, "IsLittleEndian", StringComparison.Ordinal) ||
                    string.Equals(field, "System.BitConverter.IsLittleEndian", StringComparison.Ordinal));
    }

    private static bool HasOnlySameAssemblyFieldFacts(
        IReadOnlySet<string> fields,
        IReadOnlySet<int> sameAssemblyStaticReadFieldTokens,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Func<StaticFieldFactKind, bool> sameAssemblyFieldPredicate,
        Func<string, bool> externalFieldPredicate)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        var sameAssemblyFieldSymbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fieldToken in sameAssemblyStaticReadFieldTokens)
        {
            if (!staticFieldFacts.TryGetValue(fieldToken, out var fact))
            {
                return false;
            }

            if (!sameAssemblyFieldPredicate(fact.Kind) &&
                !externalFieldPredicate(fact.Symbol))
            {
                return false;
            }

            sameAssemblyFieldSymbols.Add(fact.Symbol);
        }

        foreach (var field in fields)
        {
            if (sameAssemblyFieldSymbols.Contains(field))
            {
                continue;
            }

            if (!externalFieldPredicate(field))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFreshOwnedMemoryWrite(
        IReadOnlySet<string> effects,
        IReadOnlySet<string> calls)
    {
        if (!effects.Contains("writes_indirect_memory") || !effects.Contains("allocates_array"))
        {
            return false;
        }

        if (effects.Contains("writes_static_field") ||
            effects.Contains("writes_instance_field") ||
            effects.Contains("reads_static_field") ||
            effects.Contains("reads_instance_field") ||
            effects.Contains("indirect_call") ||
            effects.Contains("virtual_call") ||
            effects.Contains("block_memory_write"))
        {
            return false;
        }

        return calls.All(PurityClassificationEngine.IsPurityNeutralIntrinsicHelperCall);
    }

    private static bool IsFreshOwnedObjectWrite(
        IReadOnlySet<string> effects,
        IReadOnlySet<string> calls,
        bool isConstructor)
    {
        if (!effects.Contains("writes_instance_field"))
        {
            return false;
        }

        if (!isConstructor && !effects.Contains("allocates_object"))
        {
            return false;
        }

        if (effects.Contains("writes_static_field") ||
            effects.Contains("reads_static_field") ||
            effects.Contains("reads_instance_field") ||
            effects.Contains("writes_indirect_memory") ||
            effects.Contains("indirect_call") ||
            effects.Contains("virtual_call") ||
            effects.Contains("block_memory_write"))
        {
            return false;
        }

        return calls.All(IsFreshObjectInitializationHelperCall);
    }

    private static bool IsFreshObjectInitializationHelperCall(string callSymbol)
    {
        return PurityClassificationEngine.IsPurityNeutralIntrinsicHelperCall(callSymbol) ||
            callSymbol.Contains(".ctor(", StringComparison.Ordinal);
    }

    private static void AnalyzeIl(
        PEReader peReader,
        MetadataReader reader,
        byte[] il,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        SortedSet<string> effects,
        SortedSet<string> calls,
        List<CallSiteSummary> callSites,
        SortedSet<string> fields,
        SortedSet<string> staticReadFields,
        SortedSet<int> sameAssemblyStaticReadFieldTokens,
        SortedSet<string> thrownExceptionTypes,
        List<ExceptionPropagationSite> exceptionPropagationSites,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        var offset = 0;
        var knownThrownExceptionSites = new List<KnownThrownExceptionSite>();
        var trackedLocals = new Dictionary<int, TrackedStackValue>();
        var trackedStack = new List<TrackedStackValue>();
        var suppressDynamicDispatchForNextCallvirt = false;
        while (offset < il.Length)
        {
            var instructionOffset = offset;
            var opCode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            var operandSize = GetOperandSize(opCode.OperandType, il, operandOffset);
            var operandToken = operandSize == 4 && IsMetadataTokenOperand(opCode.OperandType)
                ? BitConverter.ToInt32(il, operandOffset)
                : (int?)null;

            offset += operandSize;

            if (opCode == OpCodes.Constrained)
            {
                suppressDynamicDispatchForNextCallvirt = true;
                continue;
            }

            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj)
            {
                string? calledSymbol;
                if (opCode == OpCodes.Newobj)
                {
                    effects.Add("allocates_object");
                }
                else
                {
                    effects.Add("calls_method");
                }

                var usesDynamicDispatch = opCode == OpCodes.Callvirt &&
                    !suppressDynamicDispatchForNextCallvirt &&
                    operandToken is not null &&
                    ShouldTreatCallvirtAsDynamicDispatch(reader, operandToken.Value);
                if (usesDynamicDispatch)
                {
                    effects.Add("virtual_call");
                }

                if (operandToken is not null)
                {
                    calledSymbol = ResolveMethodExactKey(reader, operandToken.Value);
                    calls.Add(calledSymbol);
                    exceptionPropagationSites.Add(CreateExceptionPropagationSite(
                        il,
                        reader,
                        exceptionRegions,
                        instructionOffset,
                        calledSymbol));
                    if (TryGetCallTargetSignature(reader, operandToken.Value, opCode == OpCodes.Newobj, out var signature))
                    {
                        var argumentValues = PopTrackedStackValues(trackedStack, signature.ParameterTypes.Length);
                        var receiverValue = signature.HasReceiver
                            ? PopTrackedStackValue(trackedStack)
                            : TrackedStackValue.Unknown;
                        callSites.Add(CreateCallSiteSummary(
                            calledSymbol,
                            usesDynamicDispatch,
                            signature,
                            receiverValue,
                            argumentValues));
                        PushCallReturnValue(
                            peReader,
                            reader,
                            operandToken,
                            trackedStack,
                            calledSymbol,
                            signature,
                            argumentValues,
                            opCode == OpCodes.Newobj,
                            methodDefinitionHandlesByExactKey,
                            fieldDefinitionHandlesBySymbol,
                            fieldDefinitionHandlesByExactKey,
                            staticFieldFacts,
                            knownMethodReturnValues,
                            knownMethodReturnValueVisiting);
                    }
                    else
                    {
                        callSites.Add(new CallSiteSummary(calledSymbol)
                        {
                            UsesDynamicDispatch = usesDynamicDispatch,
                        });
                        trackedStack.Clear();
                        trackedLocals.Clear();
                        if (opCode == OpCodes.Newobj)
                        {
                            trackedStack.Add(TrackedStackValue.Unknown);
                        }
                    }
                }
            }
            else if (opCode == OpCodes.Calli)
            {
                effects.Add("indirect_call");
            }
            else if (opCode == OpCodes.Newarr)
            {
                effects.Add("allocates_array");
            }
            else if (opCode == OpCodes.Box)
            {
                effects.Add("allocates_box");
            }
            else if (opCode == OpCodes.Ldfld || opCode == OpCodes.Ldflda)
            {
                effects.Add("reads_instance_field");
                AddField(reader, operandToken, fieldDefinitionHandlesBySymbol, fieldDefinitionHandlesByExactKey, fields);
            }
            else if (opCode == OpCodes.Ldsfld || opCode == OpCodes.Ldsflda)
            {
                effects.Add("reads_static_field");
                AddField(reader, operandToken, fieldDefinitionHandlesBySymbol, fieldDefinitionHandlesByExactKey, fields);
                AddField(reader, operandToken, fieldDefinitionHandlesBySymbol, fieldDefinitionHandlesByExactKey, staticReadFields);
                AddSameAssemblyStaticFieldToken(
                    reader,
                    operandToken,
                    fieldDefinitionHandlesBySymbol,
                    fieldDefinitionHandlesByExactKey,
                    sameAssemblyStaticReadFieldTokens);
            }
            else if (opCode == OpCodes.Stfld)
            {
                effects.Add("writes_instance_field");
                AddField(reader, operandToken, fieldDefinitionHandlesBySymbol, fieldDefinitionHandlesByExactKey, fields);
            }
            else if (opCode == OpCodes.Stsfld)
            {
                effects.Add("writes_static_field");
                AddField(reader, operandToken, fieldDefinitionHandlesBySymbol, fieldDefinitionHandlesByExactKey, fields);
            }
            else if (opCode == OpCodes.Throw || opCode == OpCodes.Rethrow)
            {
                effects.Add("throws");
                var thrownExceptionType = opCode == OpCodes.Rethrow
                    ? TryResolveRethrowExceptionType(reader, exceptionRegions, instructionOffset, knownThrownExceptionSites)
                    : PeekTrackedExceptionType(trackedStack);
                if (opCode == OpCodes.Throw && thrownExceptionType != null)
                {
                    knownThrownExceptionSites.Add(new KnownThrownExceptionSite(instructionOffset, thrownExceptionType));
                }

                if (thrownExceptionType != null &&
                    IsEscapingThrow(il, reader, exceptionRegions, instructionOffset, thrownExceptionType))
                {
                    thrownExceptionTypes.Add(thrownExceptionType);
                }
            }
            else if (IsIndirectWrite(opCode))
            {
                effects.Add("writes_indirect_memory");
            }
            else if (opCode == OpCodes.Cpblk || opCode == OpCodes.Initblk)
            {
                effects.Add("writes_indirect_memory");
                effects.Add("block_memory_write");
            }
            else if (opCode == OpCodes.Ldftn || opCode == OpCodes.Ldvirtftn)
            {
                effects.Add("loads_method_pointer");
                if (operandToken is not null)
                {
                    calls.Add(ResolveMethodExactKey(reader, operandToken.Value));
                }
            }
            else if (opCode.Size == 0)
            {
                effects.Add($"unknown_opcode_at_{instructionOffset}");
                trackedStack.Clear();
                trackedLocals.Clear();
                break;
            }

            if (opCode != OpCodes.Call && opCode != OpCodes.Callvirt && opCode != OpCodes.Newobj)
            {
                ApplyTrackedStackTransition(
                    reader,
                    il,
                    opCode,
                    operandOffset,
                    operandToken,
                    trackedStack,
                    trackedLocals,
                    fieldDefinitionHandlesBySymbol,
                    fieldDefinitionHandlesByExactKey,
                    staticFieldFacts);
            }

            suppressDynamicDispatchForNextCallvirt = false;
        }
    }

    private static string GetCallSiteDeduplicationKey(CallSiteSummary callSite)
    {
        var argumentEvidenceKey = string.Join(
            ";",
            callSite.ArgumentEvidence.Select(static evidence =>
                $"{evidence.Target}:{evidence.ParameterIndex?.ToString() ?? string.Empty}:{evidence.Type}:{evidence.Value}"));
        return $"{callSite.ExactSymbolKey}|dynamic:{callSite.UsesDynamicDispatch}|evidence:{argumentEvidenceKey}";
    }

    private static CallSiteSummary CreateCallSiteSummary(
        string calledSymbol,
        bool usesDynamicDispatch,
        CallTargetSignature signature,
        TrackedStackValue receiverValue,
        IReadOnlyList<TrackedStackValue> argumentValues)
    {
        var argumentEvidence = new List<CallSiteArgumentEvidence>();
        if (signature.HasReceiver &&
            receiverValue.KnownStringComparer is { Length: > 0 } knownReceiverComparer)
        {
            argumentEvidence.Add(new CallSiteArgumentEvidence(
                Target: "receiver",
                ParameterIndex: null,
                Type: "System.StringComparer",
                Value: knownReceiverComparer));
        }

        for (var parameterIndex = 0; parameterIndex < signature.ParameterTypes.Length; parameterIndex++)
        {
            var argumentValue = parameterIndex < argumentValues.Count
                ? argumentValues[parameterIndex]
                : TrackedStackValue.Unknown;
            if (argumentValue.KnownStringComparer is { Length: > 0 } knownArgumentComparer)
            {
                argumentEvidence.Add(new CallSiteArgumentEvidence(
                    Target: "argument",
                    ParameterIndex: parameterIndex,
                    Type: "System.StringComparer",
                    Value: knownArgumentComparer));
            }

            if (string.Equals(signature.ParameterTypes[parameterIndex], "System.StringComparison", StringComparison.Ordinal) &&
                argumentValue.Int32Constant is int comparisonValue &&
                TryGetStringComparisonValueName(comparisonValue, out var stringComparisonValueName))
            {
                argumentEvidence.Add(new CallSiteArgumentEvidence(
                    Target: "argument",
                    ParameterIndex: parameterIndex,
                    Type: "System.StringComparison",
                    Value: stringComparisonValueName));
            }
        }

        return new CallSiteSummary(calledSymbol)
        {
            UsesDynamicDispatch = usesDynamicDispatch,
            ArgumentEvidence = argumentEvidence.ToArray(),
        };
    }

    private static void PushCallReturnValue(
        PEReader peReader,
        MetadataReader reader,
        int? operandToken,
        List<TrackedStackValue> trackedStack,
        string calledSymbol,
        CallTargetSignature signature,
        IReadOnlyList<TrackedStackValue> argumentValues,
        bool isObjectConstruction,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        if (isObjectConstruction)
        {
            var exceptionType = TryGetConstructedExceptionType(calledSymbol);
            trackedStack.Add(exceptionType == null
                ? TrackedStackValue.Unknown
                : TrackedStackValue.FromKnownExceptionType(exceptionType));
            return;
        }

        if (string.Equals(signature.ReturnType, "void", StringComparison.Ordinal))
        {
            return;
        }

        trackedStack.Add(TryGetKnownCallReturnValue(
                peReader,
                reader,
                operandToken,
                calledSymbol,
                argumentValues,
                methodDefinitionHandlesByExactKey,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                staticFieldFacts,
                knownMethodReturnValues,
                knownMethodReturnValueVisiting,
                out var returnValue)
            ? returnValue
            : TrackedStackValue.Unknown);
    }

    private static void ApplyTrackedStackTransition(
        MetadataReader reader,
        byte[] il,
        OpCode opCode,
        int operandOffset,
        int? operandToken,
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts)
    {
        if (TryGetPushedInt32Constant(opCode, il, operandOffset, out var pushedInt32Constant))
        {
            trackedStack.Add(TrackedStackValue.FromInt32(pushedInt32Constant));
            return;
        }

        if (TryGetStoreLocalIndex(opCode, il, operandOffset, out var storeLocalIndex))
        {
            trackedLocals[storeLocalIndex] = PopTrackedStackValue(trackedStack);
            return;
        }

        if (TryGetLoadLocalIndex(opCode, il, operandOffset, out var loadLocalIndex))
        {
            trackedStack.Add(trackedLocals.TryGetValue(loadLocalIndex, out var trackedLocalValue)
                ? trackedLocalValue
                : TrackedStackValue.Unknown);
            return;
        }

        if (opCode == OpCodes.Dup)
        {
            trackedStack.Add(trackedStack.Count == 0 ? TrackedStackValue.Unknown : trackedStack[^1]);
            return;
        }

        if (opCode == OpCodes.Ldsfld)
        {
            trackedStack.Add(TryGetKnownTrackedStaticFieldValue(
                    reader,
                    operandToken,
                    fieldDefinitionHandlesBySymbol,
                    fieldDefinitionHandlesByExactKey,
                    staticFieldFacts,
                    out var trackedFieldValue)
                ? trackedFieldValue
                : TrackedStackValue.Unknown);
            return;
        }

        if (opCode == OpCodes.Ldfld || opCode == OpCodes.Ldflda)
        {
            PopTrackedStackValue(trackedStack);
            trackedStack.Add(TrackedStackValue.Unknown);
            return;
        }

        if (opCode == OpCodes.Stfld)
        {
            PopTrackedStackValue(trackedStack);
            PopTrackedStackValue(trackedStack);
            return;
        }

        if (opCode == OpCodes.Stsfld)
        {
            PopTrackedStackValue(trackedStack);
            return;
        }

        if (!TryGetStackPopCount(opCode.StackBehaviourPop, out var popCount) ||
            !TryGetStackPushCount(opCode.StackBehaviourPush, out var pushCount))
        {
            trackedStack.Clear();
            trackedLocals.Clear();
            return;
        }

        PopTrackedStackValues(trackedStack, popCount);
        for (var i = 0; i < pushCount; i++)
        {
            trackedStack.Add(TrackedStackValue.Unknown);
        }

        if (ShouldResetTrackedState(opCode))
        {
            trackedStack.Clear();
            trackedLocals.Clear();
        }
    }

    private static bool TryGetKnownTrackedStaticFieldValue(
        MetadataReader reader,
        int? operandToken,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        out TrackedStackValue trackedValue)
    {
        trackedValue = TrackedStackValue.Unknown;
        if (operandToken is null)
        {
            return false;
        }

        if (TryResolveSameAssemblyFieldDefinitionHandle(
                reader,
                operandToken.Value,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                out var fieldHandle) &&
            staticFieldFacts.TryGetValue(MetadataTokens.GetToken(fieldHandle), out var staticFieldFact) &&
            !staticFieldFact.TrackedValue.IsUnknown)
        {
            trackedValue = staticFieldFact.TrackedValue;
            return true;
        }

        return TryGetKnownStringComparerIdentity(
            ResolveFieldToken(reader, operandToken.Value),
            out trackedValue);
    }

    private static bool TryGetKnownCallReturnValue(
        PEReader peReader,
        MetadataReader reader,
        int? operandToken,
        string calledSymbol,
        IReadOnlyList<TrackedStackValue> argumentValues,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting,
        out TrackedStackValue trackedValue)
    {
        if (TryGetKnownStringComparerIdentity(calledSymbol, out trackedValue))
        {
            return true;
        }

        if (string.Equals(
                calledSymbol,
                "System.StringComparer.FromComparison(System.StringComparison)->System.StringComparer",
                StringComparison.Ordinal) &&
            argumentValues.Count == 1 &&
            argumentValues[0].Int32Constant is int comparisonValue)
        {
            return TryGetStringComparerIdentityFromComparison(comparisonValue, out trackedValue);
        }

        if (operandToken is not null &&
            TryResolveSameAssemblyMethodDefinitionHandle(
                reader,
                operandToken.Value,
                methodDefinitionHandlesByExactKey,
                out var methodDefinitionHandle) &&
            TryGetKnownMethodReturnValue(
                peReader,
                reader,
                methodDefinitionHandle,
                methodDefinitionHandlesByExactKey,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                staticFieldFacts,
                knownMethodReturnValues,
                knownMethodReturnValueVisiting,
                out trackedValue))
        {
            return true;
        }

        trackedValue = TrackedStackValue.Unknown;
        return false;
    }

    private static bool TryResolveSameAssemblyMethodDefinitionHandle(
        MetadataReader reader,
        int metadataToken,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        out MethodDefinitionHandle handle)
    {
        handle = default;
        var resolvedHandle = MetadataTokens.Handle(metadataToken);
        switch (resolvedHandle.Kind)
        {
            case HandleKind.MethodDefinition:
                handle = (MethodDefinitionHandle)resolvedHandle;
                return true;
            case HandleKind.MethodSpecification:
                var specification = reader.GetMethodSpecification((MethodSpecificationHandle)resolvedHandle);
                if (specification.Method.Kind == HandleKind.MethodDefinition)
                {
                    handle = (MethodDefinitionHandle)specification.Method;
                    return true;
                }

                if (specification.Method.Kind == HandleKind.MemberReference)
                {
                    return TryResolveMethodDefinitionHandleFromMemberReference(
                        reader,
                        (MemberReferenceHandle)specification.Method,
                        methodDefinitionHandlesByExactKey,
                        out handle);
                }

                return false;
            case HandleKind.MemberReference:
                return TryResolveMethodDefinitionHandleFromMemberReference(
                    reader,
                    (MemberReferenceHandle)resolvedHandle,
                    methodDefinitionHandlesByExactKey,
                    out handle);
            default:
                return false;
        }
    }

    private static bool TryResolveMethodDefinitionHandleFromMemberReference(
        MetadataReader reader,
        MemberReferenceHandle handle,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        out MethodDefinitionHandle resolvedHandle)
    {
        var exactKey = GetMemberReferenceExactKey(reader, handle);
        if (methodDefinitionHandlesByExactKey.TryGetValue(exactKey, out resolvedHandle))
        {
            return true;
        }

        var lookupKey = GetMemberReferenceMethodLookupExactKey(reader, handle);
        return !string.Equals(lookupKey, exactKey, StringComparison.Ordinal) &&
            methodDefinitionHandlesByExactKey.TryGetValue(lookupKey, out resolvedHandle);
    }

    private static bool TryGetKnownMethodReturnValue(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle handle,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting,
        out TrackedStackValue trackedValue)
    {
        var metadataToken = MetadataTokens.GetToken(handle);
        if (knownMethodReturnValues.TryGetValue(metadataToken, out trackedValue))
        {
            return !trackedValue.IsUnknown;
        }

        if (!knownMethodReturnValueVisiting.Add(metadataToken))
        {
            trackedValue = TrackedStackValue.Unknown;
            return false;
        }

        try
        {
            trackedValue = AnalyzeKnownMethodReturnValue(
                peReader,
                reader,
                handle,
                methodDefinitionHandlesByExactKey,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                staticFieldFacts,
                knownMethodReturnValues,
                knownMethodReturnValueVisiting);
            knownMethodReturnValues[metadataToken] = trackedValue;
            return !trackedValue.IsUnknown;
        }
        finally
        {
            knownMethodReturnValueVisiting.Remove(metadataToken);
        }
    }

    private static TrackedStackValue AnalyzeKnownMethodReturnValue(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle handle,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFieldFacts,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        var definition = reader.GetMethodDefinition(handle);
        if (definition.RelativeVirtualAddress == 0 ||
            (definition.Attributes & MethodAttributes.Abstract) != 0)
        {
            return TrackedStackValue.Unknown;
        }

        CallTargetSignature signature;
        try
        {
            signature = GetMethodDefinitionCallTargetSignature(reader, handle, isObjectConstruction: false);
        }
        catch (BadImageFormatException)
        {
            return TrackedStackValue.Unknown;
        }
        catch (InvalidOperationException)
        {
            return TrackedStackValue.Unknown;
        }

        if (string.Equals(signature.ReturnType, "void", StringComparison.Ordinal))
        {
            return TrackedStackValue.Unknown;
        }

        var body = peReader.GetMethodBody(definition.RelativeVirtualAddress);
        var il = body.GetILBytes();
        if (il is null)
        {
            return TrackedStackValue.Unknown;
        }

        var trackedLocals = new Dictionary<int, TrackedStackValue>();
        var trackedStack = new List<TrackedStackValue>();
        var pendingBranchStates = new Dictionary<int, BranchTrackedState>();
        var offset = 0;
        TrackedStackValue? knownReturnValue = null;
        while (offset < il.Length)
        {
            var instructionOffset = offset;
            if (pendingBranchStates.TryGetValue(instructionOffset, out var pendingBranchState))
            {
                if ((trackedStack.Count != 0 || trackedLocals.Count != 0) &&
                    !TrackedStatesEqual(trackedStack, trackedLocals, pendingBranchState))
                {
                    return TrackedStackValue.Unknown;
                }

                RestoreTrackedState(trackedStack, trackedLocals, pendingBranchState);
            }

            var opCode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            var operandSize = GetOperandSize(opCode.OperandType, il, operandOffset);
            var operandToken = operandSize == 4 && IsMetadataTokenOperand(opCode.OperandType)
                ? BitConverter.ToInt32(il, operandOffset)
                : (int?)null;
            offset += operandSize;

            if (opCode == OpCodes.Constrained)
            {
                continue;
            }

            if (opCode == OpCodes.Ret)
            {
                var returnValue = PopTrackedStackValue(trackedStack);
                if (returnValue.IsUnknown)
                {
                    return TrackedStackValue.Unknown;
                }

                if (knownReturnValue is null)
                {
                    knownReturnValue = returnValue;
                }
                else if (knownReturnValue.Value != returnValue)
                {
                    return TrackedStackValue.Unknown;
                }

                trackedStack.Clear();
                trackedLocals.Clear();
                continue;
            }

            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj)
            {
                if (operandToken is not null &&
                    TryGetCallTargetSignature(reader, operandToken.Value, opCode == OpCodes.Newobj, out var calledSignature))
                {
                    var argumentValues = PopTrackedStackValues(trackedStack, calledSignature.ParameterTypes.Length);
                    if (calledSignature.HasReceiver)
                    {
                        PopTrackedStackValue(trackedStack);
                    }

                    PushCallReturnValue(
                        peReader,
                        reader,
                        operandToken,
                        trackedStack,
                        ResolveMethodExactKey(reader, operandToken.Value),
                        calledSignature,
                        argumentValues,
                        opCode == OpCodes.Newobj,
                        methodDefinitionHandlesByExactKey,
                        fieldDefinitionHandlesBySymbol,
                        fieldDefinitionHandlesByExactKey,
                        staticFieldFacts,
                        knownMethodReturnValues,
                        knownMethodReturnValueVisiting);
                }
                else
                {
                    trackedStack.Clear();
                    trackedLocals.Clear();
                    if (opCode == OpCodes.Newobj)
                    {
                        trackedStack.Add(TrackedStackValue.Unknown);
                    }
                }

                continue;
            }

            if (opCode.FlowControl == FlowControl.Branch &&
                TryGetBranchTargetOffset(opCode, il, operandOffset, instructionOffset, out var branchTargetOffset))
            {
                var branchState = CaptureTrackedState(trackedStack, trackedLocals);
                if (pendingBranchStates.TryGetValue(branchTargetOffset, out var existingBranchState) &&
                    !TrackedStatesEqual(branchState.Stack, branchState.Locals, existingBranchState))
                {
                    return TrackedStackValue.Unknown;
                }

                pendingBranchStates[branchTargetOffset] = branchState;
            }

            ApplyTrackedStackTransition(
                reader,
                il,
                opCode,
                operandOffset,
                operandToken,
                trackedStack,
                trackedLocals,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                staticFieldFacts);
        }

        return knownReturnValue ?? TrackedStackValue.Unknown;
    }

    private static bool TryGetKnownStringComparerIdentity(string symbol, out TrackedStackValue trackedValue)
    {
        trackedValue = symbol switch
        {
            "System.StringComparer.get_CurrentCulture()->System.StringComparer" => TrackedStackValue.FromKnownStringComparer("System.StringComparer.CurrentCulture"),
            "System.StringComparer.get_CurrentCultureIgnoreCase()->System.StringComparer" => TrackedStackValue.FromKnownStringComparer("System.StringComparer.CurrentCultureIgnoreCase"),
            "System.StringComparer.get_InvariantCulture()->System.StringComparer" => TrackedStackValue.FromKnownStringComparer("System.StringComparer.InvariantCulture"),
            "System.StringComparer.get_InvariantCultureIgnoreCase()->System.StringComparer" => TrackedStackValue.FromKnownStringComparer("System.StringComparer.InvariantCultureIgnoreCase"),
            "System.StringComparer.get_Ordinal()->System.StringComparer" => TrackedStackValue.FromKnownStringComparer("System.StringComparer.Ordinal"),
            "System.StringComparer.get_OrdinalIgnoreCase()->System.StringComparer" => TrackedStackValue.FromKnownStringComparer("System.StringComparer.OrdinalIgnoreCase"),
            _ => TrackedStackValue.Unknown
        };

        return !trackedValue.IsUnknown;
    }

    private static bool TryGetStringComparisonValueName(int value, out string name)
    {
        if (Enum.IsDefined(typeof(StringComparison), value))
        {
            name = $"System.StringComparison.{(StringComparison)value}";
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static bool TryGetStringComparerIdentityFromComparison(int comparisonValue, out TrackedStackValue trackedValue)
    {
        trackedValue = comparisonValue switch
        {
            0 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.CurrentCulture"),
            1 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.CurrentCultureIgnoreCase"),
            2 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.InvariantCulture"),
            3 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.InvariantCultureIgnoreCase"),
            4 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.Ordinal"),
            5 => TrackedStackValue.FromKnownStringComparer("System.StringComparer.OrdinalIgnoreCase"),
            _ => TrackedStackValue.Unknown
        };

        return !trackedValue.IsUnknown;
    }

    private static bool TryGetCallTargetSignature(
        MetadataReader reader,
        int metadataToken,
        bool isObjectConstruction,
        out CallTargetSignature signature)
    {
        var handle = MetadataTokens.Handle(metadataToken);
        try
        {
            signature = handle.Kind switch
            {
                HandleKind.MethodDefinition => GetMethodDefinitionCallTargetSignature(
                    reader,
                    (MethodDefinitionHandle)handle,
                    isObjectConstruction),
                HandleKind.MemberReference => GetMemberReferenceCallTargetSignature(
                    reader,
                    (MemberReferenceHandle)handle,
                    isObjectConstruction),
                HandleKind.MethodSpecification => GetMethodSpecificationCallTargetSignature(
                    reader,
                    (MethodSpecificationHandle)handle,
                    isObjectConstruction),
                _ => default
            };
            return handle.Kind is HandleKind.MethodDefinition
                or HandleKind.MemberReference
                or HandleKind.MethodSpecification;
        }
        catch (BadImageFormatException)
        {
            signature = default;
            return false;
        }
        catch (InvalidOperationException)
        {
            signature = default;
            return false;
        }
    }

    private static CallTargetSignature GetMethodDefinitionCallTargetSignature(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        bool isObjectConstruction)
    {
        var definition = reader.GetMethodDefinition(handle);
        var decodedSignature = definition.DecodeSignature(new TypeNameProvider(reader), genericContext: null);
        return new CallTargetSignature(
            HasReceiver: !isObjectConstruction && (definition.Attributes & MethodAttributes.Static) == 0,
            ParameterTypes: decodedSignature.ParameterTypes.ToArray(),
            ReturnType: decodedSignature.ReturnType);
    }

    private static CallTargetSignature GetMemberReferenceCallTargetSignature(
        MetadataReader reader,
        MemberReferenceHandle handle,
        bool isObjectConstruction)
    {
        var memberReference = reader.GetMemberReference(handle);
        var decodedSignature = memberReference.DecodeMethodSignature(new TypeNameProvider(reader), genericContext: null);
        return new CallTargetSignature(
            HasReceiver: !isObjectConstruction && decodedSignature.Header.IsInstance,
            ParameterTypes: decodedSignature.ParameterTypes.ToArray(),
            ReturnType: decodedSignature.ReturnType);
    }

    private static CallTargetSignature GetMethodSpecificationCallTargetSignature(
        MetadataReader reader,
        MethodSpecificationHandle handle,
        bool isObjectConstruction)
    {
        var specification = reader.GetMethodSpecification(handle);
        return specification.Method.Kind switch
        {
            HandleKind.MethodDefinition => GetMethodDefinitionCallTargetSignature(
                reader,
                (MethodDefinitionHandle)specification.Method,
                isObjectConstruction),
            HandleKind.MemberReference => GetMemberReferenceCallTargetSignature(
                reader,
                (MemberReferenceHandle)specification.Method,
                isObjectConstruction),
            _ => default,
        };
    }

    private static TrackedStackValue[] PopTrackedStackValues(List<TrackedStackValue> trackedStack, int count)
    {
        var values = new TrackedStackValue[count];
        for (var index = count - 1; index >= 0; index--)
        {
            values[index] = PopTrackedStackValue(trackedStack);
        }

        return values;
    }

    private static TrackedStackValue PopTrackedStackValue(List<TrackedStackValue> trackedStack)
    {
        if (trackedStack.Count == 0)
        {
            return TrackedStackValue.Unknown;
        }

        var lastIndex = trackedStack.Count - 1;
        var value = trackedStack[lastIndex];
        trackedStack.RemoveAt(lastIndex);
        return value;
    }

    private static string? PeekTrackedExceptionType(List<TrackedStackValue> trackedStack)
    {
        return trackedStack.Count == 0 || string.IsNullOrWhiteSpace(trackedStack[^1].KnownExceptionType)
            ? null
            : trackedStack[^1].KnownExceptionType;
    }

    private static bool TryGetStackPopCount(StackBehaviour behavior, out int count)
    {
        count = behavior switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or
            StackBehaviour.Popi or
            StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or
            StackBehaviour.Popi_pop1 or
            StackBehaviour.Popi_popi or
            StackBehaviour.Popi_popi8 or
            StackBehaviour.Popi_popr4 or
            StackBehaviour.Popi_popr8 or
            StackBehaviour.Popref_pop1 or
            StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or
            StackBehaviour.Popref_popi_popi or
            StackBehaviour.Popref_popi_popi8 or
            StackBehaviour.Popref_popi_popr4 or
            StackBehaviour.Popref_popi_popr8 or
            StackBehaviour.Popref_popi_popref => 3,
            _ => -1,
        };

        return count >= 0;
    }

    private static bool TryGetStackPushCount(StackBehaviour behavior, out int count)
    {
        count = behavior switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 or
            StackBehaviour.Pushi or
            StackBehaviour.Pushi8 or
            StackBehaviour.Pushr4 or
            StackBehaviour.Pushr8 or
            StackBehaviour.Pushref => 1,
            StackBehaviour.Push1_push1 => 2,
            _ => -1,
        };

        return count >= 0;
    }

    private static bool ShouldResetTrackedState(OpCode opCode)
    {
        return opCode.FlowControl is FlowControl.Branch
            or FlowControl.Cond_Branch
            or FlowControl.Return
            or FlowControl.Throw;
    }

    private static BranchTrackedState CaptureTrackedState(
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals)
    {
        return new BranchTrackedState(
            new List<TrackedStackValue>(trackedStack),
            new Dictionary<int, TrackedStackValue>(trackedLocals));
    }

    private static void RestoreTrackedState(
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals,
        BranchTrackedState branchState)
    {
        trackedStack.Clear();
        trackedStack.AddRange(branchState.Stack);

        trackedLocals.Clear();
        foreach (var pair in branchState.Locals)
        {
            trackedLocals[pair.Key] = pair.Value;
        }
    }

    private static bool TrackedStatesEqual(
        List<TrackedStackValue> trackedStack,
        Dictionary<int, TrackedStackValue> trackedLocals,
        BranchTrackedState branchState)
    {
        if (trackedStack.Count != branchState.Stack.Count || trackedLocals.Count != branchState.Locals.Count)
        {
            return false;
        }

        for (var i = 0; i < trackedStack.Count; i++)
        {
            if (trackedStack[i] != branchState.Stack[i])
            {
                return false;
            }
        }

        foreach (var pair in trackedLocals)
        {
            if (!branchState.Locals.TryGetValue(pair.Key, out var value) || value != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKnownStableIdentityInitializerCall(string calledSymbol)
    {
        return calledSymbol.StartsWith("System.Array.Empty<", StringComparison.Ordinal);
    }

    private static StaticFieldInitializerValue[] PopStaticFieldInitializerValues(
        List<StaticFieldInitializerValue> trackedStack,
        int count)
    {
        var values = new StaticFieldInitializerValue[count];
        for (var index = count - 1; index >= 0; index--)
        {
            values[index] = PopStaticFieldInitializerValue(trackedStack);
        }

        return values;
    }

    private static StaticFieldInitializerValue PopStaticFieldInitializerValue(List<StaticFieldInitializerValue> trackedStack)
    {
        if (trackedStack.Count == 0)
        {
            return StaticFieldInitializerValue.Unknown;
        }

        var lastIndex = trackedStack.Count - 1;
        var value = trackedStack[lastIndex];
        trackedStack.RemoveAt(lastIndex);
        return value;
    }

    private static bool TryCreateStaticFieldInitializerValue(
        TrackedStackValue trackedValue,
        out StaticFieldInitializerValue value)
    {
        if (trackedValue.Int32Constant is not null)
        {
            value = StaticFieldInitializerValue.FromConstantTracked(trackedValue);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(trackedValue.KnownStringComparer))
        {
            value = StaticFieldInitializerValue.FromStableIdentityTracked(trackedValue);
            return true;
        }

        value = StaticFieldInitializerValue.Unknown;
        return false;
    }

    private static bool TryGetTrackedStaticFieldInitializerValue(
        MetadataReader reader,
        int? metadataToken,
        IReadOnlyDictionary<int, StaticFieldInitializerValue> assignmentsByFieldToken,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        out StaticFieldInitializerValue value)
    {
        value = StaticFieldInitializerValue.Unknown;
        if (metadataToken is null)
        {
            return false;
        }

        if (TryResolveSameAssemblyFieldDefinitionHandle(
                reader,
                metadataToken.Value,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                out var sameAssemblyFieldHandle) &&
            assignmentsByFieldToken.TryGetValue(MetadataTokens.GetToken(sameAssemblyFieldHandle), out value))
        {
            return value.Kind != StaticFieldInitializerValueKind.Unknown;
        }

        if (TryGetKnownStringComparerIdentity(ResolveFieldToken(reader, metadataToken.Value), out var trackedValue) &&
            TryCreateStaticFieldInitializerValue(trackedValue, out value))
        {
            return true;
        }

        value = StaticFieldInitializerValue.Unknown;
        return false;
    }

    private static bool TryGetBranchTargetOffset(
        OpCode opCode,
        byte[] il,
        int operandOffset,
        int instructionOffset,
        out int targetOffset)
    {
        targetOffset = 0;
        if (opCode.OperandType == OperandType.ShortInlineBrTarget)
        {
            targetOffset = instructionOffset + opCode.Size + 1 + unchecked((sbyte)il[operandOffset]);
            return true;
        }

        if (opCode.OperandType == OperandType.InlineBrTarget)
        {
            targetOffset = instructionOffset + opCode.Size + 4 + BitConverter.ToInt32(il, operandOffset);
            return true;
        }

        return false;
    }

    private static bool TryGetStoreLocalIndex(OpCode opCode, byte[] il, int operandOffset, out int localIndex)
    {
        return TryGetLocalIndex(
            opCode,
            il,
            operandOffset,
            OpCodes.Stloc_0,
            OpCodes.Stloc_1,
            OpCodes.Stloc_2,
            OpCodes.Stloc_3,
            OpCodes.Stloc_S,
            OpCodes.Stloc,
            out localIndex);
    }

    private static bool TryGetPushedInt32Constant(OpCode opCode, byte[] il, int operandOffset, out int value)
    {
        value = 0;
        if (opCode == OpCodes.Ldc_I4_M1)
        {
            value = -1;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_0)
        {
            value = 0;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_1)
        {
            value = 1;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_2)
        {
            value = 2;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_3)
        {
            value = 3;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_4)
        {
            value = 4;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_5)
        {
            value = 5;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_6)
        {
            value = 6;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_7)
        {
            value = 7;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_8)
        {
            value = 8;
            return true;
        }

        if (opCode == OpCodes.Ldc_I4_S)
        {
            value = unchecked((sbyte)il[operandOffset]);
            return true;
        }

        if (opCode == OpCodes.Ldc_I4)
        {
            value = BitConverter.ToInt32(il, operandOffset);
            return true;
        }

        return false;
    }

    private static bool TryGetLoadLocalIndex(OpCode opCode, byte[] il, int operandOffset, out int localIndex)
    {
        return TryGetLocalIndex(
            opCode,
            il,
            operandOffset,
            OpCodes.Ldloc_0,
            OpCodes.Ldloc_1,
            OpCodes.Ldloc_2,
            OpCodes.Ldloc_3,
            OpCodes.Ldloc_S,
            OpCodes.Ldloc,
            out localIndex);
    }

    private static bool TryGetLocalIndex(
        OpCode opCode,
        byte[] il,
        int operandOffset,
        OpCode index0,
        OpCode index1,
        OpCode index2,
        OpCode index3,
        OpCode shortForm,
        OpCode wideForm,
        out int localIndex)
    {
        if (opCode == index0)
        {
            localIndex = 0;
            return true;
        }

        if (opCode == index1)
        {
            localIndex = 1;
            return true;
        }

        if (opCode == index2)
        {
            localIndex = 2;
            return true;
        }

        if (opCode == index3)
        {
            localIndex = 3;
            return true;
        }

        if (opCode == shortForm)
        {
            localIndex = il[operandOffset];
            return true;
        }

        if (opCode == wideForm)
        {
            localIndex = BitConverter.ToUInt16(il, operandOffset);
            return true;
        }

        localIndex = -1;
        return false;
    }

    private static string? TryGetConstructedExceptionType(string? constructorSymbol)
    {
        if (string.IsNullOrWhiteSpace(constructorSymbol))
        {
            return null;
        }

        var ctorIndex = constructorSymbol.IndexOf("..ctor(", StringComparison.Ordinal);
        if (ctorIndex <= 0)
        {
            return null;
        }

        var typeName = constructorSymbol.Substring(0, ctorIndex);
        return typeName.EndsWith("Exception", StringComparison.Ordinal) ? typeName : null;
    }

    private static string? TryResolveRethrowExceptionType(
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset,
        IReadOnlyList<KnownThrownExceptionSite> knownThrownExceptionSites)
    {
        if (TryGetEnclosingCatchRegion(exceptionRegions, instructionOffset, out var catchRegion))
        {
            var catchExceptionType = GetCatchExceptionType(reader, catchRegion);
            if (!string.IsNullOrWhiteSpace(catchExceptionType))
            {
                var protectedTryExceptionTypes = knownThrownExceptionSites
                    .Where(site =>
                        ContainsOffset(catchRegion.TryOffset, catchRegion.TryLength, site.InstructionOffset) &&
                        CatchHandlesException(reader, site.ExceptionType, catchExceptionType))
                    .Select(site => site.ExceptionType)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (protectedTryExceptionTypes.Length == 1)
                {
                    return protectedTryExceptionTypes[0];
                }
            }
        }

        return GetEnclosingCatchExceptionType(reader, exceptionRegions, instructionOffset);
    }

    private static ExceptionPropagationSite CreateExceptionPropagationSite(
        byte[] il,
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset,
        string exactSymbolKey)
    {
        return new ExceptionPropagationSite(
            exactSymbolKey,
            instructionOffset,
            GetHandlingCatchExceptionTypes(reader, exceptionRegions, instructionOffset),
            IsShadowedByDefinitelyThrowingFinally(il, exceptionRegions, instructionOffset));
    }

    private static bool ExceptionEscapesPropagationSite(
        ExceptionPropagationSite propagationSite,
        string thrownExceptionType)
    {
        if (propagationSite.IsShadowedByDefinitelyThrowingFinally)
        {
            return false;
        }

        foreach (var catchExceptionType in propagationSite.HandlingCatchExceptionTypes)
        {
            if (string.IsNullOrWhiteSpace(catchExceptionType))
            {
                continue;
            }

            if (string.Equals(catchExceptionType, "System.Exception", StringComparison.Ordinal) ||
                string.Equals(catchExceptionType, "System.Object", StringComparison.Ordinal) ||
                string.Equals(catchExceptionType, thrownExceptionType, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsEscapingThrow(
        byte[] il,
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset,
        string thrownExceptionType)
    {
        if (IsShadowedByDefinitelyThrowingFinally(il, exceptionRegions, instructionOffset))
        {
            return false;
        }

        foreach (var exceptionRegion in exceptionRegions)
        {
            if (exceptionRegion.Kind != ExceptionRegionKind.Catch ||
                !ContainsOffset(exceptionRegion.TryOffset, exceptionRegion.TryLength, instructionOffset))
            {
                continue;
            }

            var catchExceptionType = GetCatchExceptionType(reader, exceptionRegion);
            if (CatchHandlesException(reader, thrownExceptionType, catchExceptionType))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] GetHandlingCatchExceptionTypes(
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset)
    {
        return exceptionRegions
            .Where(exceptionRegion =>
                exceptionRegion.Kind == ExceptionRegionKind.Catch &&
                ContainsOffset(exceptionRegion.TryOffset, exceptionRegion.TryLength, instructionOffset))
            .Select(exceptionRegion => GetCatchExceptionType(reader, exceptionRegion))
            .Where(exceptionType => !string.IsNullOrWhiteSpace(exceptionType))
            .Distinct(StringComparer.Ordinal)
            .ToArray()!;
    }

    private static bool IsShadowedByDefinitelyThrowingFinally(
        byte[] il,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset)
    {
        foreach (var exceptionRegion in exceptionRegions)
        {
            if (exceptionRegion.Kind != ExceptionRegionKind.Finally ||
                !ContainsOffset(exceptionRegion.TryOffset, exceptionRegion.TryLength, instructionOffset) ||
                ContainsOffset(exceptionRegion.HandlerOffset, exceptionRegion.HandlerLength, instructionOffset))
            {
                continue;
            }

            if (FinallyHandlerDefinitelyThrows(il, exceptionRegion.HandlerOffset, exceptionRegion.HandlerLength))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FinallyHandlerDefinitelyThrows(byte[] il, int handlerOffset, int handlerLength)
    {
        var endOffset = handlerOffset + handlerLength;
        var offset = handlerOffset;
        OpCode lastMeaningfulOpCode = default;
        var foundMeaningfulInstruction = false;
        while (offset < endOffset)
        {
            var opCode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            var operandSize = GetOperandSize(opCode.OperandType, il, operandOffset);
            offset += operandSize;

            if (opCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return ||
                opCode == OpCodes.Endfinally ||
                opCode == OpCodes.Endfilter ||
                opCode == OpCodes.Leave ||
                opCode == OpCodes.Leave_S)
            {
                return false;
            }

            if (opCode != OpCodes.Nop)
            {
                lastMeaningfulOpCode = opCode;
                foundMeaningfulInstruction = true;
            }
        }

        return foundMeaningfulInstruction &&
            (lastMeaningfulOpCode == OpCodes.Throw || lastMeaningfulOpCode == OpCodes.Rethrow);
    }

    private static bool TryGetEnclosingCatchRegion(
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset,
        out ExceptionRegion catchRegion)
    {
        catchRegion = default;
        var smallestHandlerLength = int.MaxValue;
        var found = false;
        foreach (var exceptionRegion in exceptionRegions)
        {
            if (exceptionRegion.Kind != ExceptionRegionKind.Catch ||
                !ContainsOffset(exceptionRegion.HandlerOffset, exceptionRegion.HandlerLength, instructionOffset) ||
                exceptionRegion.HandlerLength >= smallestHandlerLength)
            {
                continue;
            }

            catchRegion = exceptionRegion;
            smallestHandlerLength = exceptionRegion.HandlerLength;
            found = true;
        }

        return found;
    }

    private static string? GetEnclosingCatchExceptionType(
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset)
    {
        return TryGetEnclosingCatchRegion(exceptionRegions, instructionOffset, out var catchRegion)
            ? GetCatchExceptionType(reader, catchRegion)
            : null;
    }

    private static bool ContainsOffset(int startOffset, int length, int instructionOffset)
    {
        return instructionOffset >= startOffset && instructionOffset < startOffset + length;
    }

    private static string? GetCatchExceptionType(MetadataReader reader, ExceptionRegion exceptionRegion)
    {
        if (exceptionRegion.Kind != ExceptionRegionKind.Catch)
        {
            return null;
        }

        if (exceptionRegion.CatchType.IsNil)
        {
            return "System.Exception";
        }

        return GetEntityTypeName(reader, exceptionRegion.CatchType);
    }

    private static string? GetEntityTypeName(MetadataReader reader, EntityHandle handle)
    {
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => GetExceptionTypeDefinitionName(reader, (TypeDefinitionHandle)handle),
                HandleKind.TypeReference => GetExceptionTypeReferenceName(reader, (TypeReferenceHandle)handle),
                _ => null
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static string GetExceptionTypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        return GetQualifiedTypeName(
            reader.GetString(definition.Namespace),
            reader.GetString(definition.Name));
    }

    private static string GetExceptionTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        return GetQualifiedTypeName(
            reader.GetString(reference.Namespace),
            reader.GetString(reference.Name));
    }

    private static string GetQualifiedTypeName(string typeNamespace, string typeName)
    {
        return string.IsNullOrWhiteSpace(typeNamespace)
            ? typeName
            : typeNamespace + "." + typeName;
    }

    private static bool CatchHandlesException(
        MetadataReader reader,
        string thrownExceptionType,
        string? catchExceptionType)
    {
        if (string.IsNullOrWhiteSpace(catchExceptionType))
        {
            return false;
        }

        if (string.Equals(catchExceptionType, "System.Exception", StringComparison.Ordinal) ||
            string.Equals(catchExceptionType, "System.Object", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(thrownExceptionType, catchExceptionType, StringComparison.Ordinal))
        {
            return true;
        }

        return IsDefinedTypeDerivedFrom(reader, thrownExceptionType, catchExceptionType);
    }

    private static bool IsDefinedTypeDerivedFrom(
        MetadataReader reader,
        string thrownExceptionType,
        string catchExceptionType)
    {
        try
        {
            var currentType = thrownExceptionType;
            var visitedTypes = new HashSet<string>(StringComparer.Ordinal);
            while (visitedTypes.Add(currentType))
            {
                var definitionHandle = reader.TypeDefinitions
                    .FirstOrDefault(handle => string.Equals(
                        GetExceptionTypeDefinitionName(reader, handle),
                        currentType,
                        StringComparison.Ordinal));
                if (definitionHandle.IsNil)
                {
                    return false;
                }

                var definition = reader.GetTypeDefinition(definitionHandle);
                var baseType = GetEntityTypeName(reader, definition.BaseType);
                if (string.IsNullOrWhiteSpace(baseType))
                {
                    return false;
                }

                if (string.Equals(baseType, catchExceptionType, StringComparison.Ordinal))
                {
                    return true;
                }

                currentType = baseType;
            }
        }
        catch (BadImageFormatException)
        {
            return false;
        }

        return false;
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        var value = il[offset++];
        short key;
        if (value == 0xFE)
        {
            key = unchecked((short)(0xFE00 | il[offset++]));
        }
        else
        {
            key = value;
        }

        return OpCodesByValue.TryGetValue(key, out var opCode) ? opCode : default;
    }

    private static int GetOperandSize(OperandType operandType, byte[] il, int operandOffset)
    {
        return operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineField => 4,
            OperandType.InlineI => 4,
            OperandType.InlineMethod => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineString => 4,
            OperandType.InlineTok => 4,
            OperandType.InlineType => 4,
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, operandOffset) * 4),
            _ => 0,
        };
    }

    private static bool IsMetadataTokenOperand(OperandType operandType)
    {
        return operandType is OperandType.InlineField
            or OperandType.InlineMethod
            or OperandType.InlineTok
            or OperandType.InlineType;
    }

    private static bool IsIndirectWrite(OpCode opCode)
    {
        return opCode == OpCodes.Stind_I ||
            opCode == OpCodes.Stind_I1 ||
            opCode == OpCodes.Stind_I2 ||
            opCode == OpCodes.Stind_I4 ||
            opCode == OpCodes.Stind_I8 ||
            opCode == OpCodes.Stind_R4 ||
            opCode == OpCodes.Stind_R8 ||
            opCode == OpCodes.Stind_Ref ||
            opCode == OpCodes.Stobj ||
            opCode == OpCodes.Initobj ||
            opCode == OpCodes.Stelem ||
            opCode == OpCodes.Stelem_I ||
            opCode == OpCodes.Stelem_I1 ||
            opCode == OpCodes.Stelem_I2 ||
            opCode == OpCodes.Stelem_I4 ||
            opCode == OpCodes.Stelem_I8 ||
            opCode == OpCodes.Stelem_R4 ||
            opCode == OpCodes.Stelem_R8 ||
            opCode == OpCodes.Stelem_Ref;
    }

    private static Dictionary<int, StaticFieldFact> BuildStaticFieldFacts(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        var usageByFieldToken = ScanStaticFieldUsage(
            peReader,
            reader,
            fieldDefinitionHandlesBySymbol,
            fieldDefinitionHandlesByExactKey);
        var initializerAssignmentsByFieldToken = AnalyzeStaticFieldInitializerAssignments(
            peReader,
            reader,
            methodDefinitionHandlesByExactKey,
            fieldDefinitionHandlesBySymbol,
            fieldDefinitionHandlesByExactKey,
            knownMethodReturnValues,
            knownMethodReturnValueVisiting);
        var facts = new Dictionary<int, StaticFieldFact>();
        foreach (var handle in reader.FieldDefinitions)
        {
            var definition = reader.GetFieldDefinition(handle);
            if ((definition.Attributes & FieldAttributes.Static) == 0)
            {
                continue;
            }

            var fieldToken = MetadataTokens.GetToken(handle);
            var factKind = StaticFieldFactKind.Unknown;
            if ((definition.Attributes & FieldAttributes.Literal) != 0 ||
                (definition.Attributes & FieldAttributes.HasFieldRVA) != 0)
            {
                factKind = StaticFieldFactKind.Constant;
            }
            else if ((definition.Attributes & FieldAttributes.InitOnly) != 0 &&
                !HasRejectedStaticFieldStorageAttribute(reader, definition) &&
                usageByFieldToken.TryGetValue(fieldToken, out var usage) &&
                !usage.HasAddressExposure &&
                !usage.HasWritesOutsideTypeInitializer &&
                usage.TotalWriteCount == 1 &&
                usage.OwningTypeInitializerWriteCount == 1 &&
                initializerAssignmentsByFieldToken.TryGetValue(fieldToken, out var assignment))
            {
                factKind = assignment.Kind switch
                {
                    StaticFieldInitializerValueKind.Constant => StaticFieldFactKind.Constant,
                    StaticFieldInitializerValueKind.StableIdentity => StaticFieldFactKind.StableIdentity,
                    _ => StaticFieldFactKind.Unknown
                };
                facts[fieldToken] = new StaticFieldFact(GetFieldDefinitionSymbol(reader, handle), factKind, assignment.TrackedValue);
                continue;
            }

            facts[fieldToken] = new StaticFieldFact(GetFieldDefinitionSymbol(reader, handle), factKind, TrackedStackValue.Unknown);
        }

        return facts;
    }

    private static Dictionary<int, StaticFieldUsage> ScanStaticFieldUsage(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey)
    {
        var usageByFieldToken = new Dictionary<int, StaticFieldUsage>();
        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var methodDefinition = reader.GetMethodDefinition(methodHandle);
            if (methodDefinition.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var body = peReader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
            var il = body.GetILBytes();
            if (il is null)
            {
                continue;
            }

            var declaringTypeHandle = methodDefinition.GetDeclaringType();
            var isTypeInitializer = string.Equals(reader.GetString(methodDefinition.Name), ".cctor", StringComparison.Ordinal);
            var offset = 0;
            while (offset < il.Length)
            {
                var opCode = ReadOpCode(il, ref offset);
                var operandOffset = offset;
                var operandSize = GetOperandSize(opCode.OperandType, il, operandOffset);
                var operandToken = operandSize == 4 && IsMetadataTokenOperand(opCode.OperandType)
                    ? BitConverter.ToInt32(il, operandOffset)
                    : (int?)null;
                offset += operandSize;

                if (operandToken is null)
                {
                    continue;
                }

                if (!TryResolveSameAssemblyFieldDefinitionHandle(
                        reader,
                        operandToken.Value,
                        fieldDefinitionHandlesBySymbol,
                        fieldDefinitionHandlesByExactKey,
                        out var fieldHandle))
                {
                    continue;
                }

                var fieldDefinition = reader.GetFieldDefinition(fieldHandle);
                if ((fieldDefinition.Attributes & FieldAttributes.Static) == 0)
                {
                    continue;
                }

                var fieldToken = MetadataTokens.GetToken(fieldHandle);
                usageByFieldToken.TryGetValue(fieldToken, out var usage);
                if (opCode == OpCodes.Ldsflda)
                {
                    usage.HasAddressExposure = true;
                }
                else if (opCode == OpCodes.Stsfld)
                {
                    usage.TotalWriteCount++;
                    if (isTypeInitializer && fieldDefinition.GetDeclaringType().Equals(declaringTypeHandle))
                    {
                        usage.OwningTypeInitializerWriteCount++;
                    }
                    else
                    {
                        usage.HasWritesOutsideTypeInitializer = true;
                    }
                }

                usageByFieldToken[fieldToken] = usage;
            }
        }

        return usageByFieldToken;
    }

    private static bool HasRejectedStaticFieldStorageAttribute(MetadataReader reader, FieldDefinition definition)
    {
        foreach (var customAttributeHandle in definition.GetCustomAttributes())
        {
            var attributeTypeName = TryGetCustomAttributeTypeName(reader, customAttributeHandle);
            if (string.Equals(attributeTypeName, "System.ThreadStaticAttribute", StringComparison.Ordinal) ||
                string.Equals(attributeTypeName, "System.ContextStaticAttribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetCustomAttributeTypeName(MetadataReader reader, CustomAttributeHandle handle)
    {
        try
        {
            var attribute = reader.GetCustomAttribute(handle);
            return attribute.Constructor.Kind switch
            {
                HandleKind.MethodDefinition => GetTypeName(
                    reader,
                    reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()),
                HandleKind.MemberReference => GetMemberReferenceParentName(
                    reader,
                    reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent),
                _ => null,
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Dictionary<int, StaticFieldInitializerValue> AnalyzeStaticFieldInitializerAssignments(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        var assignmentsByFieldToken = new Dictionary<int, StaticFieldInitializerValue>();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            if (!TryGetTypeInitializerHandle(reader, typeHandle, out var typeInitializerHandle))
            {
                continue;
            }

            foreach (var pair in AnalyzeTypeInitializerAssignments(
                peReader,
                reader,
                typeHandle,
                typeInitializerHandle,
                methodDefinitionHandlesByExactKey,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                knownMethodReturnValues,
                knownMethodReturnValueVisiting))
            {
                assignmentsByFieldToken[pair.Key] = pair.Value;
            }
        }

        return assignmentsByFieldToken;
    }

    private static Dictionary<int, StaticFieldInitializerValue> AnalyzeTypeInitializerAssignments(
        PEReader peReader,
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinitionHandle typeInitializerHandle,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodDefinitionHandlesByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        Dictionary<int, TrackedStackValue> knownMethodReturnValues,
        HashSet<int> knownMethodReturnValueVisiting)
    {
        var methodDefinition = reader.GetMethodDefinition(typeInitializerHandle);
        if (methodDefinition.RelativeVirtualAddress == 0)
        {
            return new Dictionary<int, StaticFieldInitializerValue>();
        }

        var body = peReader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
        var il = body.GetILBytes();
        if (il is null || body.ExceptionRegions.Length != 0)
        {
            return new Dictionary<int, StaticFieldInitializerValue>();
        }

        var trackedLocals = new Dictionary<int, StaticFieldInitializerValue>();
        var trackedStack = new List<StaticFieldInitializerValue>();
        var assignmentsByFieldToken = new Dictionary<int, StaticFieldInitializerValue>();
        var offset = 0;
        while (offset < il.Length)
        {
            var instructionOffset = offset;
            var opCode = ReadOpCode(il, ref offset);
            var operandOffset = offset;
            var operandSize = GetOperandSize(opCode.OperandType, il, operandOffset);
            var metadataToken = operandSize == 4 && IsMetadataTokenOperand(opCode.OperandType)
                ? BitConverter.ToInt32(il, operandOffset)
                : (int?)null;
            offset += operandSize;

            if (opCode == OpCodes.Constrained)
            {
                continue;
            }

            if (opCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch ||
                opCode == OpCodes.Throw ||
                opCode == OpCodes.Rethrow)
            {
                return new Dictionary<int, StaticFieldInitializerValue>();
            }

            if (TryGetPushedInt32Constant(opCode, il, operandOffset, out var pushedInt32Constant))
            {
                trackedStack.Add(StaticFieldInitializerValue.FromConstantTracked(TrackedStackValue.FromInt32(pushedInt32Constant)));
                continue;
            }

            if (opCode == OpCodes.Ldstr)
            {
                trackedStack.Add(StaticFieldInitializerValue.Constant);
                continue;
            }

            if (opCode == OpCodes.Ldnull)
            {
                trackedStack.Add(StaticFieldInitializerValue.StableIdentity);
                continue;
            }

            if (TryGetStoreLocalIndex(opCode, il, operandOffset, out var storeLocalIndex))
            {
                trackedLocals[storeLocalIndex] = PopStaticFieldInitializerValue(trackedStack);
                continue;
            }

            if (TryGetLoadLocalIndex(opCode, il, operandOffset, out var loadLocalIndex))
            {
                trackedStack.Add(trackedLocals.TryGetValue(loadLocalIndex, out var localValue)
                    ? localValue
                    : StaticFieldInitializerValue.Unknown);
                continue;
            }

            if (opCode == OpCodes.Dup)
            {
                trackedStack.Add(trackedStack.Count == 0 ? StaticFieldInitializerValue.Unknown : trackedStack[^1]);
                continue;
            }

            if (opCode == OpCodes.Ldsfld)
            {
                trackedStack.Add(TryGetTrackedStaticFieldInitializerValue(
                        reader,
                        metadataToken,
                        assignmentsByFieldToken,
                        fieldDefinitionHandlesBySymbol,
                        fieldDefinitionHandlesByExactKey,
                        out var knownFieldValue)
                    ? knownFieldValue
                    : StaticFieldInitializerValue.Unknown);
                continue;
            }

            if (opCode == OpCodes.Stsfld)
            {
                var assignedValue = PopStaticFieldInitializerValue(trackedStack);
                if (metadataToken is not null &&
                    TryResolveSameAssemblyFieldDefinitionHandle(
                        reader,
                        metadataToken.Value,
                        fieldDefinitionHandlesBySymbol,
                        fieldDefinitionHandlesByExactKey,
                        out var fieldHandle))
                {
                    var fieldDefinition = reader.GetFieldDefinition(fieldHandle);
                    if (fieldDefinition.GetDeclaringType().Equals(declaringTypeHandle))
                    {
                        var fieldToken = MetadataTokens.GetToken(fieldHandle);
                        if (assignmentsByFieldToken.ContainsKey(fieldToken))
                        {
                            assignmentsByFieldToken[fieldToken] = StaticFieldInitializerValue.Unknown;
                        }
                        else
                        {
                            assignmentsByFieldToken[fieldToken] = assignedValue;
                        }
                    }
                }

                continue;
            }

            if (opCode == OpCodes.Newarr)
            {
                PopStaticFieldInitializerValue(trackedStack);
                trackedStack.Add(StaticFieldInitializerValue.Unknown);
                continue;
            }

            if (opCode == OpCodes.Newobj)
            {
                if (metadataToken is not null &&
                    TryGetCallTargetSignature(reader, metadataToken.Value, isObjectConstruction: true, out var constructorSignature))
                {
                    PopStaticFieldInitializerValues(trackedStack, constructorSignature.ParameterTypes.Length);
                    trackedStack.Add(StaticFieldInitializerValue.StableIdentity);
                }
                else
                {
                    trackedStack.Clear();
                    trackedLocals.Clear();
                    trackedStack.Add(StaticFieldInitializerValue.Unknown);
                }

                continue;
            }

            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt)
            {
                if (metadataToken is not null &&
                    TryGetCallTargetSignature(reader, metadataToken.Value, isObjectConstruction: false, out var calledSignature))
                {
                    var argumentValues = PopStaticFieldInitializerValues(trackedStack, calledSignature.ParameterTypes.Length);
                    if (calledSignature.HasReceiver)
                    {
                        PopStaticFieldInitializerValue(trackedStack);
                    }

                    if (!string.Equals(calledSignature.ReturnType, "void", StringComparison.Ordinal))
                    {
                        var calledSymbol = ResolveMethodExactKey(reader, metadataToken.Value);
                        var trackedArgumentValues = argumentValues
                            .Select(static argumentValue => argumentValue.TrackedValue)
                            .ToArray();
                        if (TryGetKnownCallReturnValue(
                                peReader,
                                reader,
                                metadataToken,
                                calledSymbol,
                                trackedArgumentValues,
                                methodDefinitionHandlesByExactKey,
                                fieldDefinitionHandlesBySymbol,
                                fieldDefinitionHandlesByExactKey,
                                EmptyStaticFieldFacts,
                                knownMethodReturnValues,
                                knownMethodReturnValueVisiting,
                                out var knownCallTrackedValue) &&
                            TryCreateStaticFieldInitializerValue(knownCallTrackedValue, out var knownCallInitializerValue))
                        {
                            trackedStack.Add(knownCallInitializerValue);
                        }
                        else if (IsKnownStableIdentityInitializerCall(calledSymbol))
                        {
                            trackedStack.Add(StaticFieldInitializerValue.StableIdentity);
                        }
                        else
                        {
                            trackedStack.Add(StaticFieldInitializerValue.Unknown);
                        }
                    }
                }
                else
                {
                    trackedStack.Clear();
                    trackedLocals.Clear();
                }

                continue;
            }

            if (opCode == OpCodes.Ret)
            {
                trackedStack.Clear();
                trackedLocals.Clear();
                continue;
            }

            if (!TryGetStackPopCount(opCode.StackBehaviourPop, out var popCount) ||
                !TryGetStackPushCount(opCode.StackBehaviourPush, out var pushCount))
            {
                return new Dictionary<int, StaticFieldInitializerValue>();
            }

            PopStaticFieldInitializerValues(trackedStack, popCount);
            for (var i = 0; i < pushCount; i++)
            {
                trackedStack.Add(StaticFieldInitializerValue.Unknown);
            }

            if (ShouldResetTrackedState(opCode))
            {
                trackedStack.Clear();
                trackedLocals.Clear();
            }
        }

        return assignmentsByFieldToken
            .Where(static pair => pair.Value.Kind != StaticFieldInitializerValueKind.Unknown)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
    }

    private static bool TryGetTypeInitializerHandle(
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        out MethodDefinitionHandle methodHandle)
    {
        foreach (var candidateHandle in reader.GetTypeDefinition(declaringTypeHandle).GetMethods())
        {
            if (string.Equals(reader.GetString(reader.GetMethodDefinition(candidateHandle).Name), ".cctor", StringComparison.Ordinal))
            {
                methodHandle = candidateHandle;
                return true;
            }
        }

        methodHandle = default;
        return false;
    }

    private static void AddSameAssemblyStaticFieldToken(
        MetadataReader reader,
        int? operandToken,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        SortedSet<int> sameAssemblyStaticReadFieldTokens)
    {
        if (operandToken is not null &&
            TryResolveSameAssemblyFieldDefinitionHandle(
                reader,
                operandToken.Value,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                out var fieldHandle))
        {
            sameAssemblyStaticReadFieldTokens.Add(MetadataTokens.GetToken(fieldHandle));
        }
    }

    private static void AddField(
        MetadataReader reader,
        int? operandToken,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        SortedSet<string> fields)
    {
        if (operandToken is null)
        {
            return;
        }

        if (TryResolveSameAssemblyFieldDefinitionHandle(
                reader,
                operandToken.Value,
                fieldDefinitionHandlesBySymbol,
                fieldDefinitionHandlesByExactKey,
                out var fieldHandle))
        {
            fields.Add(GetFieldDefinitionSymbol(reader, fieldHandle));
            return;
        }

        fields.Add(ResolveFieldToken(reader, operandToken.Value));
    }

    private static bool TryResolveSameAssemblyFieldDefinitionHandle(
        MetadataReader reader,
        int metadataToken,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldDefinitionHandlesByExactKey,
        out FieldDefinitionHandle handle)
    {
        handle = default;
        var resolvedHandle = MetadataTokens.Handle(metadataToken);
        switch (resolvedHandle.Kind)
        {
            case HandleKind.FieldDefinition:
                handle = (FieldDefinitionHandle)resolvedHandle;
                return true;
            case HandleKind.MemberReference:
                var memberReferenceHandle = (MemberReferenceHandle)resolvedHandle;
                return fieldDefinitionHandlesBySymbol.TryGetValue(GetMemberReferenceSymbol(reader, memberReferenceHandle), out handle) ||
                    fieldDefinitionHandlesByExactKey.TryGetValue(GetMemberReferenceFieldExactKey(reader, memberReferenceHandle), out handle) ||
                    fieldDefinitionHandlesBySymbol.TryGetValue(GetMemberReferenceFieldLookupSymbol(reader, memberReferenceHandle), out handle) ||
                    fieldDefinitionHandlesByExactKey.TryGetValue(GetMemberReferenceFieldLookupExactKey(reader, memberReferenceHandle), out handle);
            default:
                return false;
        }
    }

    private static string ResolveMethodExactKey(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.Handle(token);
        return handle.Kind switch
        {
            HandleKind.MethodDefinition => GetMethodExactKey(reader, (MethodDefinitionHandle)handle),
            HandleKind.MemberReference => GetMemberReferenceExactKey(reader, (MemberReferenceHandle)handle),
            HandleKind.MethodSpecification => ResolveMethodSpecificationExactKey(reader, (MethodSpecificationHandle)handle),
            _ => $"metadata-token:0x{token:X8}",
        };
    }

    private static string ResolveMethodSpecificationExactKey(MetadataReader reader, MethodSpecificationHandle handle)
    {
        var specification = reader.GetMethodSpecification(handle);
        var method = specification.Method;
        return method.Kind switch
        {
            HandleKind.MethodDefinition => GetMethodExactKey(reader, (MethodDefinitionHandle)method),
            HandleKind.MemberReference => GetMemberReferenceExactKey(reader, (MemberReferenceHandle)method),
            _ => $"method-spec:0x{MetadataTokens.GetToken(handle):X8}",
        };
    }

    private static string ResolveFieldToken(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.Handle(token);
        return handle.Kind switch
        {
            HandleKind.FieldDefinition => GetFieldDefinitionSymbol(reader, (FieldDefinitionHandle)handle),
            HandleKind.MemberReference => GetMemberReferenceSymbol(reader, (MemberReferenceHandle)handle),
            _ => $"metadata-token:0x{token:X8}",
        };
    }

    private static string GetFieldExactKey(MetadataReader reader, FieldDefinitionHandle handle)
    {
        var definition = reader.GetFieldDefinition(handle);
        var typeName = NormalizeExactTypeName(GetTypeName(reader, definition.GetDeclaringType()));
        var fieldName = reader.GetString(definition.Name);
        var fieldType = DecodeFieldDefinitionExactType(reader, definition);
        return $"{typeName}.{fieldName}:{fieldType}";
    }

    private static string GetMemberReferenceFieldExactKey(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName = NormalizeExactTypeName(GetMemberReferenceParentName(reader, memberReference.Parent));
        var fieldName = reader.GetString(memberReference.Name);
        var fieldType = DecodeMemberReferenceFieldExactType(reader, memberReference);
        return $"{parentName}.{fieldName}:{fieldType}";
    }

    private static string GetMemberReferenceFieldLookupSymbol(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName = GetMemberReferenceFieldLookupParentName(reader, memberReference.Parent);
        return $"{parentName}.{reader.GetString(memberReference.Name)}";
    }

    private static string GetMemberReferenceFieldLookupExactKey(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName = NormalizeExactTypeName(GetMemberReferenceFieldLookupParentName(reader, memberReference.Parent));
        var fieldName = reader.GetString(memberReference.Name);
        var fieldType = DecodeMemberReferenceFieldExactType(reader, memberReference);
        return $"{parentName}.{fieldName}:{fieldType}";
    }

    private static string GetMethodDisplaySymbol(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var definition = reader.GetMethodDefinition(handle);
        var typeName = GetTypeName(reader, definition.GetDeclaringType());
        var methodName = reader.GetString(definition.Name);
        var signature = DecodeMethodDisplaySignature(reader, definition);
        return $"{typeName}.{methodName}{signature}";
    }

    private static string GetMethodExactKey(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var definition = reader.GetMethodDefinition(handle);
        var typeName = NormalizeExactTypeName(GetTypeName(reader, definition.GetDeclaringType()));
        var methodName = reader.GetString(definition.Name);
        var signature = DecodeMethodExactSignature(reader, definition);
        return $"{typeName}.{methodName}{signature}";
    }

    private static string GetFieldDefinitionSymbol(MetadataReader reader, FieldDefinitionHandle handle)
    {
        var definition = reader.GetFieldDefinition(handle);
        var typeName = GetTypeName(reader, definition.GetDeclaringType());
        return $"{typeName}.{reader.GetString(definition.Name)}";
    }

    private static string DecodeFieldDefinitionExactType(MetadataReader reader, FieldDefinition definition)
    {
        try
        {
            return definition.DecodeSignature(new TypeNameProvider(reader), genericContext: null);
        }
        catch (BadImageFormatException)
        {
            return "?";
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    private static string DecodeMemberReferenceFieldExactType(MetadataReader reader, MemberReference memberReference)
    {
        try
        {
            return memberReference.DecodeFieldSignature(new TypeNameProvider(reader), genericContext: null);
        }
        catch (BadImageFormatException)
        {
            return "?";
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    private static bool ShouldTreatCallvirtAsDynamicDispatch(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.Handle(token);
        return handle.Kind switch
        {
            HandleKind.MethodDefinition => IsVirtualDispatchCandidate(reader, (MethodDefinitionHandle)handle),
            HandleKind.MethodSpecification => IsVirtualDispatchCandidate(reader, (MethodSpecificationHandle)handle),
            HandleKind.MemberReference => IsVirtualDispatchCandidate(reader, (MemberReferenceHandle)handle),
            _ => true,
        };
    }

    private static bool IsVirtualDispatchCandidate(MetadataReader reader, MethodSpecificationHandle handle)
    {
        var specification = reader.GetMethodSpecification(handle);
        return specification.Method.Kind switch
        {
            HandleKind.MethodDefinition => IsVirtualDispatchCandidate(reader, (MethodDefinitionHandle)specification.Method),
            _ => true,
        };
    }

    private static bool IsVirtualDispatchCandidate(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var definition = reader.GetMethodDefinition(handle);
        var attributes = definition.Attributes;
        if ((attributes & System.Reflection.MethodAttributes.Virtual) == 0)
        {
            return false;
        }

        if ((attributes & System.Reflection.MethodAttributes.Final) != 0)
        {
            return false;
        }

        var declaringType = reader.GetTypeDefinition(definition.GetDeclaringType());
        return (declaringType.Attributes & System.Reflection.TypeAttributes.Sealed) == 0;
    }

    private static bool IsVirtualDispatchCandidate(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var runtimeType = TryResolveRuntimeType(reader, memberReference.Parent);
        if (runtimeType == null)
        {
            return true;
        }

        if (runtimeType.IsValueType || runtimeType.IsSealed)
        {
            return false;
        }

        var parameterCount = TryGetMemberReferenceParameterCount(reader, memberReference);
        if (parameterCount == null)
        {
            return true;
        }

        var methodName = reader.GetString(memberReference.Name);
        var candidates = runtimeType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal) &&
                method.GetParameters().Length == parameterCount.Value)
            .ToArray();
        if (candidates.Length == 0)
        {
            return true;
        }

        return candidates.Any(static method => method.IsVirtual && !method.IsFinal && method.DeclaringType?.IsSealed != true);
    }

    private static int? TryGetMemberReferenceParameterCount(MetadataReader reader, MemberReference memberReference)
    {
        try
        {
            return memberReference.DecodeMethodSignature(new TypeNameProvider(reader), genericContext: null).ParameterTypes.Length;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Type? TryResolveRuntimeType(MetadataReader reader, EntityHandle handle)
    {
        var typeName = handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeName(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetTypeReferenceName(reader, (TypeReferenceHandle)handle),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return TryResolveRuntimeType(typeName);
    }

    private static Type? TryResolveRuntimeType(string typeName)
    {
        return RuntimeTypeCache.GetOrAdd(typeName, static fullName =>
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var resolved = assembly.GetType(fullName, throwOnError: false);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            if (fullName.IndexOfAny(new[] { '<', '>', ',', '!', '*' }) >= 0)
            {
                return null;
            }

            try
            {
                return Type.GetType(fullName, throwOnError: false);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (FileLoadException)
            {
                return null;
            }
        });
    }

    private static string GetMemberReferenceSymbol(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName = GetMemberReferenceParentName(reader, memberReference.Parent);
        var name = reader.GetString(memberReference.Name);
        var signature = DecodeMemberReferenceDisplaySignature(reader, memberReference);
        return $"{parentName}.{name}{signature}";
    }

    private static string GetMemberReferenceExactKey(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName = NormalizeExactTypeName(GetMemberReferenceParentName(reader, memberReference.Parent));
        var name = reader.GetString(memberReference.Name);
        var signature = DecodeMemberReferenceExactSignature(reader, memberReference);
        return $"{parentName}.{name}{signature}";
    }

    private static string GetMemberReferenceMethodLookupExactKey(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        var parentName = NormalizeExactTypeName(GetMemberReferenceMethodLookupParentName(reader, memberReference.Parent));
        var name = reader.GetString(memberReference.Name);
        var signature = DecodeMemberReferenceExactSignature(reader, memberReference);
        return $"{parentName}.{name}{signature}";
    }

    private static string GetMemberReferenceParentName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeName(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetTypeReferenceName(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => DecodeTypeSpecification(reader, (TypeSpecificationHandle)handle),
            HandleKind.MethodDefinition => GetMethodDisplaySymbol(reader, (MethodDefinitionHandle)handle),
            HandleKind.ModuleReference => reader.GetString(reader.GetModuleReference((ModuleReferenceHandle)handle).Name),
            _ => $"metadata-parent:0x{MetadataTokens.GetToken(handle):X8}",
        };
    }

    private static string GetMemberReferenceFieldLookupParentName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeSpecification => DecodeTypeSpecificationForFieldLookup(reader, (TypeSpecificationHandle)handle),
            _ => GetMemberReferenceParentName(reader, handle),
        };
    }

    private static string GetMemberReferenceMethodLookupParentName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeSpecification => DecodeTypeSpecificationForMethodLookup(reader, (TypeSpecificationHandle)handle),
            _ => GetMemberReferenceParentName(reader, handle),
        };
    }

    public static string GetTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        if (handle.IsNil)
        {
            return "<module>";
        }

        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaringType = definition.GetDeclaringType();
        if (!declaringType.IsNil)
        {
            return $"{GetTypeName(reader, declaringType)}+{name}";
        }

        var ns = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    public static string GetTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        var ns = reader.GetString(reference.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static string DecodeMethodDisplaySignature(MetadataReader reader, MethodDefinition definition)
    {
        try
        {
            var signature = definition.DecodeSignature(new TypeNameProvider(reader), genericContext: null);
            return $"({string.Join(", ", signature.ParameterTypes)})";
        }
        catch (BadImageFormatException)
        {
            return "(?)";
        }
    }

    private static string DecodeMethodExactSignature(MetadataReader reader, MethodDefinition definition)
    {
        try
        {
            var signature = definition.DecodeSignature(new TypeNameProvider(reader), genericContext: null);
            return $"({string.Join(", ", signature.ParameterTypes)})->{signature.ReturnType}";
        }
        catch (BadImageFormatException)
        {
            return "(?)->?";
        }
    }

    private static string DecodeMemberReferenceDisplaySignature(MetadataReader reader, MemberReference memberReference)
    {
        try
        {
            var signature = memberReference.DecodeMethodSignature(new TypeNameProvider(reader), genericContext: null);
            return $"({string.Join(", ", signature.ParameterTypes)})";
        }
        catch (BadImageFormatException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string DecodeMemberReferenceExactSignature(MetadataReader reader, MemberReference memberReference)
    {
        try
        {
            var signature = memberReference.DecodeMethodSignature(new TypeNameProvider(reader), genericContext: null);
            return $"({string.Join(", ", signature.ParameterTypes)})->{signature.ReturnType}";
        }
        catch (BadImageFormatException)
        {
            return "(?)->?";
        }
        catch (InvalidOperationException)
        {
            return "(?)->?";
        }
    }

    internal static string NormalizeExactTypeName(string typeName)
    {
        return typeName switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.Char" => "char",
            "System.Decimal" => "decimal",
            "System.Double" => "double",
            "System.Int16" => "short",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.IntPtr" => "nint",
            "System.Object" => "object",
            "System.SByte" => "sbyte",
            "System.Single" => "float",
            "System.String" => "string",
            "System.UInt16" => "ushort",
            "System.UInt32" => "uint",
            "System.UInt64" => "ulong",
            "System.UIntPtr" => "nuint",
            "System.Void" => "void",
            _ => typeName
        };
    }

    private static string DecodeTypeSpecification(MetadataReader reader, TypeSpecificationHandle handle)
    {
        try
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(new TypeNameProvider(reader), genericContext: null);
        }
        catch (BadImageFormatException)
        {
            return "type-spec";
        }
    }

    private static string DecodeTypeSpecificationForFieldLookup(MetadataReader reader, TypeSpecificationHandle handle)
    {
        try
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(
                new TypeNameProvider(reader, eraseGenericInstantiationsForLookup: true),
                genericContext: null);
        }
        catch (BadImageFormatException)
        {
            return DecodeTypeSpecification(reader, handle);
        }
        catch (InvalidOperationException)
        {
            return DecodeTypeSpecification(reader, handle);
        }
    }

    private static string DecodeTypeSpecificationForMethodLookup(MetadataReader reader, TypeSpecificationHandle handle)
    {
        try
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(
                new TypeNameProvider(reader, eraseGenericInstantiationsForLookup: true),
                genericContext: null);
        }
        catch (BadImageFormatException)
        {
            return DecodeTypeSpecification(reader, handle);
        }
        catch (InvalidOperationException)
        {
            return DecodeTypeSpecification(reader, handle);
        }
    }

    internal readonly record struct KnownThrownExceptionSite(int InstructionOffset, string ExceptionType);
}

internal sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    private readonly bool eraseGenericInstantiationsForLookup;

    public TypeNameProvider(MetadataReader reader, bool eraseGenericInstantiationsForLookup = false)
    {
        this.eraseGenericInstantiationsForLookup = eraseGenericInstantiationsForLookup;
    }

    public string GetArrayType(string elementType, ArrayShape shape)
    {
        var rank = Math.Max(shape.Rank, 1);
        return $"{elementType}[{new string(',', rank - 1)}]";
    }

    public string GetByReferenceType(string elementType)
    {
        return $"ref {elementType}";
    }

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        return "delegate*";
    }

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
    {
        if (eraseGenericInstantiationsForLookup)
        {
            return genericType;
        }

        return $"{genericType}<{string.Join(", ", typeArguments)}>";
    }

    public string GetGenericMethodParameter(object? genericContext, int index)
    {
        return $"!!{index}";
    }

    public string GetGenericTypeParameter(object? genericContext, int index)
    {
        return $"!{index}";
    }

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
    {
        return unmodifiedType;
    }

    public string GetPinnedType(string elementType)
    {
        return elementType;
    }

    public string GetPointerType(string elementType)
    {
        return $"{elementType}*";
    }

    public string GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        return typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.TypedReference => "typedref",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.UIntPtr => "nuint",
            PrimitiveTypeCode.Void => "void",
            _ => typeCode.ToString(),
        };
    }

    public string GetSZArrayType(string elementType)
    {
        return $"{elementType}[]";
    }

    public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        return AssemblyEffectSummarizer.NormalizeExactTypeName(AssemblyEffectSummarizer.GetTypeName(metadataReader, handle));
    }

    public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        return AssemblyEffectSummarizer.NormalizeExactTypeName(AssemblyEffectSummarizer.GetTypeReferenceName(metadataReader, handle));
    }

    public string GetTypeFromSpecification(
        MetadataReader metadataReader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
    {
        return metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}

internal sealed record EffectSummaryDocument(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    AssemblyEffectReport[] Assemblies,
    PurityClassificationReport? PurityReport,
    GeneratedPurityCatalogDocument? GeneratedPurityCatalog,
    BclFallbackInventoryReport? BclFallbackInventory);

internal sealed record AssemblyEffectReport(
    string AssemblyName,
    string AssemblyPath,
    string AssemblySha256,
    string ModuleVersionId,
    int MethodCount,
    int EmittedMethodCount,
    MethodEffectSummary[] Methods)
{
    [JsonIgnore]
    public MethodEffectSummary[] ClassificationMethods { get; init; } = Array.Empty<MethodEffectSummary>();
}

internal sealed record MethodEffectSummary(
    string Symbol,
    string ExactSymbolKey,
    string MetadataToken,
    int RelativeVirtualAddress,
    string? MethodBodySha256,
    string CacheKey,
    string[] Effects,
    string[] RootCandidates,
    string[] TransitiveRootCandidates,
    string[] ThrownExceptionTypes,
    string[] TransitiveThrownExceptionTypes,
    ExceptionSourcePath[] ThrownExceptionSourcePaths,
    ExceptionSourcePath[] TransitiveThrownExceptionSourcePaths,
    string[] Calls,
    string[] Fields)
{
    public CallSiteSummary[] CallSites { get; init; } = Array.Empty<CallSiteSummary>();

    public ThrownExceptionEdgeSummary[] TransitiveThrownExceptionEdges { get; init; } = Array.Empty<ThrownExceptionEdgeSummary>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TransitiveThrownExceptionEdgesTruncated { get; init; }

    public MethodPurityClassification? PurityClassification { get; init; }

    [JsonIgnore]
    public ExceptionPropagationSite[] ExceptionPropagationSites { get; init; } = Array.Empty<ExceptionPropagationSite>();

    [JsonIgnore]
    public bool IsStatic { get; init; }
}

internal sealed record ExceptionSourcePath(
    string ExceptionType,
    string SourcePath);

internal sealed record ExceptionPropagationSite(
    string ExactSymbolKey,
    int InstructionOffset,
    string[] HandlingCatchExceptionTypes,
    bool IsShadowedByDefinitelyThrowingFinally);

internal sealed record ThrownExceptionEdgeSummary(
    string ExceptionType,
    string SourcePath,
    string? CalleeExactSymbolKey,
    int Depth);

internal readonly record struct ThrownExceptionTraversalResult(
    ThrownExceptionEdgeSummary[] Result,
    bool DependsOnCycle,
    bool IsTruncated);

internal sealed record CallSiteSummary(string ExactSymbolKey)
{
    public bool UsesDynamicDispatch { get; init; }

    public CallSiteArgumentEvidence[] ArgumentEvidence { get; init; } = Array.Empty<CallSiteArgumentEvidence>();
}

internal sealed record CallSiteArgumentEvidence(
    string Target,
    int? ParameterIndex,
    string Type,
    string Value);

internal readonly record struct CallTargetSignature(
    bool HasReceiver,
    string[] ParameterTypes,
    string ReturnType);

internal sealed record BranchTrackedState(
    List<TrackedStackValue> Stack,
    Dictionary<int, TrackedStackValue> Locals);

internal enum StaticFieldFactKind
{
    Unknown,
    Constant,
    StableIdentity
}

internal readonly record struct StaticFieldFact(
    string Symbol,
    StaticFieldFactKind Kind,
    TrackedStackValue TrackedValue);

internal struct StaticFieldUsage
{
    public int TotalWriteCount;

    public int OwningTypeInitializerWriteCount;

    public bool HasWritesOutsideTypeInitializer;

    public bool HasAddressExposure;
}

internal enum StaticFieldInitializerValueKind
{
    Unknown,
    Constant,
    StableIdentity
}

internal readonly record struct StaticFieldInitializerValue(
    StaticFieldInitializerValueKind Kind,
    TrackedStackValue TrackedValue)
{
    public static StaticFieldInitializerValue Unknown =>
        new(StaticFieldInitializerValueKind.Unknown, TrackedStackValue.Unknown);

    public static StaticFieldInitializerValue Constant =>
        new(StaticFieldInitializerValueKind.Constant, TrackedStackValue.Unknown);

    public static StaticFieldInitializerValue StableIdentity =>
        new(StaticFieldInitializerValueKind.StableIdentity, TrackedStackValue.Unknown);

    public static StaticFieldInitializerValue FromConstantTracked(TrackedStackValue trackedValue) =>
        new(StaticFieldInitializerValueKind.Constant, trackedValue);

    public static StaticFieldInitializerValue FromStableIdentityTracked(TrackedStackValue trackedValue) =>
        new(StaticFieldInitializerValueKind.StableIdentity, trackedValue);
}

internal readonly record struct TrackedStackValue(int? Int32Constant, string? KnownStringComparer, string? KnownExceptionType)
{
    public static TrackedStackValue Unknown => default;

    public bool IsUnknown =>
        Int32Constant is null &&
        string.IsNullOrWhiteSpace(KnownStringComparer) &&
        string.IsNullOrWhiteSpace(KnownExceptionType);

    public static TrackedStackValue FromInt32(int value) => new(value, null, null);

    public static TrackedStackValue FromKnownStringComparer(string value) => new(null, value, null);

    public static TrackedStackValue FromKnownExceptionType(string value) => new(null, null, value);
}
