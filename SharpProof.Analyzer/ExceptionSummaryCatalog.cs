using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Identity;
using SharpProof.Schema;

namespace SharpProof.Analyzer;

internal sealed class ExceptionSummaryCatalog
{
    private const int BuiltInSummarySourcePriority = 0;
    private const int AdditionalSummarySourcePriority = 1;

    private static readonly Lazy<ExceptionSummaryCatalog> BuiltInCatalog =
        new(CreateBuiltInCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly EffectSummaryIdentityResolver IdentityResolver =
        new(
            false,
            false,
            true,
            RoslynStructuralMethodIdentityAdapter.GetCanonicalKey);

    public static readonly ExceptionSummaryCatalog Empty = new(
        ImmutableDictionary<string, ImmutableArray<SummaryEntry>>.Empty);

    private readonly ImmutableDictionary<string, ImmutableArray<SummaryEntry>> _entriesBySymbol;

    private ExceptionSummaryCatalog(ImmutableDictionary<string, ImmutableArray<SummaryEntry>> entriesBySymbol)
    {
        _entriesBySymbol = entriesBySymbol;
    }

    private bool IsEmpty => _entriesBySymbol.IsEmpty;

    public static ExceptionSummaryCatalog FromOptions(
        AnalyzerOptions options,
        CancellationToken cancellationToken)
    {
        return FromOptionsWithCompatibilityReporter(
            options,
            cancellationToken,
            new EffectSummaryCompatibilityReporter());
    }

    internal static ExceptionSummaryCatalog FromOptionsWithCompatibilityReporter(
        AnalyzerOptions options,
        CancellationToken cancellationToken,
        EffectSummaryCompatibilityReporter compatibilityReporter)
    {
        if (!BuiltInEffectSummaryLoader.HasAdditionalSummaryJsonDocuments(options)) return BuiltInCatalog.Value;

        var entriesBySymbol = CreateMutableEntries(BuiltInCatalog.Value);
        BuiltInEffectSummaryLoader.LoadAdditionalSummaryJsonDocuments(
            options,
            cancellationToken,
            (path, json) => AddParsedEntries(
                entriesBySymbol,
                json,
                AdditionalSummarySourcePriority,
                path,
                compatibilityReporter));
        return CreateCatalog(entriesBySymbol);
    }

    public bool TryGetExceptionInfos(
        IMethodSymbol methodSymbol,
        Compilation? compilation,
        out ImmutableArray<SummaryExceptionInfo> exceptionInfos)
    {
        if (IsEmpty)
        {
            exceptionInfos = ImmutableArray<SummaryExceptionInfo>.Empty;
            return false;
        }

        var matchedExceptionSources =
            new Dictionary<string, ImmutableSortedSet<string>.Builder>(StringComparer.Ordinal);
        var matchedExceptionEdges =
            new Dictionary<string, Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>>(StringComparer
                .Ordinal);
        var actualAssemblyIdentity = compilation is null
            ? null
            : IdentityResolver.TryResolveActualAssemblyIdentity(methodSymbol, compilation);
        var actualMethodIdentity = compilation is null
            ? null
            : IdentityResolver.TryResolveActualMethodIdentity(methodSymbol, compilation);

        foreach (var key in GetSymbolKeys(methodSymbol))
        {
            if (!_entriesBySymbol.TryGetValue(key, out var entries)) continue;

            foreach (var entry in entries)
            {
                if (!entry.IsTrustedFor(methodSymbol, actualAssemblyIdentity, actualMethodIdentity)) continue;

                foreach (var exceptionInfo in entry.ExceptionInfos)
                {
                    if (!matchedExceptionSources.TryGetValue(exceptionInfo.ExceptionType, out var sources))
                    {
                        sources = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                        matchedExceptionSources.Add(exceptionInfo.ExceptionType, sources);
                    }

                    sources.UnionWith(exceptionInfo.Sources);

                    if (!exceptionInfo.Edges.IsDefaultOrEmpty)
                    {
                        if (!matchedExceptionEdges.TryGetValue(exceptionInfo.ExceptionType, out var edgeMap))
                        {
                            edgeMap = new Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>(
                                SummaryExceptionEdgeInfoComparer.Instance);
                            matchedExceptionEdges.Add(exceptionInfo.ExceptionType, edgeMap);
                        }

                        foreach (var edge in exceptionInfo.Edges) edgeMap[edge] = edge;
                    }
                }
            }
        }

        if (matchedExceptionSources.Count == 0)
        {
            exceptionInfos = ImmutableArray<SummaryExceptionInfo>.Empty;
            return false;
        }

        exceptionInfos = matchedExceptionSources
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new SummaryExceptionInfo(
                item.Key,
                item.Value.ToImmutableArray(),
                matchedExceptionEdges.TryGetValue(item.Key, out var edgeMap)
                    ? edgeMap.Values
                        .OrderBy(edge => edge.Depth)
                        .ThenBy(edge => edge.CalleeIdentity?.ToCanonicalKey(), StringComparer.Ordinal)
                        .ThenBy(
                            edge => string.Join(">", edge.CallChain.Select(static identity => identity.ToCanonicalKey())),
                            StringComparer.Ordinal)
                        .ThenBy(edge => edge.SourcePath, StringComparer.Ordinal)
                        .ToImmutableArray()
                    : ImmutableArray<SummaryExceptionEdgeInfo>.Empty))
            .ToImmutableArray();
        return true;
    }

    private static ExceptionSummaryCatalog CreateBuiltInCatalog()
    {
        var entriesBySymbol = new Dictionary<string, ImmutableArray<SummaryEntry>.Builder>(StringComparer.Ordinal);
        BuiltInEffectSummaryLoader.LoadBuiltInSummaryJsonDocuments(json =>
            AddParsedEntries(entriesBySymbol, json, BuiltInSummarySourcePriority, null, null));
        return CreateCatalog(entriesBySymbol);
    }

    private static Dictionary<string, ImmutableArray<SummaryEntry>.Builder> CreateMutableEntries(
        ExceptionSummaryCatalog catalog)
    {
        var entriesBySymbol = new Dictionary<string, ImmutableArray<SummaryEntry>.Builder>(StringComparer.Ordinal);
        foreach (var entry in catalog._entriesBySymbol)
        {
            var builder = ImmutableArray.CreateBuilder<SummaryEntry>(entry.Value.Length);
            builder.AddRange(entry.Value);
            entriesBySymbol.Add(entry.Key, builder);
        }

        return entriesBySymbol;
    }

    private static ExceptionSummaryCatalog CreateCatalog(
        Dictionary<string, ImmutableArray<SummaryEntry>.Builder> entriesBySymbol)
    {
        if (entriesBySymbol.Count == 0) return Empty;

        return new ExceptionSummaryCatalog(entriesBySymbol.ToImmutableDictionary(
            item => item.Key,
            item => item.Value.ToImmutable(),
            StringComparer.Ordinal));
    }

    private static void AddParsedEntries(
        Dictionary<string, ImmutableArray<SummaryEntry>.Builder> entriesBySymbol,
        string json,
        int sourcePriority,
        string? sourcePath,
        EffectSummaryCompatibilityReporter? compatibilityReporter)
    {
        try
        {
            foreach (var entry in ParseEntries(
                         json,
                         sourcePriority,
                         sourcePath,
                         compatibilityReporter))
            {
                if (!entriesBySymbol.TryGetValue(entry.Symbol, out var builder))
                {
                    builder = ImmutableArray.CreateBuilder<SummaryEntry>();
                    entriesBySymbol.Add(entry.Symbol, builder);
                }

                builder.Add(entry);
            }
        }
        catch (JsonException)
        {
        }
    }

    private static IEnumerable<SummaryEntry> ParseEntries(
        string json,
        int sourcePriority,
        string? sourcePath,
        EffectSummaryCompatibilityReporter? compatibilityReporter)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("SchemaVersion", out var schemaVersionElement) ||
            schemaVersionElement.ValueKind != JsonValueKind.Number ||
            !schemaVersionElement.TryGetInt32(out var schemaVersion) ||
            schemaVersion != EffectSummarySchemaContract.CurrentVersion)
            yield break;

        if (!document.RootElement.TryGetProperty("Assemblies", out var assembliesElement) ||
            assembliesElement.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var assemblyElement in assembliesElement.EnumerateArray())
        {
            var assemblyIdentity = SummaryAssemblyIdentity.FromJson(assemblyElement);
            var artifactSource = EffectSummaryArtifactSource.FromJson(assemblyElement);
            if (!assemblyElement.TryGetProperty("Methods", out var methodsElement) ||
                methodsElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var methodElement in methodsElement.EnumerateArray())
            {
                if (!StructuralMethodIdentityJson.TryReadMethod(methodElement, out _, out var canonicalKey))
                    continue;

                var exceptionFacts = ParseExceptionFacts(methodElement);
                var exceptionTypes = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                var exceptionSources =
                    new Dictionary<string, ImmutableSortedSet<string>.Builder>(StringComparer.Ordinal);
                var exceptionEdges =
                    new Dictionary<string, Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>>(
                        StringComparer.Ordinal);
                exceptionTypes.UnionWith(GetExceptionTypes(methodElement, "ThrownExceptionTypes"));
                exceptionTypes.UnionWith(GetExceptionTypes(methodElement, "TransitiveThrownExceptionTypes"));
                AddExceptionSources(exceptionTypes, exceptionSources, methodElement, "ThrownExceptionProvenance");
                AddExceptionSources(exceptionTypes, exceptionSources, methodElement,
                    "TransitiveThrownExceptionProvenance");
                AddExceptionEdges(exceptionTypes, exceptionSources, exceptionEdges, methodElement,
                    "TransitiveThrownExceptionEdges");
                if (exceptionTypes.Count == 0) continue;

                var exceptionInfos = exceptionTypes
                    .Select(exceptionType => new SummaryExceptionInfo(
                        exceptionType,
                        exceptionSources.TryGetValue(exceptionType, out var sources)
                            ? sources.ToImmutableArray()
                            : ImmutableArray<string>.Empty,
                        exceptionEdges.TryGetValue(exceptionType, out var edges)
                            ? edges.Values
                                .OrderBy(edge => edge.Depth)
                                .ThenBy(edge => edge.CalleeIdentity?.ToCanonicalKey(), StringComparer.Ordinal)
                                .ThenBy(
                                    edge => string.Join(
                                        ">",
                                        edge.CallChain.Select(static identity => identity.ToCanonicalKey())),
                                    StringComparer.Ordinal)
                                .ThenBy(edge => edge.SourcePath, StringComparer.Ordinal)
                                .ToImmutableArray()
                            : ImmutableArray<SummaryExceptionEdgeInfo>.Empty))
                    .ToImmutableArray();
                yield return new SummaryEntry(
                    canonicalKey,
                    exceptionInfos,
                    exceptionFacts,
                    assemblyIdentity,
                    SummaryMethodIdentity.FromJson(methodElement),
                    artifactSource,
                    sourcePriority,
                    sourcePath,
                    compatibilityReporter);
            }
        }
    }

    private static ImmutableArray<SummaryExceptionFact> ParseExceptionFacts(JsonElement methodElement)
    {
        var directExceptionTypes = GetExceptionTypes(methodElement, "ThrownExceptionTypes");
        var directExceptionSourceKeys = GetExceptionSourceKeys(methodElement, "ThrownExceptionProvenance");
        var factMap = new Dictionary<SummaryExceptionFact, SummaryExceptionFact>(SummaryExceptionFactComparer.Instance);
        AddExceptionTypeFacts(factMap, methodElement, "ThrownExceptionTypes",
            static _ => SummaryExceptionOriginKind.Direct);
        AddExceptionTypeFacts(
            factMap,
            methodElement,
            "TransitiveThrownExceptionTypes",
            exceptionType => directExceptionTypes.Contains(exceptionType)
                ? SummaryExceptionOriginKind.Direct
                : SummaryExceptionOriginKind.Transitive);
        AddExceptionSourceFacts(
            factMap,
            methodElement,
            "ThrownExceptionProvenance",
            static (_, _) => SummaryExceptionOriginKind.Direct);
        AddExceptionSourceFacts(
            factMap,
            methodElement,
            "TransitiveThrownExceptionProvenance",
            (exceptionType, sourcePath) =>
                sourcePath != null &&
                directExceptionSourceKeys.Contains(CreateExceptionFactSourceKey(exceptionType, sourcePath))
                    ? SummaryExceptionOriginKind.Direct
                    : SummaryExceptionOriginKind.Transitive);
        AddExceptionEdgeFacts(
            factMap,
            methodElement,
            "TransitiveThrownExceptionEdges",
            (exceptionType, sourcePath, calleeIdentity, depth) =>
                IsDirectExceptionEdge(sourcePath, calleeIdentity, depth, directExceptionSourceKeys, exceptionType)
                    ? SummaryExceptionOriginKind.Direct
                    : SummaryExceptionOriginKind.Transitive);

        PruneRedundantTypeOnlyFacts(factMap);

        return factMap.Count == 0
            ? ImmutableArray<SummaryExceptionFact>.Empty
            : factMap.Values
                .OrderBy(fact => fact.ExceptionType, StringComparer.Ordinal)
                .ThenBy(fact => fact.OriginKind)
                .ThenBy(fact => fact.Depth ?? int.MinValue)
                .ThenBy(fact => fact.CalleeIdentity?.ToCanonicalKey(), StringComparer.Ordinal)
                .ThenBy(
                    fact => string.Join(">", fact.CallChain.Select(static identity => identity.ToCanonicalKey())),
                    StringComparer.Ordinal)
                .ThenBy(fact => fact.SourcePath, StringComparer.Ordinal)
                .ToImmutableArray();
    }

    private static void PruneRedundantTypeOnlyFacts(
        Dictionary<SummaryExceptionFact, SummaryExceptionFact> factMap)
    {
        var redundantFacts = factMap.Values
            .Where(fact =>
                fact.SourcePath == null &&
                fact.CallChain.IsDefaultOrEmpty &&
                fact.CalleeIdentity == null &&
                fact.Depth == null &&
                factMap.Values.Any(other =>
                    !ReferenceEquals(other, fact) &&
                    string.Equals(other.ExceptionType, fact.ExceptionType, StringComparison.Ordinal) &&
                    other.OriginKind == fact.OriginKind &&
                    (other.SourcePath != null || !other.CallChain.IsDefaultOrEmpty ||
                     other.CalleeIdentity != null || other.Depth != null)))
            .ToArray();

        foreach (var redundantFact in redundantFacts) factMap.Remove(redundantFact);
    }

    private static void AddExceptionTypeFacts(
        Dictionary<SummaryExceptionFact, SummaryExceptionFact> factMap,
        JsonElement methodElement,
        string propertyName,
        Func<string, SummaryExceptionOriginKind> getOriginKind)
    {
        foreach (var trimmedValue in EnumerateTrimmedStringArrayValues(methodElement, propertyName))
        {
            var fact = new SummaryExceptionFact(
                trimmedValue,
                getOriginKind(trimmedValue),
                null,
                ImmutableArray<StructuralMethodIdentity>.Empty,
                null,
                null);
            factMap[fact] = fact;
        }
    }

    private static void AddExceptionSources(
        ImmutableSortedSet<string>.Builder exceptionTypes,
        Dictionary<string, ImmutableSortedSet<string>.Builder> exceptionSources,
        JsonElement methodElement,
        string propertyName)
    {
        foreach (var valueElement in EnumerateObjectArrayProperty(methodElement, propertyName))
        {
            if (!TryGetExceptionTypeAndSourcePath(valueElement, out var exceptionType, out var sourcePath)) continue;

            exceptionTypes.Add(exceptionType);
            if (sourcePath == null) continue;

            if (!exceptionSources.TryGetValue(exceptionType, out var sources))
            {
                sources = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                exceptionSources.Add(exceptionType, sources);
            }

            sources.Add(sourcePath);
        }
    }

    private static void AddExceptionSourceFacts(
        Dictionary<SummaryExceptionFact, SummaryExceptionFact> factMap,
        JsonElement methodElement,
        string propertyName,
        Func<string, string?, SummaryExceptionOriginKind> getOriginKind)
    {
        foreach (var valueElement in EnumerateObjectArrayProperty(methodElement, propertyName))
        {
            if (!TryGetExceptionTypeAndSourcePath(valueElement, out var exceptionType, out var sourcePath)) continue;

            var fact = new SummaryExceptionFact(
                exceptionType,
                getOriginKind(exceptionType, sourcePath),
                sourcePath,
                StructuralMethodIdentityJson.ReadCallChain(valueElement),
                null,
                null);
            factMap[fact] = fact;
        }
    }

    private static void AddExceptionEdges(
        ImmutableSortedSet<string>.Builder exceptionTypes,
        Dictionary<string, ImmutableSortedSet<string>.Builder> exceptionSources,
        Dictionary<string, Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>> exceptionEdges,
        JsonElement methodElement,
        string propertyName)
    {
        foreach (var valueElement in EnumerateObjectArrayProperty(methodElement, propertyName))
        {
            if (!TryGetExceptionType(valueElement, out var exceptionType)) continue;

            exceptionTypes.Add(exceptionType);

            var sourcePath = GetEdgeSourcePath(valueElement);
            if (sourcePath != null)
            {
                if (!exceptionSources.TryGetValue(exceptionType, out var sources))
                {
                    sources = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                    exceptionSources.Add(exceptionType, sources);
                }

                sources.Add(sourcePath);
            }

            if (!exceptionEdges.TryGetValue(exceptionType, out var edgeMap))
            {
                edgeMap = new Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>(
                    SummaryExceptionEdgeInfoComparer.Instance);
                exceptionEdges.Add(exceptionType, edgeMap);
            }

            var edge = new SummaryExceptionEdgeInfo(
                sourcePath,
                StructuralMethodIdentityJson.ReadCallChain(valueElement),
                GetEdgeCalleeIdentity(valueElement),
                TryGetOptionalInt32(valueElement, "Depth"));
            edgeMap[edge] = edge;
        }
    }

    private static void AddExceptionEdgeFacts(
        Dictionary<SummaryExceptionFact, SummaryExceptionFact> factMap,
        JsonElement methodElement,
        string propertyName,
        Func<string, string?, StructuralMethodIdentity?, int?, SummaryExceptionOriginKind> getOriginKind)
    {
        foreach (var valueElement in EnumerateObjectArrayProperty(methodElement, propertyName))
        {
            if (!TryGetExceptionType(valueElement, out var exceptionType)) continue;

            var sourcePath = GetEdgeSourcePath(valueElement);
            var callChain = StructuralMethodIdentityJson.ReadCallChain(valueElement);
            var calleeIdentity = GetEdgeCalleeIdentity(valueElement);
            var depth = TryGetOptionalInt32(valueElement, "Depth");
            var fact = new SummaryExceptionFact(
                exceptionType,
                getOriginKind(exceptionType, sourcePath, calleeIdentity, depth),
                sourcePath,
                callChain,
                calleeIdentity,
                depth);
            factMap[fact] = fact;
        }
    }

    private static HashSet<string> GetExceptionTypes(JsonElement methodElement, string propertyName)
    {
        var exceptionTypes = new HashSet<string>(StringComparer.Ordinal);
        exceptionTypes.UnionWith(EnumerateTrimmedStringArrayValues(methodElement, propertyName));
        return exceptionTypes;
    }

    private static HashSet<string> GetExceptionSourceKeys(JsonElement methodElement, string propertyName)
    {
        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var valueElement in EnumerateObjectArrayProperty(methodElement, propertyName))
        {
            if (!TryGetExceptionTypeAndSourcePath(valueElement, out var exceptionType, out var sourcePath) ||
                sourcePath == null)
                continue;

            sourceKeys.Add(CreateExceptionFactSourceKey(exceptionType, sourcePath));
        }

        return sourceKeys;
    }

    private static bool IsDirectExceptionEdge(
        string? sourcePath,
        StructuralMethodIdentity? calleeIdentity,
        int? depth,
        HashSet<string> directExceptionSourceKeys,
        string exceptionType)
    {
        if (depth == 0 && calleeIdentity == null) return true;

        return sourcePath != null &&
               calleeIdentity == null &&
               directExceptionSourceKeys.Contains(CreateExceptionFactSourceKey(exceptionType, sourcePath));
    }

    private static string CreateExceptionFactSourceKey(string exceptionType, string sourcePath)
    {
        return exceptionType + "|" + sourcePath;
    }

    private static IEnumerable<string> EnumerateTrimmedStringArrayValues(JsonElement element, string propertyName)
    {
        if (!TryGetArrayProperty(element, propertyName, out var valuesElement)) yield break;

        foreach (var valueElement in valuesElement.EnumerateArray())
        {
            if (valueElement.ValueKind != JsonValueKind.String) continue;

            var value = valueElement.GetString();
            if (value != null && !string.IsNullOrWhiteSpace(value)) yield return value.Trim();
        }
    }

    private static IEnumerable<JsonElement> EnumerateObjectArrayProperty(JsonElement element, string propertyName)
    {
        if (!TryGetArrayProperty(element, propertyName, out var valuesElement)) yield break;

        foreach (var valueElement in valuesElement.EnumerateArray())
            if (valueElement.ValueKind == JsonValueKind.Object)
                yield return valueElement;
    }

    private static bool TryGetArrayProperty(JsonElement element, string propertyName, out JsonElement valuesElement)
    {
        if (element.TryGetProperty(propertyName, out valuesElement) &&
            valuesElement.ValueKind == JsonValueKind.Array)
            return true;

        valuesElement = default;
        return false;
    }

    private static bool TryGetExceptionTypeAndSourcePath(
        JsonElement element,
        out string exceptionType,
        out string? sourcePath)
    {
        sourcePath = CompatibilityHelpers.GetTrimmedStringProperty(element, "SourcePath");
        return TryGetExceptionType(element, out exceptionType);
    }

    private static bool TryGetExceptionType(JsonElement element, out string exceptionType)
    {
        var value = CompatibilityHelpers.GetTrimmedStringProperty(element, "ExceptionType");
        if (value == null)
        {
            exceptionType = null!;
            return false;
        }

        exceptionType = value;
        return true;
    }

    private static string? GetEdgeSourcePath(JsonElement element)
    {
        return CompatibilityHelpers.GetTrimmedStringProperty(element, "SourcePath");
    }

    private static StructuralMethodIdentity? GetEdgeCalleeIdentity(JsonElement element)
    {
        return element.TryGetProperty("CalleeIdentity", out var identityElement) &&
               StructuralMethodIdentityJson.TryReadIdentity(identityElement, out var identity)
            ? identity
            : null;
    }

    private static int? TryGetOptionalInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement)) return null;

        return valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static IEnumerable<string> GetSymbolKeys(IMethodSymbol methodSymbol)
    {
        yield return RoslynStructuralMethodIdentityAdapter.GetCanonicalKey(methodSymbol);
    }

    private sealed class SummaryEntry
    {
        public SummaryEntry(
            string symbol,
            ImmutableArray<SummaryExceptionInfo> exceptionInfos,
            ImmutableArray<SummaryExceptionFact> exceptionFacts,
            SummaryAssemblyIdentity? assemblyIdentity,
            SummaryMethodIdentity? methodIdentity,
            EffectSummaryArtifactSource? artifactSource,
            int sourcePriority,
            string? sourcePath,
            EffectSummaryCompatibilityReporter? compatibilityReporter)
        {
            Symbol = symbol;
            ExceptionInfos = exceptionInfos;
            ExceptionFacts = exceptionFacts;
            AssemblyIdentity = assemblyIdentity;
            MethodIdentity = methodIdentity;
            ArtifactSource = artifactSource;
            SourcePriority = sourcePriority;
            SourcePath = sourcePath;
            CompatibilityReporter = compatibilityReporter;
        }

        public string Symbol { get; }

        public ImmutableArray<SummaryExceptionInfo> ExceptionInfos { get; }

        public ImmutableArray<SummaryExceptionFact> ExceptionFacts { get; }

        public SummaryAssemblyIdentity? AssemblyIdentity { get; }

        public SummaryMethodIdentity? MethodIdentity { get; }
        private EffectSummaryArtifactSource? ArtifactSource { get; }

        public int SourcePriority { get; }
        private string? SourcePath { get; }
        private EffectSummaryCompatibilityReporter? CompatibilityReporter { get; }

        public bool IsTrustedFor(
            IMethodSymbol methodSymbol,
            ActualAssemblyIdentity? actualAssemblyIdentity,
            ActualMethodIdentity? actualMethodIdentity)
        {
            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true) return false;

            var assemblyCompatibility = AssemblyIdentity?.GetCompatibility(actualAssemblyIdentity) ??
                                        EffectSummaryCompatibility.Incompatible(
                                            "effect_summary_incomplete_assembly_identity",
                                            "its assembly identity is missing");
            if (!assemblyCompatibility.IsCompatible)
            {
                ReportIncompatibility(assemblyCompatibility);
                return false;
            }

            var artifactSourceCompatibility = ArtifactSource?.GetCompatibility(actualAssemblyIdentity!) ??
                                              EffectSummaryCompatibility.Compatible;
            if (!artifactSourceCompatibility.IsCompatible)
            {
                ReportIncompatibility(artifactSourceCompatibility);
                return false;
            }

            var methodCompatibility = MethodIdentity?.GetCompatibility(actualMethodIdentity) ??
                                      EffectSummaryCompatibility.Incompatible(
                                          "effect_summary_incomplete_method_identity",
                                          "its method identity is missing");
            if (!methodCompatibility.IsCompatible)
            {
                if (SourcePriority == BuiltInSummarySourcePriority &&
                    MethodIdentity?.MatchesMetadataToken(actualMethodIdentity) == true)
                    return true;

                ReportIncompatibility(methodCompatibility);
                return false;
            }

            if (SourcePriority == BuiltInSummarySourcePriority) return true;

            return true;
        }

        private void ReportIncompatibility(EffectSummaryCompatibility compatibility)
        {
            if (SourcePriority != AdditionalSummarySourcePriority || CompatibilityReporter == null) return;

            CompatibilityReporter.Report(SourcePath ?? string.Empty, Symbol, compatibility);
        }
    }

    internal sealed class SummaryExceptionInfo
    {
        public SummaryExceptionInfo(
            string exceptionType,
            ImmutableArray<string> sources,
            ImmutableArray<SummaryExceptionEdgeInfo> edges)
        {
            ExceptionType = exceptionType;
            Sources = sources;
            Edges = edges;
        }

        public string ExceptionType { get; }

        public ImmutableArray<string> Sources { get; }

        public ImmutableArray<SummaryExceptionEdgeInfo> Edges { get; }
    }

    internal enum SummaryExceptionOriginKind
    {
        Direct = 0,
        Transitive = 1
    }

    internal sealed class SummaryExceptionFact
    {
        public SummaryExceptionFact(
            string exceptionType,
            SummaryExceptionOriginKind originKind,
            string? sourcePath,
            ImmutableArray<StructuralMethodIdentity> callChain,
            StructuralMethodIdentity? calleeIdentity,
            int? depth)
        {
            ExceptionType = exceptionType;
            OriginKind = originKind;
            SourcePath = sourcePath;
            CallChain = callChain;
            CalleeIdentity = calleeIdentity;
            Depth = depth;
        }

        public string ExceptionType { get; }

        public SummaryExceptionOriginKind OriginKind { get; }

        public string? SourcePath { get; }

        public ImmutableArray<StructuralMethodIdentity> CallChain { get; }

        public StructuralMethodIdentity? CalleeIdentity { get; }

        public int? Depth { get; }
    }

    internal sealed class SummaryExceptionEdgeInfo
    {
        public SummaryExceptionEdgeInfo(
            string? sourcePath,
            ImmutableArray<StructuralMethodIdentity> callChain,
            StructuralMethodIdentity? calleeIdentity,
            int? depth)
        {
            SourcePath = sourcePath;
            CallChain = callChain;
            CalleeIdentity = calleeIdentity;
            Depth = depth;
        }

        public string? SourcePath { get; }

        public ImmutableArray<StructuralMethodIdentity> CallChain { get; }

        public StructuralMethodIdentity? CalleeIdentity { get; }

        public int? Depth { get; }
    }

    private sealed class SummaryExceptionEdgeInfoComparer : IEqualityComparer<SummaryExceptionEdgeInfo>
    {
        public static readonly SummaryExceptionEdgeInfoComparer Instance = new();

        public bool Equals(SummaryExceptionEdgeInfo? x, SummaryExceptionEdgeInfo? y)
        {
            if (ReferenceEquals(x, y)) return true;

            if (x is null || y is null) return false;

            return string.Equals(x.SourcePath, y.SourcePath, StringComparison.Ordinal) &&
                   x.CallChain.SequenceEqual(y.CallChain) &&
                   Equals(x.CalleeIdentity, y.CalleeIdentity) &&
                   x.Depth == y.Depth;
        }

        public int GetHashCode(SummaryExceptionEdgeInfo obj)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (obj.SourcePath != null ? StringComparer.Ordinal.GetHashCode(obj.SourcePath) : 0);
                foreach (var identity in obj.CallChain) hash = hash * 31 + identity.GetHashCode();
                hash = hash * 31 + (obj.CalleeIdentity?.GetHashCode() ?? 0);
                hash = hash * 31 + (obj.Depth ?? 0);
                return hash;
            }
        }
    }

    private sealed class SummaryExceptionFactComparer : IEqualityComparer<SummaryExceptionFact>
    {
        public static readonly SummaryExceptionFactComparer Instance = new();

        public bool Equals(SummaryExceptionFact? x, SummaryExceptionFact? y)
        {
            if (ReferenceEquals(x, y)) return true;

            if (x is null || y is null) return false;

            return string.Equals(x.ExceptionType, y.ExceptionType, StringComparison.Ordinal) &&
                   x.OriginKind == y.OriginKind &&
                   string.Equals(x.SourcePath, y.SourcePath, StringComparison.Ordinal) &&
                   x.CallChain.SequenceEqual(y.CallChain) &&
                   Equals(x.CalleeIdentity, y.CalleeIdentity) &&
                   x.Depth == y.Depth;
        }

        public int GetHashCode(SummaryExceptionFact obj)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(obj.ExceptionType);
                hash = hash * 31 + (int)obj.OriginKind;
                hash = hash * 31 + (obj.SourcePath != null ? StringComparer.Ordinal.GetHashCode(obj.SourcePath) : 0);
                foreach (var identity in obj.CallChain) hash = hash * 31 + identity.GetHashCode();
                hash = hash * 31 + (obj.CalleeIdentity?.GetHashCode() ?? 0);
                hash = hash * 31 + (obj.Depth ?? 0);
                return hash;
            }
        }
    }
}
