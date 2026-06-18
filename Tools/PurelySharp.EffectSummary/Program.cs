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
        var document = ArtifactSpecDocument.Load(artifactSpecPath);
        foreach (var artifact in document.Artifacts)
        {
            var options = CliOptions.FromArtifactSpec(document.Defaults, artifact);
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
        var assemblies = options.AssemblyPaths.Count == 0
            ? new[] { RuntimeAssemblyResolver.Resolve(options.Framework, options.RuntimeAssemblyName) }
            : options.AssemblyPaths.Select(Path.GetFullPath).ToArray();

        var reports = assemblies
            .Select(path => AssemblyEffectSummarizer.Summarize(
                path,
                options.Limit,
                options.SymbolPrefixes,
                options.ExactSymbols,
                options.ExactSymbolKeys,
                options.IncludeCallees,
                options.MaxDepth,
                options.IncludeTransitiveRoots))
            .ToArray();

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

        var document = new EffectSummaryDocument(
            SchemaVersion: purityClassificationReport == null ? 1 : 3,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Assemblies: reports,
            PurityReport: purityClassificationReport,
            GeneratedPurityCatalog: generatedPurityCatalog);

        return document;
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
        Console.Error.WriteLine("PurelySharp.EffectSummary");
        Console.Error.WriteLine("Summarizes IL effects from .NET assemblies for evidence-based purity catalog work.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet run --project Tools/PurelySharp.EffectSummary -- [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --assembly <path>          Assembly to summarize. Can be repeated.");
        Console.Error.WriteLine("  --artifact-spec <path>     Generate one or more output files from a JSON artifact spec.");
        Console.Error.WriteLine("  --framework <net8.0>       Runtime framework to inspect when --assembly is omitted.");
        Console.Error.WriteLine("  --runtime-assembly <name>  Runtime assembly name when --assembly is omitted. Default: System.Private.CoreLib.dll");
        Console.Error.WriteLine("  --symbol-prefix <prefix>   Emit only methods whose decoded symbol starts with this prefix. Can be repeated.");
        Console.Error.WriteLine("  --include-callees          Also emit same-assembly callees reachable from matched symbols.");
        Console.Error.WriteLine("  --max-depth <count>        Maximum same-assembly callee depth when --include-callees is used. Default: 1.");
        Console.Error.WriteLine("  --transitive-roots         Propagate root candidate labels through same-assembly calls.");
        Console.Error.WriteLine("  --classify-purity         Add report-only fixed-point purity classifications to the JSON output.");
        Console.Error.WriteLine("  --compare-manual-catalogs Compare emitted methods against the current reviewed manual catalogs.");
        Console.Error.WriteLine("  --output <path>            Write JSON to a file instead of stdout.");
        Console.Error.WriteLine("  --limit <count>            Limit emitted method summaries for smoke testing.");
        Console.Error.WriteLine("  --help                     Show this help.");
    }
}

internal sealed class CliOptions
{
    public List<string> AssemblyPaths { get; } = new();

    public List<string> SymbolPrefixes { get; } = new();

    public List<string> ExactSymbols { get; } = new();

    public List<string> ExactSymbolKeys { get; } = new();

    public string? ArtifactSpecPath { get; private set; }

    public string Framework { get; private set; } = "net8.0";

    public string RuntimeAssemblyName { get; private set; } = "System.Private.CoreLib.dll";

    public string? OutputPath { get; private set; }

    public int? Limit { get; private set; }

    public bool IncludeCallees { get; private set; }

    public int MaxDepth { get; private set; } = 1;

    public bool IncludeTransitiveRoots { get; private set; }

    public bool IncludePurityClassification { get; private set; }

    public bool CompareManualCatalogs { get; private set; }

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
                case "--symbol-prefix":
                    options.SymbolPrefixes.Add(ReadRequiredValue(args, ref i, arg));
                    break;
                case "--include-callees":
                    options.IncludeCallees = true;
                    break;
                case "--max-depth":
                    options.MaxDepth = int.Parse(ReadRequiredValue(args, ref i, arg));
                    break;
                case "--transitive-roots":
                    options.IncludeTransitiveRoots = true;
                    break;
                case "--classify-purity":
                    options.IncludePurityClassification = true;
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

        return options;
    }

    public static CliOptions FromArtifactSpec(ArtifactSpecDefaults? defaults, ArtifactSpecEntry artifact)
    {
        var options = new CliOptions
        {
            Framework = artifact.Framework ?? defaults?.Framework ?? "net8.0",
            RuntimeAssemblyName = artifact.RuntimeAssemblyName ?? defaults?.RuntimeAssemblyName ?? "System.Private.CoreLib.dll",
            OutputPath = artifact.OutputPath,
            Limit = artifact.Limit ?? defaults?.Limit,
            IncludeCallees = artifact.IncludeCallees ?? defaults?.IncludeCallees ?? false,
            MaxDepth = artifact.MaxDepth ?? defaults?.MaxDepth ?? 1,
            IncludeTransitiveRoots = artifact.IncludeTransitiveRoots ?? defaults?.IncludeTransitiveRoots ?? false,
            IncludePurityClassification = artifact.IncludePurityClassification ?? defaults?.IncludePurityClassification ?? false,
            CompareManualCatalogs = artifact.CompareManualCatalogs ?? defaults?.CompareManualCatalogs ?? false,
        };

        if (artifact.AssemblyPaths != null)
        {
            options.AssemblyPaths.AddRange(artifact.AssemblyPaths);
        }

        if (artifact.SymbolPrefixes != null)
        {
            options.SymbolPrefixes.AddRange(artifact.SymbolPrefixes);
        }

        if (!string.IsNullOrWhiteSpace(artifact.SourceSummaryPath))
        {
            var sourceSymbols = ArtifactSpecSymbolSource.LoadSymbols(artifact.SourceSummaryPath!);
            options.ExactSymbols.AddRange(sourceSymbols.Symbols);
            options.ExactSymbolKeys.AddRange(sourceSymbols.ExactSymbolKeys);
        }

        if (options.CompareManualCatalogs)
        {
            options.IncludePurityClassification = true;
        }

        return options;
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

    public bool? IncludeTransitiveRoots { get; set; }

    public bool? IncludePurityClassification { get; set; }

    public bool? CompareManualCatalogs { get; set; }
}

internal sealed class ArtifactSpecEntry
{
    public string? OutputPath { get; set; }

    public string? SourceSummaryPath { get; set; }

    public string? Framework { get; set; }

    public string? RuntimeAssemblyName { get; set; }

    public string[]? AssemblyPaths { get; set; }

    public string[]? SymbolPrefixes { get; set; }

    public int? Limit { get; set; }

    public bool? IncludeCallees { get; set; }

    public int? MaxDepth { get; set; }

    public bool? IncludeTransitiveRoots { get; set; }

    public bool? IncludePurityClassification { get; set; }

    public bool? CompareManualCatalogs { get; set; }
}

internal static class ArtifactSpecSymbolSource
{
    public static ArtifactSpecSymbolSet LoadSymbols(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var exactSymbolKeys = new HashSet<string>(StringComparer.Ordinal);

        if (document.RootElement.TryGetProperty("GeneratedPurityCatalog", out var generatedPurityCatalog) &&
            generatedPurityCatalog.ValueKind == JsonValueKind.Object &&
            generatedPurityCatalog.TryGetProperty("Entries", out var entriesElement) &&
            entriesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entryElement in entriesElement.EnumerateArray())
            {
                var symbol = GetTrimmedStringProperty(entryElement, "Symbol");
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    symbols.Add(symbol);
                }

                var exactSymbolKey = GetTrimmedStringProperty(entryElement, "ExactSymbolKey");
                if (!string.IsNullOrWhiteSpace(exactSymbolKey))
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
                    if (!string.IsNullOrWhiteSpace(symbol))
                    {
                        symbols.Add(symbol);
                    }

                    var exactSymbolKey = GetTrimmedStringProperty(methodElement, "ExactSymbolKey");
                    if (!string.IsNullOrWhiteSpace(exactSymbolKey))
                    {
                        exactSymbolKeys.Add(exactSymbolKey);
                    }
                }
            }
        }

        if (symbols.Count == 0)
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
}

internal sealed record ArtifactSpecSymbolSet(
    string[] Symbols,
    string[] ExactSymbolKeys);

internal static class RuntimeAssemblyResolver
{
    public static string Resolve(string framework, string assemblyName)
    {
        var major = ParseMajorFrameworkVersion(framework);
        var runtimeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "shared",
            "Microsoft.NETCore.App");

        if (!Directory.Exists(runtimeRoot))
        {
            throw new DirectoryNotFoundException($"Runtime root not found: {runtimeRoot}");
        }

        var versionDirectory = Directory
            .EnumerateDirectories(runtimeRoot)
            .Select(path => (Path: path, Version: TryParseVersion(Path.GetFileName(path))))
            .Where(item => item.Version is not null && item.Version.Major == major)
            .OrderByDescending(item => item.Version)
            .Select(item => item.Path)
            .FirstOrDefault();

        if (versionDirectory is null)
        {
            throw new DirectoryNotFoundException($"No Microsoft.NETCore.App runtime found for {framework} under {runtimeRoot}.");
        }

        var assemblyPath = Path.Combine(versionDirectory, assemblyName);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Runtime assembly not found: {assemblyPath}", assemblyPath);
        }

        return assemblyPath;
    }

    private static int ParseMajorFrameworkVersion(string framework)
    {
        if (!framework.StartsWith("net", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported framework moniker '{framework}'. Expected netX.Y.");
        }

        var digits = new string(framework.Skip(3).TakeWhile(char.IsDigit).ToArray());
        return int.Parse(digits);
    }

    private static Version? TryParseVersion(string text)
    {
        return Version.TryParse(text, out var version) ? version : null;
    }
}

internal static class AssemblyEffectSummarizer
{
    private static readonly ConcurrentDictionary<string, Type?> RuntimeTypeCache =
        new ConcurrentDictionary<string, Type?>(StringComparer.Ordinal);

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
        bool includeTransitiveRoots)
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

        var allSummaries = new List<MethodEffectSummary>();
        foreach (var handle in reader.MethodDefinitions)
        {
            allSummaries.Add(SummarizeMethod(peReader, reader, handle, moduleVersionId));
        }

        if (includeTransitiveRoots)
        {
            allSummaries = AddTransitiveRootCandidates(allSummaries);
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
            Methods: summaries);
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
        var queue = new Queue<(string ExactSymbolKey, int Depth)>();
        foreach (var summary in allSummaries.Where(summary => MatchesSymbolPrefix(summary.Symbol, symbolPrefixes)))
        {
            if (included.Add(summary.ExactSymbolKey))
            {
                queue.Enqueue((summary.ExactSymbolKey, 0));
            }
        }

        while (queue.Count > 0)
        {
            var (exactSymbolKey, depth) = queue.Dequeue();
            if (depth >= maxDepth || !bySymbol.TryGetValue(exactSymbolKey, out var summary))
            {
                continue;
            }

            foreach (var call in summary.Calls)
            {
                if (bySymbol.ContainsKey(call) && included.Add(call))
                {
                    queue.Enqueue((call, depth + 1));
                }
            }
        }

        return allSummaries.Where(summary => included.Contains(summary.ExactSymbolKey));
    }

    private static MethodEffectSummary SummarizeMethod(
        PEReader peReader,
        MetadataReader reader,
        MethodDefinitionHandle handle,
        string moduleVersionId)
    {
        var definition = reader.GetMethodDefinition(handle);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var calls = new SortedSet<string>(StringComparer.Ordinal);
        var fields = new SortedSet<string>(StringComparer.Ordinal);
        var staticFields = new SortedSet<string>(StringComparer.Ordinal);
        var thrownExceptionTypes = new SortedSet<string>(StringComparer.Ordinal);
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
                AnalyzeIl(reader, il, body.ExceptionRegions, effects, calls, fields, staticFields, thrownExceptionTypes);
            }
        }

        var metadataToken = $"0x{MetadataTokens.GetToken(handle):X8}";
        var cacheKey = $"mvid:{moduleVersionId}|token:{metadataToken}|il:{methodBodySha256 ?? "no-il"}";
        var isConstructor = string.Equals(reader.GetString(definition.Name), ".ctor", StringComparison.Ordinal);
        return new MethodEffectSummary(
            Symbol: GetMethodDisplaySymbol(reader, handle),
            ExactSymbolKey: GetMethodExactKey(reader, handle),
            MetadataToken: metadataToken,
            RelativeVirtualAddress: definition.RelativeVirtualAddress,
            MethodBodySha256: methodBodySha256,
            CacheKey: cacheKey,
            Effects: effects.ToArray(),
            RootCandidates: GetRootCandidates(effects, calls, staticFields, isConstructor).ToArray(),
            TransitiveRootCandidates: Array.Empty<string>(),
            ThrownExceptionTypes: thrownExceptionTypes.ToArray(),
            TransitiveThrownExceptionTypes: Array.Empty<string>(),
            Calls: calls.ToArray(),
            Fields: fields.ToArray());
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

    private static List<MethodEffectSummary> AddTransitiveRootCandidates(IReadOnlyList<MethodEffectSummary> summaries)
    {
        var bySymbol = summaries
            .GroupBy(summary => summary.ExactSymbolKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var rootMemo = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var rootVisiting = new HashSet<string>(StringComparer.Ordinal);
        var exceptionMemo = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var exceptionVisiting = new HashSet<string>(StringComparer.Ordinal);

        return summaries
            .Select(summary => summary with
            {
                TransitiveRootCandidates = VisitRootCandidates(summary.ExactSymbolKey, bySymbol, rootMemo, rootVisiting),
                TransitiveThrownExceptionTypes = VisitThrownExceptionTypes(summary.ExactSymbolKey, bySymbol, exceptionMemo, exceptionVisiting)
            })
            .ToList();
    }

    private static string[] VisitRootCandidates(
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        Dictionary<string, string[]> memo,
        HashSet<string> visiting)
    {
        if (memo.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        if (!bySymbol.TryGetValue(symbol, out var summary))
        {
            return Array.Empty<string>();
        }

        var roots = new SortedSet<string>(summary.RootCandidates, StringComparer.Ordinal);
        if (!visiting.Add(symbol))
        {
            return roots.ToArray();
        }

        foreach (var call in summary.Calls)
        {
            if (bySymbol.ContainsKey(call))
            {
                roots.UnionWith(VisitRootCandidates(call, bySymbol, memo, visiting));
            }
        }

        visiting.Remove(symbol);
        var result = roots.ToArray();
        memo[symbol] = result;
        return result;
    }

    private static string[] VisitThrownExceptionTypes(
        string symbol,
        IReadOnlyDictionary<string, MethodEffectSummary> bySymbol,
        Dictionary<string, string[]> memo,
        HashSet<string> visiting)
    {
        if (memo.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        if (!bySymbol.TryGetValue(symbol, out var summary))
        {
            return Array.Empty<string>();
        }

        var thrownTypes = new SortedSet<string>(summary.ThrownExceptionTypes, StringComparer.Ordinal);
        if (!visiting.Add(symbol))
        {
            return thrownTypes.ToArray();
        }

        foreach (var call in summary.Calls)
        {
            if (bySymbol.ContainsKey(call))
            {
                thrownTypes.UnionWith(VisitThrownExceptionTypes(call, bySymbol, memo, visiting));
            }
        }

        visiting.Remove(symbol);
        var result = thrownTypes.ToArray();
        memo[symbol] = result;
        return result;
    }

    private static IEnumerable<string> GetRootCandidates(
        IEnumerable<string> effects,
        IEnumerable<string> calls,
        IEnumerable<string> staticFields,
        bool isConstructor)
    {
        var roots = new SortedSet<string>(StringComparer.Ordinal);
        var effectSet = new HashSet<string>(effects, StringComparer.Ordinal);
        var callSet = new HashSet<string>(calls, StringComparer.Ordinal);
        var staticFieldSet = new HashSet<string>(staticFields, StringComparer.Ordinal);
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
                    if (IsSafeStaticCacheRead(staticFieldSet, callSet))
                    {
                        roots.Add("safe_static_cache_read");
                    }
                    else if (IsSafeStaticConstantRead(staticFieldSet))
                    {
                        roots.Add("safe_static_constant_read");
                    }
                    else
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

    private static bool IsSafeStaticCacheRead(IReadOnlySet<string> fields, IReadOnlySet<string> calls)
    {
        if (fields.Count == 0)
        {
            return false;
        }

        if (fields.All(static field =>
            field.StartsWith("System.Array+EmptyArray`1", StringComparison.Ordinal) &&
            field.EndsWith(".Value", StringComparison.Ordinal)))
        {
            return true;
        }

        if (fields.All(static field =>
            string.Equals(field, "System.StringComparer._ordinal", StringComparison.Ordinal) ||
            string.Equals(field, "System.StringComparer._ordinalIgnoreCase", StringComparison.Ordinal) ||
            string.Equals(field, "System.StringComparer._invariantCulture", StringComparison.Ordinal) ||
            string.Equals(field, "System.StringComparer._invariantCultureIgnoreCase", StringComparison.Ordinal) ||
            string.Equals(field, "System.Threading.Tasks.Task.s_cachedCompleted", StringComparison.Ordinal) ||
            string.Equals(field, "System.Globalization.CultureInfo.s_InvariantCultureInfo", StringComparison.Ordinal) ||
            string.Equals(field, "System.OrdinalCaseSensitiveComparer.Instance", StringComparison.Ordinal) ||
            string.Equals(field, "System.OrdinalIgnoreCaseComparer.Instance", StringComparison.Ordinal) ||
            string.Equals(field, "System.CultureAwareComparer.InvariantCaseSensitiveInstance", StringComparison.Ordinal) ||
            string.Equals(field, "System.CultureAwareComparer.InvariantIgnoreCaseInstance", StringComparison.Ordinal) ||
            string.Equals(field, "System.String.Empty", StringComparison.Ordinal) ||
            string.Equals(field, "System.UriHelper.Unreserved", StringComparison.Ordinal) ||
            string.Equals(field, "System.Globalization.TextInfo.Invariant", StringComparison.Ordinal) ||
            string.Equals(field, "System.Globalization.CompareInfo.Invariant", StringComparison.Ordinal)))
        {
            return true;
        }

        if (fields.All(static field =>
            (field.StartsWith("System.Collections.Generic.Comparer`1", StringComparison.Ordinal) ||
             field.StartsWith("System.Collections.Generic.EqualityComparer`1", StringComparison.Ordinal)) &&
            field.EndsWith(".<Default>k__BackingField", StringComparison.Ordinal)))
        {
            return true;
        }

        return calls.Count == 1 && calls.Any(static call =>
            call.StartsWith("System.ReadOnlySpan`1<byte>..ctor(void*, int)", StringComparison.Ordinal));
    }

    private static bool IsSafeStaticConstantRead(IReadOnlySet<string> fields)
    {
        return fields.Count > 0 && fields.All(static field =>
            string.Equals(field, "IsLittleEndian", StringComparison.Ordinal) ||
            string.Equals(field, "System.BitConverter.IsLittleEndian", StringComparison.Ordinal));
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

        return calls.All(IsPurityNeutralIntrinsicHelperCall);
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

    private static bool IsPurityNeutralIntrinsicHelperCall(string callSymbol)
    {
        return callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.As(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.Add(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.BitCast(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.ReadUnaligned(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.CompilerServices.Unsafe.WriteUnaligned(", StringComparison.Ordinal) ||
            callSymbol.StartsWith("string.GetRawStringData()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("string.get_Length()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Span`1<", StringComparison.Ordinal) && callSymbol.Contains(".get_Length()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) && callSymbol.Contains(".get_Length()", StringComparison.Ordinal) ||
            callSymbol.StartsWith("System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences(", StringComparison.Ordinal);
    }

    private static bool IsFreshObjectInitializationHelperCall(string callSymbol)
    {
        return IsPurityNeutralIntrinsicHelperCall(callSymbol) ||
            callSymbol.Contains(".ctor(", StringComparison.Ordinal);
    }

    private static void AnalyzeIl(
        MetadataReader reader,
        byte[] il,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        SortedSet<string> effects,
        SortedSet<string> calls,
        SortedSet<string> fields,
        SortedSet<string> staticFields,
        SortedSet<string> thrownExceptionTypes)
    {
        var offset = 0;
        string? lastConstructedExceptionType = null;
        var localExceptionTypes = new Dictionary<int, string?>();
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
                string? calledSymbol = null;
                if (opCode == OpCodes.Newobj)
                {
                    effects.Add("allocates_object");
                }
                else
                {
                    effects.Add("calls_method");
                }

                if (opCode == OpCodes.Callvirt &&
                    !suppressDynamicDispatchForNextCallvirt &&
                    operandToken is not null &&
                    ShouldTreatCallvirtAsDynamicDispatch(reader, operandToken.Value))
                {
                    effects.Add("virtual_call");
                }

                if (operandToken is not null)
                {
                    calledSymbol = ResolveMethodExactKey(reader, operandToken.Value);
                    calls.Add(calledSymbol);
                }

                if (opCode == OpCodes.Newobj)
                {
                    lastConstructedExceptionType = TryGetConstructedExceptionType(calledSymbol);
                }
            }
            else if (TryGetStoreLocalIndex(opCode, il, operandOffset, out var storeLocalIndex))
            {
                if (lastConstructedExceptionType == null)
                {
                    localExceptionTypes.Remove(storeLocalIndex);
                }
                else
                {
                    localExceptionTypes[storeLocalIndex] = lastConstructedExceptionType;
                }
            }
            else if (TryGetLoadLocalIndex(opCode, il, operandOffset, out var loadLocalIndex))
            {
                lastConstructedExceptionType = localExceptionTypes.TryGetValue(loadLocalIndex, out var localExceptionType)
                    ? localExceptionType
                    : null;
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
                AddField(reader, operandToken, fields);
            }
            else if (opCode == OpCodes.Ldsfld || opCode == OpCodes.Ldsflda)
            {
                effects.Add("reads_static_field");
                AddField(reader, operandToken, fields);
                AddField(reader, operandToken, staticFields);
            }
            else if (opCode == OpCodes.Stfld)
            {
                effects.Add("writes_instance_field");
                AddField(reader, operandToken, fields);
            }
            else if (opCode == OpCodes.Stsfld)
            {
                effects.Add("writes_static_field");
                AddField(reader, operandToken, fields);
                AddField(reader, operandToken, staticFields);
            }
            else if (opCode == OpCodes.Throw || opCode == OpCodes.Rethrow)
            {
                effects.Add("throws");
                var thrownExceptionType = opCode == OpCodes.Rethrow
                    ? GetEnclosingCatchExceptionType(reader, exceptionRegions, instructionOffset)
                    : lastConstructedExceptionType;
                if (thrownExceptionType != null &&
                    IsEscapingThrow(reader, exceptionRegions, instructionOffset, thrownExceptionType))
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
                break;
            }

            if (opCode != OpCodes.Newobj &&
                opCode != OpCodes.Dup &&
                !IsLoadLocal(opCode))
            {
                lastConstructedExceptionType = null;
            }

            suppressDynamicDispatchForNextCallvirt = false;
        }
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

    private static bool IsLoadLocal(OpCode opCode)
    {
        return opCode == OpCodes.Ldloc_0 ||
            opCode == OpCodes.Ldloc_1 ||
            opCode == OpCodes.Ldloc_2 ||
            opCode == OpCodes.Ldloc_3 ||
            opCode == OpCodes.Ldloc_S ||
            opCode == OpCodes.Ldloc;
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

    private static bool IsEscapingThrow(
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset,
        string thrownExceptionType)
    {
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

    private static string? GetEnclosingCatchExceptionType(
        MetadataReader reader,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int instructionOffset)
    {
        string? catchExceptionType = null;
        var smallestHandlerLength = int.MaxValue;
        foreach (var exceptionRegion in exceptionRegions)
        {
            if (exceptionRegion.Kind != ExceptionRegionKind.Catch ||
                !ContainsOffset(exceptionRegion.HandlerOffset, exceptionRegion.HandlerLength, instructionOffset))
            {
                continue;
            }

            var currentCatchExceptionType = GetCatchExceptionType(reader, exceptionRegion);
            if (currentCatchExceptionType == null || exceptionRegion.HandlerLength >= smallestHandlerLength)
            {
                continue;
            }

            catchExceptionType = currentCatchExceptionType;
            smallestHandlerLength = exceptionRegion.HandlerLength;
        }

        return catchExceptionType;
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

    private static void AddField(MetadataReader reader, int? operandToken, SortedSet<string> fields)
    {
        if (operandToken is not null)
        {
            fields.Add(ResolveFieldToken(reader, operandToken.Value));
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
}

internal sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataReader reader;

    public TypeNameProvider(MetadataReader reader)
    {
        this.reader = reader;
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
    GeneratedPurityCatalogDocument? GeneratedPurityCatalog);

internal sealed record AssemblyEffectReport(
    string AssemblyName,
    string AssemblyPath,
    string AssemblySha256,
    string ModuleVersionId,
    int MethodCount,
    int EmittedMethodCount,
    MethodEffectSummary[] Methods);

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
    string[] Calls,
    string[] Fields)
{
    public MethodPurityClassification? PurityClassification { get; init; }
}
