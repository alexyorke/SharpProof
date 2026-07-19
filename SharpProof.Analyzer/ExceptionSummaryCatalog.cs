using System.Text.Json;
using SharpProof.Identity;

namespace SharpProof.Analyzer;

internal sealed class ExceptionSummaryCatalog
{
    private static readonly Lazy<ExceptionSummaryCatalog> BuiltInCatalog =
        new(CreateBuiltInCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly EffectSummaryIdentityResolver IdentityResolver =
        new(
            false,
            true,
            RoslynStructuralMethodIdentity.GetCanonicalKey);

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
        return BuiltInEffectSummaryLoader.LoadCatalogWithAdditionalDocuments(
            options,
            cancellationToken,
            BuiltInCatalog.Value,
            CreateMutableEntries,
            EffectSummaryCatalogSourcePriorities.Additional,
            ParseEntries,
            CreateCatalog,
            compatibilityReporter);
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

        foreach (var entry in EffectSummaryCatalogEntryMap.EnumerateCompatible(_entriesBySymbol, methodSymbol))
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
        return BuiltInEffectSummaryLoader.LoadBuiltInCatalog(
            EffectSummaryCatalogSourcePriorities.BuiltIn,
            ParseEntries,
            CreateCatalog);
    }

    private static Dictionary<string, ImmutableArray<SummaryEntry>.Builder> CreateMutableEntries(
        ExceptionSummaryCatalog catalog)
    {
        return EffectSummaryCatalogEntryMap.Clone(catalog._entriesBySymbol);
    }

    private static ExceptionSummaryCatalog CreateCatalog(
        Dictionary<string, ImmutableArray<SummaryEntry>.Builder> entriesBySymbol)
    {
        if (entriesBySymbol.Count == 0) return Empty;

        return new ExceptionSummaryCatalog(EffectSummaryCatalogEntryMap.Freeze(entriesBySymbol));
    }

    private static IEnumerable<SummaryEntry> ParseEntries(
        EffectSummaryJsonDocument document,
        int sourcePriority,
        string? sourcePath,
        EffectSummaryCompatibilityReporter? compatibilityReporter)
    {
        foreach (var assembly in document.EnumerateLegacyAssemblies())
        {
            var assemblyIdentity = SummaryAssemblyIdentity.FromJson(assembly.Element);
            var artifactSource = EffectSummaryArtifactSource.FromJson(assembly.Element);

            foreach (var methodElement in assembly.EnumerateMethods())
            {
                if (!StructuralMethodIdentityJson.TryReadMethod(methodElement, out _, out var canonicalKey))
                    continue;

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
                    assemblyIdentity,
                    SummaryMethodIdentity.FromJson(methodElement),
                    artifactSource,
                    sourcePriority,
                    sourcePath,
                    compatibilityReporter);
            }
        }
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

    private static void AddExceptionSources(
        ImmutableSortedSet<string>.Builder exceptionTypes,
        Dictionary<string, ImmutableSortedSet<string>.Builder> exceptionSources,
        JsonElement methodElement,
        string propertyName)
    {
        foreach (var valueElement in EnumerateObjectArrayProperty(methodElement, propertyName))
        {
            if (!TryGetExceptionTypeAndSourcePath(valueElement, out var exceptionType, out var sourcePath)) continue;

            AddExceptionSource(exceptionTypes, exceptionSources, exceptionType, sourcePath);
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

            var sourcePath = GetEdgeSourcePath(valueElement);
            AddExceptionSource(exceptionTypes, exceptionSources, exceptionType, sourcePath);

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

    private static void AddExceptionSource(
        ImmutableSortedSet<string>.Builder exceptionTypes,
        Dictionary<string, ImmutableSortedSet<string>.Builder> exceptionSources,
        string exceptionType,
        string? sourcePath)
    {
        exceptionTypes.Add(exceptionType);
        if (sourcePath == null) return;

        if (!exceptionSources.TryGetValue(exceptionType, out var sources))
        {
            sources = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
            exceptionSources.Add(exceptionType, sources);
        }

        sources.Add(sourcePath);
    }

    private static HashSet<string> GetExceptionTypes(JsonElement methodElement, string propertyName)
    {
        var exceptionTypes = new HashSet<string>(StringComparer.Ordinal);
        exceptionTypes.UnionWith(EnumerateTrimmedStringArrayValues(methodElement, propertyName));
        return exceptionTypes;
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
        sourcePath = AnalyzerJsonElementReader.GetTrimmedStringProperty(element, "SourcePath");
        return TryGetExceptionType(element, out exceptionType);
    }

    private static bool TryGetExceptionType(JsonElement element, out string exceptionType)
    {
        var value = AnalyzerJsonElementReader.GetTrimmedStringProperty(element, "ExceptionType");
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
        return AnalyzerJsonElementReader.GetTrimmedStringProperty(element, "SourcePath");
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

    private sealed class SummaryEntry : EffectSummaryCatalogEntry
    {
        public SummaryEntry(
            string symbol,
            ImmutableArray<SummaryExceptionInfo> exceptionInfos,
            SummaryAssemblyIdentity? assemblyIdentity,
            SummaryMethodIdentity? methodIdentity,
            EffectSummaryArtifactSource? artifactSource,
            int sourcePriority,
            string? sourcePath,
            EffectSummaryCompatibilityReporter? compatibilityReporter)
            : base(
                symbol,
                symbol,
                assemblyIdentity,
                methodIdentity,
                artifactSource,
                sourcePriority,
                sourcePath,
                compatibilityReporter)
        {
            ExceptionInfos = exceptionInfos;
        }

        public ImmutableArray<SummaryExceptionInfo> ExceptionInfos { get; }
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

            return SummaryExceptionEdgeIdentity.Equals(x, y);
        }

        public int GetHashCode(SummaryExceptionEdgeInfo obj)
        {
            return SummaryExceptionEdgeIdentity.GetHashCode(obj);
        }
    }

    private static class SummaryExceptionEdgeIdentity
    {
        internal static bool Equals(
            SummaryExceptionEdgeInfo left,
            SummaryExceptionEdgeInfo right)
        {
            return string.Equals(left.SourcePath, right.SourcePath, StringComparison.Ordinal) &&
                   left.CallChain.SequenceEqual(right.CallChain) &&
                   object.Equals(left.CalleeIdentity, right.CalleeIdentity) &&
                   left.Depth == right.Depth;
        }

        internal static int GetHashCode(SummaryExceptionEdgeInfo edge)
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
