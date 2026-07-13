using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Identity;

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
                    ? OrderExceptionEdges(edgeMap.Values)
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

    private static IEnumerable<SummaryEntry> ParseEntries(
        string json,
        int sourcePriority,
        string? sourcePath,
        EffectSummaryCompatibilityReporter? compatibilityReporter)
    {
        if (!EffectSummaryJsonDocument.TryParse(json, out var document, out _))
            yield break;
        using (document)
        {
            foreach (var assembly in document.EnumerateLegacyAssemblies())
            {
                var assemblyIdentity = SummaryAssemblyIdentity.FromJson(assembly.Element);
                var artifactSource = EffectSummaryArtifactSource.FromJson(assembly.Element);

                foreach (var methodElement in assembly.EnumerateMethods())
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
                                ? OrderExceptionEdges(edges.Values)
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

    private static ImmutableArray<SummaryExceptionEdgeInfo> OrderExceptionEdges(
        IEnumerable<SummaryExceptionEdgeInfo> edges)
    {
        return edges
            .OrderBy(static edge => edge.Depth)
            .ThenBy(static edge => edge.CalleeIdentity?.ToCanonicalKey(), StringComparer.Ordinal)
            .ThenBy(
                static edge => string.Join(">", edge.CallChain.Select(static identity => identity.ToCanonicalKey())),
                StringComparer.Ordinal)
            .ThenBy(static edge => edge.SourcePath, StringComparer.Ordinal)
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
        return RoslynStructuralMethodIdentityAdapter.GetCompatibleCanonicalKeys(methodSymbol);
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

            return EffectSummaryEntryTrustEvaluator.IsTrusted(
                AssemblyIdentity,
                ArtifactSource,
                MethodIdentity,
                actualAssemblyIdentity,
                actualMethodIdentity,
                SourcePriority == BuiltInSummarySourcePriority,
                SourcePriority == AdditionalSummarySourcePriority ? CompatibilityReporter : null,
                SourcePath,
                Symbol);
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

    internal interface ISummaryExceptionEdgeIdentity
    {
        string? SourcePath { get; }

        ImmutableArray<StructuralMethodIdentity> CallChain { get; }

        StructuralMethodIdentity? CalleeIdentity { get; }

        int? Depth { get; }
    }

    internal sealed class SummaryExceptionFact : ISummaryExceptionEdgeIdentity
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

    internal sealed class SummaryExceptionEdgeInfo : ISummaryExceptionEdgeIdentity
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

            return SummaryExceptionEdgeIdentity.Equals(x, y);
        }

        public int GetHashCode(SummaryExceptionEdgeInfo obj)
        {
            return SummaryExceptionEdgeIdentity.GetHashCode(obj);
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
                   SummaryExceptionEdgeIdentity.Equals(x, y);
        }

        public int GetHashCode(SummaryExceptionFact obj)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(obj.ExceptionType);
                hash = hash * 31 + (int)obj.OriginKind;
                hash = hash * 31 + SummaryExceptionEdgeIdentity.GetHashCode(obj);
                return hash;
            }
        }
    }

    private static class SummaryExceptionEdgeIdentity
    {
        internal static bool Equals(
            ISummaryExceptionEdgeIdentity left,
            ISummaryExceptionEdgeIdentity right)
        {
            return string.Equals(left.SourcePath, right.SourcePath, StringComparison.Ordinal) &&
                   left.CallChain.SequenceEqual(right.CallChain) &&
                   object.Equals(left.CalleeIdentity, right.CalleeIdentity) &&
                   left.Depth == right.Depth;
        }

        internal static int GetHashCode(ISummaryExceptionEdgeIdentity edge)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 +
                       (edge.SourcePath != null ? StringComparer.Ordinal.GetHashCode(edge.SourcePath) : 0);
                foreach (var identity in edge.CallChain) hash = hash * 31 + identity.GetHashCode();
                hash = hash * 31 + (edge.CalleeIdentity?.GetHashCode() ?? 0);
                hash = hash * 31 + (edge.Depth ?? 0);
                return hash;
            }
        }
    }
}
