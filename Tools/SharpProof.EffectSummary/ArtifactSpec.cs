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
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException($"Failed to deserialize artifact spec '{path}'.");

        if (document.SchemaVersion != 1)
            throw new InvalidOperationException(
                $"Unsupported artifact spec schema version '{document.SchemaVersion}' in '{path}'.");

        if (document.Artifacts.Length == 0)
            throw new InvalidOperationException($"Artifact spec '{path}' does not contain any artifacts.");

        return document;
    }
}

internal sealed class ArtifactSpecProgressDocument
{
    public int SchemaVersion { get; set; }

    public string ArtifactSpecSha256 { get; set; } = string.Empty;

    public string[] CompletedOutputPaths { get; set; } = Array.Empty<string>();
}

internal sealed class ShardedEffectSummaryProgressDocument
{
    public int SchemaVersion { get; set; }

    public string ToolModuleVersionId { get; set; } = string.Empty;

    public string InputFingerprint { get; set; } = string.Empty;

    public string[] CompletedOutputPaths { get; set; } = Array.Empty<string>();
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
        if (!document.RootElement.TryGetProperty("SchemaVersion", out var schemaVersionElement) ||
            schemaVersionElement.ValueKind != JsonValueKind.Number ||
            !schemaVersionElement.TryGetInt32(out var schemaVersion) ||
            schemaVersion != EffectSummarySchemaContract.CurrentVersion)
            throw new InvalidOperationException(
                $"Artifact source summary '{path}' must use effect-summary schema " +
                EffectSummarySchemaContract.CurrentVersion + ".");

        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var canonicalKeys = new HashSet<string>(StringComparer.Ordinal);
        var exclusionPrefixes = excludedSymbolPrefixes ?? Array.Empty<string>();
        var inclusionPrefixes = includedSymbolPrefixes ?? Array.Empty<string>();

        if (TryCollectReachableSourceSummaryMethods(document.RootElement, inclusionPrefixes, exclusionPrefixes, symbols,
                canonicalKeys))
            return new ArtifactSpecSymbolSet(
                symbols.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray(),
                canonicalKeys.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray());

        if (document.RootElement.TryGetProperty("GeneratedPurityCatalog", out var generatedPurityCatalog) &&
            generatedPurityCatalog.ValueKind == JsonValueKind.Object &&
            generatedPurityCatalog.TryGetProperty("Entries", out var entriesElement) &&
            entriesElement.ValueKind == JsonValueKind.Array)
            foreach (var entryElement in entriesElement.EnumerateArray())
                AddSourceSummaryMethod(entryElement, inclusionPrefixes, exclusionPrefixes, symbols, canonicalKeys);

        if (symbols.Count == 0 &&
            document.RootElement.TryGetProperty("Assemblies", out var assembliesElement) &&
            assembliesElement.ValueKind == JsonValueKind.Array)
            foreach (var assemblyElement in assembliesElement.EnumerateArray())
            {
                if (!assemblyElement.TryGetProperty("Methods", out var methodsElement) ||
                    methodsElement.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var methodElement in methodsElement.EnumerateArray())
                    AddSourceSummaryMethod(methodElement, inclusionPrefixes, exclusionPrefixes, symbols, canonicalKeys);
            }

        if (symbols.Count == 0 && inclusionPrefixes.Count == 0)
            throw new InvalidOperationException($"Artifact source summary '{path}' did not contain any symbols.");

        return new ArtifactSpecSymbolSet(
            symbols.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray(),
            canonicalKeys.OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray());
    }

    private static void AddSourceSummaryMethod(
        JsonElement methodElement,
        IReadOnlyList<string> includedSymbolPrefixes,
        IReadOnlyList<string> excludedSymbolPrefixes,
        ISet<string> symbols,
        ISet<string> canonicalKeys)
    {
        var symbol = GetTrimmedStringProperty(methodElement, "DisplayName");
        var included = MatchesIncludedPrefix(symbol, includedSymbolPrefixes);
        if (!string.IsNullOrWhiteSpace(symbol) &&
            included &&
            !ArtifactSpecSymbolFilter.MatchesExcludedPrefix(symbol, excludedSymbolPrefixes))
            symbols.Add(symbol);

        var canonicalKey = GetTrimmedStringProperty(methodElement, "CanonicalKey");
        if (included && !string.IsNullOrWhiteSpace(canonicalKey)) canonicalKeys.Add(canonicalKey);
    }

    private static string? GetTrimmedStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String) return null;

        return property.GetString()?.Trim();
    }

    private static bool MatchesIncludedPrefix(string? symbol, IReadOnlyList<string> includedSymbolPrefixes)
    {
        if (includedSymbolPrefixes.Count == 0) return true;

        if (string.IsNullOrWhiteSpace(symbol)) return false;

        return includedSymbolPrefixes.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool TryCollectReachableSourceSummaryMethods(
        JsonElement rootElement,
        IReadOnlyList<string> includedSymbolPrefixes,
        IReadOnlyList<string> excludedSymbolPrefixes,
        HashSet<string> symbols,
        HashSet<string> canonicalKeys)
    {
        if (includedSymbolPrefixes.Count == 0 ||
            !rootElement.TryGetProperty("Assemblies", out var assembliesElement) ||
            assembliesElement.ValueKind != JsonValueKind.Array)
            return false;

        var methodEntries = new List<SourceSummaryMethodEntry>();
        foreach (var assemblyElement in assembliesElement.EnumerateArray())
        {
            if (!assemblyElement.TryGetProperty("Methods", out var methodsElement) ||
                methodsElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var methodElement in methodsElement.EnumerateArray())
            {
                var symbol = GetTrimmedStringProperty(methodElement, "DisplayName");
                var canonicalKey = GetTrimmedStringProperty(methodElement, "CanonicalKey");
                if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(canonicalKey)) continue;

                var calls = methodElement.TryGetProperty("Calls", out var callsElement) &&
                            callsElement.ValueKind == JsonValueKind.Array
                    ? callsElement.EnumerateArray()
                        .Select(call => call.ValueKind == JsonValueKind.String ? call.GetString()?.Trim() : null)
                        .Where(call => !string.IsNullOrWhiteSpace(call))
                        .Cast<string>()
                        .ToArray()
                    : Array.Empty<string>();

                methodEntries.Add(new SourceSummaryMethodEntry(symbol, canonicalKey, calls));
            }
        }

        if (methodEntries.Count == 0) return false;

        var includedMemberTokens = includedSymbolPrefixes
            .Select(TryGetMemberToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (includedMemberTokens.Count == 0) return false;

        var byCanonicalKey = methodEntries
            .GroupBy(entry => entry.CanonicalKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new SourceSummaryMethodEntry(
                    group.First().DisplayName,
                    group.Key,
                    group.SelectMany(entry => entry.Calls)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()),
                StringComparer.Ordinal);
        var queue = new Queue<SourceSummaryMethodEntry>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in methodEntries.Where(entry =>
                     MatchesIncludedPrefix(entry.DisplayName, includedSymbolPrefixes) ||
                     includedMemberTokens.Contains(TryGetMemberToken(entry.DisplayName) ?? string.Empty)))
            if (visited.Add(entry.CanonicalKey))
                queue.Enqueue(entry);

        if (queue.Count == 0) return false;

        while (queue.Count > 0)
        {
            var entry = queue.Dequeue();
            if (!ArtifactSpecSymbolFilter.MatchesExcludedPrefix(entry.DisplayName, excludedSymbolPrefixes))
            {
                symbols.Add(entry.DisplayName);
                canonicalKeys.Add(entry.CanonicalKey);
            }

            foreach (var call in entry.Calls)
                if (byCanonicalKey.TryGetValue(call, out var callee) && visited.Add(callee.CanonicalKey))
                    queue.Enqueue(callee);
        }

        return symbols.Count > 0;
    }

    private static string? TryGetMemberToken(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        var parameterIndex = symbol.IndexOf('(', StringComparison.Ordinal);
        var beforeParameters = parameterIndex >= 0
            ? symbol.Substring(0, parameterIndex)
            : symbol;
        var lastDot = beforeParameters.LastIndexOf('.');
        if (lastDot < 0 || lastDot == beforeParameters.Length - 1) return null;

        var memberName = beforeParameters.Substring(lastDot + 1);
        if (memberName.StartsWith("get_", StringComparison.Ordinal) ||
            memberName.StartsWith("set_", StringComparison.Ordinal))
            memberName = memberName.Substring(4);
        else if (memberName.StartsWith("Get", StringComparison.Ordinal) && memberName.Length > 3)
            memberName = memberName.Substring(3);
        else if (memberName.StartsWith("Set", StringComparison.Ordinal) && memberName.Length > 3)
            memberName = memberName.Substring(3);

        return memberName;
    }
}

internal sealed record ArtifactSpecSymbolSet(
    string[] Symbols,
    string[] CanonicalKeys);

internal sealed record SourceSummaryMethodEntry(
    string DisplayName,
    string CanonicalKey,
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
            Methods = filteredMethods
        };
    }

    public static bool MatchesExcludedPrefix(string symbol, IReadOnlyList<string> excludedSymbolPrefixes)
    {
        if (string.IsNullOrWhiteSpace(symbol) || excludedSymbolPrefixes.Count == 0) return false;

        return excludedSymbolPrefixes.Any(prefix => symbol.StartsWith(prefix, StringComparison.Ordinal));
    }
}
