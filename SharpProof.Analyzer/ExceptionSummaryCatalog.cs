using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Analyzer
{
    internal sealed class ExceptionSummaryCatalog
    {
        private const int BuiltInSummarySourcePriority = 0;
        private const int AdditionalSummarySourcePriority = 1;
        private static readonly Lazy<ExceptionSummaryCatalog> BuiltInCatalog =
            new Lazy<ExceptionSummaryCatalog>(CreateBuiltInCatalog, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly SymbolDisplayFormat EffectSummaryContainingTypeFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        private static readonly SymbolDisplayFormat EffectSummaryParameterTypeFormat = new SymbolDisplayFormat(
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        public static readonly ExceptionSummaryCatalog Empty = new ExceptionSummaryCatalog(
            ImmutableDictionary<string, ImmutableArray<SummaryEntry>>.Empty);

        private static readonly ConcurrentDictionary<string, ActualAssemblyIdentity?> AssemblyIdentityCache =
            new ConcurrentDictionary<string, ActualAssemblyIdentity?>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, ImmutableDictionary<string, ActualMethodIdentity>> MethodIdentityCache =
            new ConcurrentDictionary<string, ImmutableDictionary<string, ActualMethodIdentity>>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string?> RuntimeImplementationAssemblyPathCache =
            new ConcurrentDictionary<string, string?>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string> RuntimeImplementationAssemblyPathByAssemblyNameCache =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private readonly ImmutableDictionary<string, ImmutableArray<SummaryEntry>> _entriesBySymbol;

        private ExceptionSummaryCatalog(ImmutableDictionary<string, ImmutableArray<SummaryEntry>> entriesBySymbol)
        {
            _entriesBySymbol = entriesBySymbol;
        }

        private bool IsEmpty => _entriesBySymbol.IsEmpty;

        public static ExceptionSummaryCatalog FromOptions(AnalyzerOptions options, CancellationToken cancellationToken)
        {
            if (!BuiltInEffectSummaryLoader.HasAdditionalSummaryJsonDocuments(options))
            {
                return BuiltInCatalog.Value;
            }

            var entriesBySymbol = CreateMutableEntries(BuiltInCatalog.Value);
            BuiltInEffectSummaryLoader.LoadAdditionalSummaryJsonDocuments(
                options,
                cancellationToken,
                json => AddParsedEntries(entriesBySymbol, json, AdditionalSummarySourcePriority));
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

            var matchedExceptionSources = new Dictionary<string, ImmutableSortedSet<string>.Builder>(StringComparer.Ordinal);
            var matchedExceptionEdges = new Dictionary<string, Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>>(StringComparer.Ordinal);
            var actualAssemblyIdentity = compilation is null
                ? null
                : TryResolveActualAssemblyIdentity(methodSymbol, compilation);
            var actualMethodIdentity = compilation is null
                ? null
                : TryResolveActualMethodIdentity(methodSymbol, compilation);

            foreach (var key in GetSymbolKeys(methodSymbol))
            {
                if (!_entriesBySymbol.TryGetValue(key, out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (!entry.IsTrustedFor(methodSymbol, actualAssemblyIdentity, actualMethodIdentity))
                    {
                        continue;
                    }

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
                                edgeMap = new Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>(SummaryExceptionEdgeInfoComparer.Instance);
                                matchedExceptionEdges.Add(exceptionInfo.ExceptionType, edgeMap);
                            }

                            foreach (var edge in exceptionInfo.Edges)
                            {
                                edgeMap[edge] = edge;
                            }
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
                            .ThenBy(edge => edge.CalleeExactSymbolKey, StringComparer.Ordinal)
                            .ThenBy(edge => edge.SourcePath, StringComparer.Ordinal)
                            .ToImmutableArray()
                        : ImmutableArray<SummaryExceptionEdgeInfo>.Empty))
                .ToImmutableArray();
            return true;
        }

        private static ExceptionSummaryCatalog CreateBuiltInCatalog()
        {
            var entriesBySymbol = new Dictionary<string, ImmutableArray<SummaryEntry>.Builder>(StringComparer.Ordinal);
            BuiltInEffectSummaryLoader.LoadBuiltInSummaryJsonDocuments(
                json => AddParsedEntries(entriesBySymbol, json, BuiltInSummarySourcePriority));
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
            if (entriesBySymbol.Count == 0)
            {
                return Empty;
            }

            return new ExceptionSummaryCatalog(entriesBySymbol.ToImmutableDictionary(
                item => item.Key,
                item => item.Value.ToImmutable(),
                StringComparer.Ordinal));
        }

        private static void AddParsedEntries(
            Dictionary<string, ImmutableArray<SummaryEntry>.Builder> entriesBySymbol,
            string json,
            int sourcePriority)
        {
            try
            {
                foreach (var entry in ParseEntries(json, sourcePriority))
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

        private static IEnumerable<SummaryEntry> ParseEntries(string json, int sourcePriority)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Assemblies", out var assembliesElement) ||
                assembliesElement.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var assemblyElement in assembliesElement.EnumerateArray())
            {
                var assemblyIdentity = SummaryAssemblyIdentity.FromJson(assemblyElement);
                if (!assemblyElement.TryGetProperty("Methods", out var methodsElement) ||
                    methodsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var methodElement in methodsElement.EnumerateArray())
                {
                    var symbol = CompatibilityHelpers.GetTrimmedStringProperty(methodElement, "Symbol");
                    if (symbol == null)
                    {
                        continue;
                    }

                    var exceptionFacts = ParseExceptionFacts(methodElement);
                    var exceptionTypes = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                    var exceptionSources = new Dictionary<string, ImmutableSortedSet<string>.Builder>(StringComparer.Ordinal);
                    var exceptionEdges = new Dictionary<string, Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>>(StringComparer.Ordinal);
                    exceptionTypes.UnionWith(GetExceptionTypes(methodElement, "ThrownExceptionTypes"));
                    exceptionTypes.UnionWith(GetExceptionTypes(methodElement, "TransitiveThrownExceptionTypes"));
                    AddExceptionSources(exceptionTypes, exceptionSources, methodElement, "ThrownExceptionSourcePaths");
                    AddExceptionSources(exceptionTypes, exceptionSources, methodElement, "TransitiveThrownExceptionSourcePaths");
                    AddExceptionEdges(exceptionTypes, exceptionSources, exceptionEdges, methodElement, "ThrownExceptionEdges");
                    AddExceptionEdges(exceptionTypes, exceptionSources, exceptionEdges, methodElement, "TransitiveThrownExceptionEdges");
                    if (exceptionTypes.Count == 0)
                    {
                        continue;
                    }

                    var exceptionInfos = exceptionTypes
                        .Select(exceptionType => new SummaryExceptionInfo(
                            exceptionType,
                            exceptionSources.TryGetValue(exceptionType, out var sources)
                                ? sources.ToImmutableArray()
                                : ImmutableArray<string>.Empty,
                            exceptionEdges.TryGetValue(exceptionType, out var edges)
                                ? edges.Values
                                    .OrderBy(edge => edge.Depth)
                                    .ThenBy(edge => edge.CalleeExactSymbolKey, StringComparer.Ordinal)
                                    .ThenBy(edge => edge.SourcePath, StringComparer.Ordinal)
                                    .ToImmutableArray()
                                : ImmutableArray<SummaryExceptionEdgeInfo>.Empty))
                        .ToImmutableArray();
                    yield return new SummaryEntry(
                        symbol,
                        exceptionInfos,
                        exceptionFacts,
                        assemblyIdentity,
                        SummaryMethodIdentity.FromJson(methodElement),
                        sourcePriority);
                }
            }
        }

        private static ImmutableArray<SummaryExceptionFact> ParseExceptionFacts(JsonElement methodElement)
        {
            var directExceptionTypes = GetExceptionTypes(methodElement, "ThrownExceptionTypes");
            var directExceptionSourceKeys = GetExceptionSourceKeys(methodElement, "ThrownExceptionSourcePaths");
            var factMap = new Dictionary<SummaryExceptionFact, SummaryExceptionFact>(SummaryExceptionFactComparer.Instance);
            AddExceptionTypeFacts(factMap, methodElement, "ThrownExceptionTypes", static _ => SummaryExceptionOriginKind.Direct);
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
                "ThrownExceptionSourcePaths",
                static (_, _) => SummaryExceptionOriginKind.Direct);
            AddExceptionSourceFacts(
                factMap,
                methodElement,
                "TransitiveThrownExceptionSourcePaths",
                (exceptionType, sourcePath) =>
                    sourcePath != null && directExceptionSourceKeys.Contains(CreateExceptionFactSourceKey(exceptionType, sourcePath))
                        ? SummaryExceptionOriginKind.Direct
                        : SummaryExceptionOriginKind.Transitive);
            AddExceptionEdgeFacts(
                factMap,
                methodElement,
                "ThrownExceptionEdges",
                static (_, _, _, _) => SummaryExceptionOriginKind.Direct);
            AddExceptionEdgeFacts(
                factMap,
                methodElement,
                "TransitiveThrownExceptionEdges",
                (exceptionType, sourcePath, calleeExactSymbolKey, depth) =>
                    IsDirectExceptionEdge(sourcePath, calleeExactSymbolKey, depth, directExceptionSourceKeys, exceptionType)
                        ? SummaryExceptionOriginKind.Direct
                        : SummaryExceptionOriginKind.Transitive);

            PruneRedundantTypeOnlyFacts(factMap);

            return factMap.Count == 0
                ? ImmutableArray<SummaryExceptionFact>.Empty
                : factMap.Values
                    .OrderBy(fact => fact.ExceptionType, StringComparer.Ordinal)
                    .ThenBy(fact => fact.OriginKind)
                    .ThenBy(fact => fact.Depth ?? int.MinValue)
                    .ThenBy(fact => fact.CalleeExactSymbolKey, StringComparer.Ordinal)
                    .ThenBy(fact => fact.SourcePath, StringComparer.Ordinal)
                    .ToImmutableArray();
        }

        private static void PruneRedundantTypeOnlyFacts(
            Dictionary<SummaryExceptionFact, SummaryExceptionFact> factMap)
        {
            var redundantFacts = factMap.Values
                .Where(fact =>
                    fact.SourcePath == null &&
                    fact.CalleeExactSymbolKey == null &&
                    fact.Depth == null &&
                    factMap.Values.Any(other =>
                        !ReferenceEquals(other, fact) &&
                        string.Equals(other.ExceptionType, fact.ExceptionType, StringComparison.Ordinal) &&
                        other.OriginKind == fact.OriginKind &&
                        (other.SourcePath != null || other.CalleeExactSymbolKey != null || other.Depth != null)))
                .ToArray();

            foreach (var redundantFact in redundantFacts)
            {
                factMap.Remove(redundantFact);
            }
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
                    sourcePath: null,
                    calleeExactSymbolKey: null,
                    depth: null);
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
                if (!TryGetExceptionTypeAndSourcePath(valueElement, out var exceptionType, out var sourcePath))
                {
                    continue;
                }

                exceptionTypes.Add(exceptionType);
                if (sourcePath == null)
                {
                    continue;
                }

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
                if (!TryGetExceptionTypeAndSourcePath(valueElement, out var exceptionType, out var sourcePath))
                {
                    continue;
                }

                var fact = new SummaryExceptionFact(
                    exceptionType,
                    getOriginKind(exceptionType, sourcePath),
                    sourcePath,
                    calleeExactSymbolKey: null,
                    depth: null);
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
                if (!TryGetExceptionType(valueElement, out var exceptionType))
                {
                    continue;
                }

                exceptionTypes.Add(exceptionType);

                var sourcePath = GetEdgeSourcePath(valueElement);
                if (sourcePath == null)
                {
                    continue;
                }

                if (!exceptionSources.TryGetValue(exceptionType, out var sources))
                {
                    sources = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                    exceptionSources.Add(exceptionType, sources);
                }

                sources.Add(sourcePath);

                if (!exceptionEdges.TryGetValue(exceptionType, out var edgeMap))
                {
                    edgeMap = new Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>(SummaryExceptionEdgeInfoComparer.Instance);
                    exceptionEdges.Add(exceptionType, edgeMap);
                }

                var edge = new SummaryExceptionEdgeInfo(
                    sourcePath,
                    GetEdgeCalleeExactSymbolKey(valueElement),
                    TryGetOptionalInt32(valueElement, "Depth"));
                edgeMap[edge] = edge;
            }
        }

        private static void AddExceptionEdgeFacts(
            Dictionary<SummaryExceptionFact, SummaryExceptionFact> factMap,
            JsonElement methodElement,
            string propertyName,
            Func<string, string?, string?, int?, SummaryExceptionOriginKind> getOriginKind)
        {
            foreach (var valueElement in EnumerateObjectArrayProperty(methodElement, propertyName))
            {
                if (!TryGetExceptionType(valueElement, out var exceptionType))
                {
                    continue;
                }

                var sourcePath = GetEdgeSourcePath(valueElement);
                var calleeExactSymbolKey = GetEdgeCalleeExactSymbolKey(valueElement);
                var depth = TryGetOptionalInt32(valueElement, "Depth");
                var fact = new SummaryExceptionFact(
                    exceptionType,
                    getOriginKind(exceptionType, sourcePath, calleeExactSymbolKey, depth),
                    sourcePath,
                    calleeExactSymbolKey,
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
                {
                    continue;
                }

                sourceKeys.Add(CreateExceptionFactSourceKey(exceptionType, sourcePath));
            }

            return sourceKeys;
        }

        private static bool IsDirectExceptionEdge(
            string? sourcePath,
            string? calleeExactSymbolKey,
            int? depth,
            HashSet<string> directExceptionSourceKeys,
            string exceptionType)
        {
            if (depth == 0 && calleeExactSymbolKey == null)
            {
                return true;
            }

            return sourcePath != null &&
                calleeExactSymbolKey == null &&
                directExceptionSourceKeys.Contains(CreateExceptionFactSourceKey(exceptionType, sourcePath));
        }

        private static string CreateExceptionFactSourceKey(string exceptionType, string sourcePath)
        {
            return exceptionType + "|" + sourcePath;
        }

        private static IEnumerable<string> EnumerateTrimmedStringArrayValues(JsonElement element, string propertyName)
        {
            if (!TryGetArrayProperty(element, propertyName, out var valuesElement))
            {
                yield break;
            }

            foreach (var valueElement in valuesElement.EnumerateArray())
            {
                if (valueElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = valueElement.GetString();
                if (value != null && !string.IsNullOrWhiteSpace(value))
                {
                    yield return value.Trim();
                }
            }
        }

        private static IEnumerable<JsonElement> EnumerateObjectArrayProperty(JsonElement element, string propertyName)
        {
            if (!TryGetArrayProperty(element, propertyName, out var valuesElement))
            {
                yield break;
            }

            foreach (var valueElement in valuesElement.EnumerateArray())
            {
                if (valueElement.ValueKind == JsonValueKind.Object)
                {
                    yield return valueElement;
                }
            }
        }

        private static bool TryGetArrayProperty(JsonElement element, string propertyName, out JsonElement valuesElement)
        {
            if (element.TryGetProperty(propertyName, out valuesElement) &&
                valuesElement.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

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
            return CompatibilityHelpers.GetTrimmedStringProperty(element, "SourcePath") ??
                CompatibilityHelpers.GetTrimmedStringProperty(element, "ExceptionSourcePath") ??
                CompatibilityHelpers.GetTrimmedStringProperty(element, "CallPath") ??
                CompatibilityHelpers.GetTrimmedStringProperty(element, "CalleeExactSymbolKey") ??
                CompatibilityHelpers.GetTrimmedStringProperty(element, "CalleeSymbol");
        }

        private static string? GetEdgeCalleeExactSymbolKey(JsonElement element)
        {
            return CompatibilityHelpers.GetTrimmedStringProperty(element, "CalleeExactSymbolKey") ??
                CompatibilityHelpers.GetTrimmedStringProperty(element, "CalleeSymbol");
        }

        private static int? TryGetOptionalInt32(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var valueElement))
            {
                return null;
            }

            return valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetInt32(out var number)
                ? number
                : null;
        }

        private static IEnumerable<string> GetSymbolKeys(IMethodSymbol methodSymbol)
        {
            return EffectSummarySymbolKeyFactory.GetMethodSymbolKeys(methodSymbol);
        }

        private static string CreateEffectSummaryKey(IMethodSymbol methodSymbol)
        {
            var containingTypeName = methodSymbol.ContainingType.ToDisplayString(EffectSummaryContainingTypeFormat);
            var methodName = methodSymbol.MethodKind == MethodKind.Constructor
                ? ".ctor"
                : methodSymbol.Name;
            var parameterList = string.Join(
                ", ",
                methodSymbol.Parameters.Select(parameter => parameter.Type.ToDisplayString(EffectSummaryParameterTypeFormat)));
            return containingTypeName + "." + methodName + "(" + parameterList + ")";
        }

        private static ActualMethodIdentity? TryResolveActualMethodIdentity(
            IMethodSymbol methodSymbol,
            Compilation compilation)
        {
            var implementationPath = TryResolveRuntimeImplementationAssemblyPath(methodSymbol);
            if (!string.IsNullOrWhiteSpace(implementationPath))
            {
                var path = implementationPath!;
                if (File.Exists(path) &&
                    TryResolveMethodIdentityFromPath(methodSymbol, path, out var implementationIdentity))
                {
                    return implementationIdentity;
                }
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            {
                return null;
            }

            foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
            {
                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                if (assemblySymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(assemblySymbol, methodSymbol.ContainingAssembly))
                {
                    continue;
                }

                var referencePath = reference.FilePath;
                if (string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath))
                {
                    return null;
                }

                var path = referencePath!;
                var methodMap = MethodIdentityCache.GetOrAdd(path, static resolvedPath => LoadMethodIdentities(resolvedPath));
                foreach (var key in GetSymbolKeys(methodSymbol))
                {
                    if (methodMap.TryGetValue(key, out var identity))
                    {
                        return identity;
                    }
                }

                return null;
            }

            return null;
        }

        private static bool TryResolveMethodIdentityFromPath(
            IMethodSymbol methodSymbol,
            string assemblyPath,
            out ActualMethodIdentity identity)
        {
            identity = null!;
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            {
                return false;
            }

            var methodMap = MethodIdentityCache.GetOrAdd(assemblyPath, static path => LoadMethodIdentities(path));
            foreach (var key in GetSymbolKeys(methodSymbol))
            {
                if (methodMap.TryGetValue(key, out var foundIdentity))
                {
                    identity = foundIdentity;
                    return true;
                }
            }

            return false;
        }

        private static ImmutableDictionary<string, ActualMethodIdentity> LoadMethodIdentities(string path)
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return ImmutableDictionary<string, ActualMethodIdentity>.Empty;
            }

            var metadataReader = peReader.GetMetadataReader();
            var builder = ImmutableDictionary.CreateBuilder<string, ActualMethodIdentity>(StringComparer.Ordinal);
            var methodBodyHashProvider = new MethodBodyHashProvider(path);
            foreach (var handle in metadataReader.MethodDefinitions)
            {
                var definition = metadataReader.GetMethodDefinition(handle);
                var token = "0x" + MetadataTokens.GetToken(handle).ToString("X8");
                var identity = new ActualMethodIdentity(
                    token,
                    methodBodyHashProvider,
                    definition.RelativeVirtualAddress);
                foreach (var key in GetMethodKeys(metadataReader, handle))
                {
                    builder[key] = identity;
                }
            }

            return builder.ToImmutable();
        }

        private static IEnumerable<string> GetMethodKeys(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var raw = GetMethodSymbol(reader, handle);
            yield return raw;

            var effectSummaryKey = GetEffectSummaryLikeMethodSymbol(reader, handle);
            if (!string.Equals(effectSummaryKey, raw, StringComparison.Ordinal))
            {
                yield return effectSummaryKey;
            }

            var positionalEffectSummaryKey = GetPositionalEffectSummaryLikeMethodSymbol(reader, handle);
            if (!string.Equals(positionalEffectSummaryKey, raw, StringComparison.Ordinal) &&
                !string.Equals(positionalEffectSummaryKey, effectSummaryKey, StringComparison.Ordinal))
            {
                yield return positionalEffectSummaryKey;
            }

            var exactKey = GetExactMethodKey(reader, handle);
            if (!string.Equals(exactKey, raw, StringComparison.Ordinal) &&
                !string.Equals(exactKey, effectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(exactKey, positionalEffectSummaryKey, StringComparison.Ordinal))
            {
                yield return exactKey;
            }

            var positionalExactKey = GetPositionalExactMethodKey(reader, handle);
            if (!string.Equals(positionalExactKey, raw, StringComparison.Ordinal) &&
                !string.Equals(positionalExactKey, effectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(positionalExactKey, positionalEffectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(positionalExactKey, exactKey, StringComparison.Ordinal))
            {
                yield return positionalExactKey;
            }

            var roslynDisplay = GetRoslynLikeMethodSymbol(reader, handle);
            if (!string.Equals(roslynDisplay, raw, StringComparison.Ordinal) &&
                !string.Equals(roslynDisplay, effectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(roslynDisplay, positionalEffectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(roslynDisplay, exactKey, StringComparison.Ordinal) &&
                !string.Equals(roslynDisplay, positionalExactKey, StringComparison.Ordinal))
            {
                yield return roslynDisplay;
            }
        }

        private static string GetMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = SummaryMetadataNames.GetTypeName(reader, definition.GetDeclaringType());
            var methodName = reader.GetString(definition.Name);
            var signature = DecodeMethodSignature(reader, definition);
            return typeName + "." + methodName + signature;
        }

        private static string DecodeMethodSignature(MetadataReader reader, MethodDefinition definition)
        {
            try
            {
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(), CreateGenericContext(reader, definition));
                return "(" + string.Join(", ", signature.ParameterTypes) + ")";
            }
            catch (BadImageFormatException)
            {
                return "(?)";
            }
        }

        private static string DecodePositionalMethodSignature(MetadataReader reader, MethodDefinition definition)
        {
            try
            {
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(), genericContext: null);
                return "(" + string.Join(", ", signature.ParameterTypes) + ")";
            }
            catch (BadImageFormatException)
            {
                return "(?)";
            }
        }

        private static string DecodeExactMethodSignature(MetadataReader reader, MethodDefinition definition)
        {
            try
            {
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(), CreateGenericContext(reader, definition));
                return "(" + string.Join(", ", signature.ParameterTypes) + ")->" + signature.ReturnType;
            }
            catch (BadImageFormatException)
            {
                return "(?)->?";
            }
        }

        private static string DecodePositionalExactMethodSignature(MetadataReader reader, MethodDefinition definition)
        {
            try
            {
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(), genericContext: null);
                return "(" + string.Join(", ", signature.ParameterTypes) + ")->" + signature.ReturnType;
            }
            catch (BadImageFormatException)
            {
                return "(?)->?";
            }
        }

        private static string GetEffectSummaryLikeMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = SummaryMetadataNames.GetTypeName(reader, definition.GetDeclaringType());
            return typeName + "." + reader.GetString(definition.Name) + DecodeMethodSignature(reader, definition);
        }

        private static string GetPositionalEffectSummaryLikeMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = SummaryMetadataNames.GetTypeName(reader, definition.GetDeclaringType());
            return typeName + "." + reader.GetString(definition.Name) + DecodePositionalMethodSignature(reader, definition);
        }

        private static string GetExactMethodKey(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = SummaryMetadataNames.NormalizeExactTypeName(SummaryMetadataNames.GetTypeName(reader, definition.GetDeclaringType()));
            return typeName + "." + reader.GetString(definition.Name) + DecodeExactMethodSignature(reader, definition);
        }

        private static string GetPositionalExactMethodKey(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = SummaryMetadataNames.NormalizeExactTypeName(SummaryMetadataNames.GetTypeName(reader, definition.GetDeclaringType()));
            return typeName + "." + reader.GetString(definition.Name) + DecodePositionalExactMethodSignature(reader, definition);
        }

        private static string GetRoslynLikeMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = SummaryMetadataNames.GetTypeName(reader, definition.GetDeclaringType());
            var rawMethodName = reader.GetString(definition.Name);
            var methodName = rawMethodName;

            if (string.Equals(rawMethodName, ".ctor", StringComparison.Ordinal))
            {
                var lastSeparator = typeName.LastIndexOfAny(new[] { '.', '+' });
                methodName = lastSeparator >= 0 ? typeName.Substring(lastSeparator + 1) : typeName;
            }
            else if (rawMethodName.StartsWith("get_", StringComparison.Ordinal))
            {
                methodName = rawMethodName.Substring(4) + ".get";
            }
            else if (rawMethodName.StartsWith("set_", StringComparison.Ordinal))
            {
                methodName = rawMethodName.Substring(4) + ".set";
            }
            else
            {
                var genericNames = definition.GetGenericParameters()
                    .Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray();
                if (genericNames.Length > 0)
                {
                    methodName += "<" + string.Join(", ", genericNames) + ">";
                }
            }

            return typeName + "." + methodName + DecodeMethodSignature(reader, definition);
        }

        private static GenericContext CreateGenericContext(MetadataReader reader, MethodDefinition definition)
        {
            var typeDefinition = reader.GetTypeDefinition(definition.GetDeclaringType());
            var typeParameters = typeDefinition.GetGenericParameters()
                .Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name))
                .ToImmutableArray();
            var methodParameters = definition.GetGenericParameters()
                .Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name))
                .ToImmutableArray();
            return new GenericContext(typeParameters, methodParameters);
        }

        private sealed class EffectSummaryTypeNameProvider : ISignatureTypeProvider<string, object?>
        {
            public string GetArrayType(string elementType, ArrayShape shape)
            {
                var rank = Math.Max(shape.Rank, 1);
                return elementType + "[" + new string(',', rank - 1) + "]";
            }

            public string GetByReferenceType(string elementType) => "ref " + elementType;

            public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";

            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
                => genericType + "<" + string.Join(", ", typeArguments) + ">";

            public string GetGenericMethodParameter(object? genericContext, int index)
            {
                var context = genericContext as GenericContext;
                return context != null && index >= 0 && index < context.MethodParameters.Length
                    ? context.MethodParameters[index]
                    : "!!" + index;
            }

            public string GetGenericTypeParameter(object? genericContext, int index)
            {
                var context = genericContext as GenericContext;
                return context != null && index >= 0 && index < context.TypeParameters.Length
                    ? context.TypeParameters[index]
                    : "!" + index;
            }

            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

            public string GetPinnedType(string elementType) => elementType;

            public string GetPointerType(string elementType) => elementType + "*";

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

            public string GetSZArrayType(string elementType) => elementType + "[]";

            public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind)
                => SummaryMetadataNames.GetTypeName(metadataReader, handle);

            public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind)
                => SummaryMetadataNames.GetTypeReferenceName(metadataReader, handle);

            public string GetTypeFromSpecification(
                MetadataReader metadataReader,
                object? genericContext,
                TypeSpecificationHandle handle,
                byte rawTypeKind)
            {
                return metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
            }
        }

        private sealed class GenericContext
        {
            public GenericContext(ImmutableArray<string> typeParameters, ImmutableArray<string> methodParameters)
            {
                TypeParameters = typeParameters;
                MethodParameters = methodParameters;
            }

            public ImmutableArray<string> TypeParameters { get; }

            public ImmutableArray<string> MethodParameters { get; }
        }

        private static ActualAssemblyIdentity? TryResolveActualAssemblyIdentity(
            IMethodSymbol methodSymbol,
            Compilation compilation)
        {
            var implementationPath = TryResolveRuntimeImplementationAssemblyPath(methodSymbol);
            if (!string.IsNullOrWhiteSpace(implementationPath))
            {
                var path = implementationPath!;
                if (File.Exists(path) &&
                    TryResolveMethodIdentityFromPath(methodSymbol, path, out _))
                {
                    return AssemblyIdentityCache.GetOrAdd(path, static resolvedPath => ActualAssemblyIdentity.FromFile(resolvedPath));
                }
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            {
                return null;
            }

            foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
            {
                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                if (assemblySymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(assemblySymbol, methodSymbol.ContainingAssembly))
                {
                    continue;
                }

                var referencePath = reference.FilePath;
                if (string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath))
                {
                    return null;
                }

                var path = referencePath!;
                return AssemblyIdentityCache.GetOrAdd(path, static resolvedPath => ActualAssemblyIdentity.FromFile(resolvedPath));
            }

            return null;
        }

        private static string? TryResolveRuntimeImplementationAssemblyPath(IMethodSymbol methodSymbol)
        {
            var cacheKey = CreateEffectSummaryKey(methodSymbol.OriginalDefinition);
            return RuntimeImplementationAssemblyPathCache.GetOrAdd(cacheKey, _ => ResolveRuntimeImplementationAssemblyPath(methodSymbol));
        }

        private static string? ResolveRuntimeImplementationAssemblyPath(IMethodSymbol methodSymbol)
        {
            var coreLibPath = typeof(object).Assembly.Location;
            if (!string.IsNullOrWhiteSpace(coreLibPath) &&
                File.Exists(coreLibPath) &&
                TryResolveMethodIdentityFromPath(methodSymbol, coreLibPath, out _))
            {
                return coreLibPath;
            }

            var assemblyName = methodSymbol.ContainingAssembly?.Identity.Name;
            if (!string.IsNullOrWhiteSpace(assemblyName) &&
                RuntimeImplementationAssemblyPathByAssemblyNameCache.TryGetValue(assemblyName!, out var cachedAssemblyPath) &&
                File.Exists(cachedAssemblyPath) &&
                TryResolveMethodIdentityFromPath(methodSymbol, cachedAssemblyPath, out _))
            {
                return cachedAssemblyPath;
            }

            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var location = assembly.Location;
                    if (!string.IsNullOrWhiteSpace(location) &&
                        File.Exists(location) &&
                        TryResolveMethodIdentityFromPath(methodSymbol, location, out _))
                    {
                        RuntimeImplementationAssemblyPathByAssemblyNameCache[assemblyName!] = location;
                        return location;
                    }
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var location = assembly.Location;
                if (string.IsNullOrWhiteSpace(location) ||
                    !File.Exists(location) ||
                    string.Equals(location, coreLibPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryResolveMethodIdentityFromPath(methodSymbol, location, out _))
                {
                    if (!string.IsNullOrWhiteSpace(assemblyName))
                    {
                        RuntimeImplementationAssemblyPathByAssemblyNameCache[assemblyName!] = location;
                    }

                    return location;
                }
            }

            foreach (var trustedPlatformAssemblyPath in RuntimeMetadataAssemblyLocator.GetTrustedPlatformAssemblyPaths())
            {
                if (string.Equals(trustedPlatformAssemblyPath, coreLibPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryResolveMethodIdentityFromPath(methodSymbol, trustedPlatformAssemblyPath, out _))
                {
                    if (!string.IsNullOrWhiteSpace(assemblyName))
                    {
                        RuntimeImplementationAssemblyPathByAssemblyNameCache[assemblyName!] = trustedPlatformAssemblyPath;
                    }

                    return trustedPlatformAssemblyPath;
                }
            }

            return null;
        }

        private sealed class SummaryEntry
        {
            public SummaryEntry(
                string symbol,
                ImmutableArray<SummaryExceptionInfo> exceptionInfos,
                ImmutableArray<SummaryExceptionFact> exceptionFacts,
                SummaryAssemblyIdentity? assemblyIdentity,
                SummaryMethodIdentity? methodIdentity,
                int sourcePriority)
            {
                Symbol = symbol;
                ExceptionInfos = exceptionInfos;
                ExceptionFacts = exceptionFacts;
                AssemblyIdentity = assemblyIdentity;
                MethodIdentity = methodIdentity;
                SourcePriority = sourcePriority;
            }

            public string Symbol { get; }

            public ImmutableArray<SummaryExceptionInfo> ExceptionInfos { get; }

            public ImmutableArray<SummaryExceptionFact> ExceptionFacts { get; }

            public SummaryAssemblyIdentity? AssemblyIdentity { get; }

            public SummaryMethodIdentity? MethodIdentity { get; }

            public int SourcePriority { get; }

            public bool IsTrustedFor(
                IMethodSymbol methodSymbol,
                ActualAssemblyIdentity? actualAssemblyIdentity,
                ActualMethodIdentity? actualMethodIdentity)
            {
                if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
                {
                    return false;
                }

                if (AssemblyIdentity == null ||
                    !AssemblyIdentity.IsComplete ||
                    MethodIdentity == null ||
                    actualAssemblyIdentity == null ||
                    actualMethodIdentity == null ||
                    !AssemblyIdentity.Matches(actualAssemblyIdentity) ||
                    !MethodIdentity.MatchesMetadataToken(actualMethodIdentity))
                {
                    return false;
                }

                if (SourcePriority == BuiltInSummarySourcePriority)
                {
                    return true;
                }

                return MethodIdentity.IsCompleteEnoughFor(actualMethodIdentity) &&
                    MethodIdentity.Matches(actualMethodIdentity);
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
            Transitive = 1,
        }

        internal sealed class SummaryExceptionFact
        {
            public SummaryExceptionFact(
                string exceptionType,
                SummaryExceptionOriginKind originKind,
                string? sourcePath,
                string? calleeExactSymbolKey,
                int? depth)
            {
                ExceptionType = exceptionType;
                OriginKind = originKind;
                SourcePath = sourcePath;
                CalleeExactSymbolKey = calleeExactSymbolKey;
                Depth = depth;
            }

            public string ExceptionType { get; }

            public SummaryExceptionOriginKind OriginKind { get; }

            public string? SourcePath { get; }

            public string? CalleeExactSymbolKey { get; }

            public int? Depth { get; }
        }

        internal sealed class SummaryExceptionEdgeInfo
        {
            public SummaryExceptionEdgeInfo(
                string? sourcePath,
                string? calleeExactSymbolKey,
                int? depth)
            {
                SourcePath = sourcePath;
                CalleeExactSymbolKey = calleeExactSymbolKey;
                Depth = depth;
            }

            public string? SourcePath { get; }

            public string? CalleeExactSymbolKey { get; }

            public int? Depth { get; }
        }

        private sealed class SummaryExceptionEdgeInfoComparer : IEqualityComparer<SummaryExceptionEdgeInfo>
        {
            public static readonly SummaryExceptionEdgeInfoComparer Instance = new SummaryExceptionEdgeInfoComparer();

            public bool Equals(SummaryExceptionEdgeInfo? x, SummaryExceptionEdgeInfo? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null || y is null)
                {
                    return false;
                }

                return string.Equals(x.SourcePath, y.SourcePath, StringComparison.Ordinal) &&
                    string.Equals(x.CalleeExactSymbolKey, y.CalleeExactSymbolKey, StringComparison.Ordinal) &&
                    x.Depth == y.Depth;
            }

            public int GetHashCode(SummaryExceptionEdgeInfo obj)
            {
                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + (obj.SourcePath != null ? StringComparer.Ordinal.GetHashCode(obj.SourcePath) : 0);
                    hash = (hash * 31) + (obj.CalleeExactSymbolKey != null ? StringComparer.Ordinal.GetHashCode(obj.CalleeExactSymbolKey) : 0);
                    hash = (hash * 31) + (obj.Depth ?? 0);
                    return hash;
                }
            }
        }

        private sealed class SummaryExceptionFactComparer : IEqualityComparer<SummaryExceptionFact>
        {
            public static readonly SummaryExceptionFactComparer Instance = new SummaryExceptionFactComparer();

            public bool Equals(SummaryExceptionFact? x, SummaryExceptionFact? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null || y is null)
                {
                    return false;
                }

                return string.Equals(x.ExceptionType, y.ExceptionType, StringComparison.Ordinal) &&
                    x.OriginKind == y.OriginKind &&
                    string.Equals(x.SourcePath, y.SourcePath, StringComparison.Ordinal) &&
                    string.Equals(x.CalleeExactSymbolKey, y.CalleeExactSymbolKey, StringComparison.Ordinal) &&
                    x.Depth == y.Depth;
            }

            public int GetHashCode(SummaryExceptionFact obj)
            {
                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.ExceptionType);
                    hash = (hash * 31) + (int)obj.OriginKind;
                    hash = (hash * 31) + (obj.SourcePath != null ? StringComparer.Ordinal.GetHashCode(obj.SourcePath) : 0);
                    hash = (hash * 31) + (obj.CalleeExactSymbolKey != null ? StringComparer.Ordinal.GetHashCode(obj.CalleeExactSymbolKey) : 0);
                    hash = (hash * 31) + (obj.Depth ?? 0);
                    return hash;
                }
            }
        }

    }
}
