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
    internal sealed class GeneratedPurityCatalog
    {
        private const int BuiltInSummarySourcePriority = 0;
        private const int AdditionalSummarySourcePriority = 1;
        private static readonly AsyncLocal<GeneratedPurityCatalog?> CurrentCatalog = new AsyncLocal<GeneratedPurityCatalog?>();
        private static readonly Lazy<GeneratedPurityCatalog> BuiltInCatalog =
            new Lazy<GeneratedPurityCatalog>(CreateBuiltInCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

        public static readonly GeneratedPurityCatalog Empty = new GeneratedPurityCatalog(
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

        private GeneratedPurityCatalog(ImmutableDictionary<string, ImmutableArray<SummaryEntry>> entriesBySymbol)
        {
            _entriesBySymbol = entriesBySymbol;
        }

        private bool IsEmpty => _entriesBySymbol.IsEmpty;

        public static GeneratedPurityCatalog Current => CurrentCatalog.Value ?? BuiltInCatalog.Value;

        public static GeneratedPurityCatalog FromOptions(AnalyzerOptions options, CancellationToken cancellationToken)
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

        private static GeneratedPurityCatalog CreateBuiltInCatalog()
        {
            var entriesBySymbol = new Dictionary<string, ImmutableArray<SummaryEntry>.Builder>(StringComparer.Ordinal);
            BuiltInEffectSummaryLoader.LoadBuiltInSummaryJsonDocuments(
                json => AddParsedEntries(entriesBySymbol, json, BuiltInSummarySourcePriority));
            return CreateCatalog(entriesBySymbol);
        }

        public static IDisposable UseCurrent(GeneratedPurityCatalog catalog)
        {
            return new Scope(CurrentCatalog.Value, catalog.IsEmpty ? null : catalog);
        }

        private static Dictionary<string, ImmutableArray<SummaryEntry>.Builder> CreateMutableEntries(
            GeneratedPurityCatalog catalog)
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

        private static GeneratedPurityCatalog CreateCatalog(
            Dictionary<string, ImmutableArray<SummaryEntry>.Builder> entriesBySymbol)
        {
            if (entriesBySymbol.Count == 0)
            {
                return Empty;
            }

            return new GeneratedPurityCatalog(entriesBySymbol.ToImmutableDictionary(
                item => item.Key,
                item => item.Value.ToImmutable(),
                StringComparer.Ordinal));
        }

        public bool TryGetPurity(IMethodSymbol methodSymbol, Compilation compilation, out PurityEntry classification)
        {
            classification = default;
            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            {
                return false;
            }

            if (TryGetImplicitMetadataValueTypeConstructorPurity(methodSymbol, out classification))
            {
                return true;
            }

            var actualAssemblyIdentity = TryResolveActualAssemblyIdentity(methodSymbol, compilation);
            var actualMethodIdentity = TryResolveActualMethodIdentity(methodSymbol, compilation);
            if (actualAssemblyIdentity == null || actualMethodIdentity == null)
            {
                return false;
            }

            SummaryEntry? bestEntry = null;
            foreach (var key in GetSymbolKeys(methodSymbol))
            {
                if (!_entriesBySymbol.TryGetValue(key, out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (IsBuiltInAbstractInterfaceEntry(methodSymbol, entry))
                    {
                        continue;
                    }

                    if (!entry.IsTrustedFor(methodSymbol, actualAssemblyIdentity, actualMethodIdentity))
                    {
                        continue;
                    }

                    if (bestEntry == null || CompareTrustedPurityEntries(entry, bestEntry) > 0)
                    {
                        bestEntry = entry;
                    }
                }
            }

            if (bestEntry == null)
            {
                return TryGetBuiltInFrameworkEntryByKeyOnly(methodSymbol, out classification);
            }

            classification = bestEntry.Classification;
            return true;
        }

        private bool TryGetBuiltInFrameworkEntryByKeyOnly(IMethodSymbol methodSymbol, out PurityEntry classification)
        {
            classification = default;
            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true ||
                !IsFrameworkAssemblyName(methodSymbol.ContainingAssembly?.Identity.Name))
            {
                return false;
            }

            SummaryEntry? bestEntry = null;
            foreach (var key in GetSymbolKeys(methodSymbol))
            {
                if (!_entriesBySymbol.TryGetValue(key, out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (IsBuiltInAbstractInterfaceEntry(methodSymbol, entry))
                    {
                        continue;
                    }

                    if (entry.SourcePriority != BuiltInSummarySourcePriority ||
                        entry.AssemblyIdentity?.IsComplete != true ||
                        entry.MethodIdentity == null)
                    {
                        continue;
                    }

                    if (bestEntry == null || CompareTrustedPurityEntries(entry, bestEntry) > 0)
                    {
                        bestEntry = entry;
                    }
                }
            }

            if (bestEntry == null)
            {
                return false;
            }

            classification = bestEntry.Classification;
            return true;
        }

        private static bool IsBuiltInAbstractInterfaceEntry(IMethodSymbol methodSymbol, SummaryEntry entry)
        {
            return entry.SourcePriority == BuiltInSummarySourcePriority &&
                methodSymbol.ContainingType?.TypeKind == TypeKind.Interface &&
                (string.Equals(entry.Classification.PrimaryCategory, "abstract", StringComparison.Ordinal) ||
                 entry.Classification.Categories.Contains("abstract", StringComparer.Ordinal));
        }

        internal static bool IsFrameworkAssemblyName(string? assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return false;
            }

            var name = assemblyName!;
            return name == "mscorlib" ||
                name == "netstandard" ||
                name == "System" ||
                name == "System.Private.CoreLib" ||
                name.StartsWith("System.", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.", StringComparison.Ordinal);
        }

        private static bool TryGetImplicitMetadataValueTypeConstructorPurity(IMethodSymbol methodSymbol, out PurityEntry classification)
        {
            classification = default;
            if (methodSymbol.MethodKind != MethodKind.Constructor ||
                !methodSymbol.IsImplicitlyDeclared ||
                methodSymbol.Parameters.Length != 0 ||
                methodSymbol.IsStatic ||
                methodSymbol.ContainingType?.IsValueType != true)
            {
                return false;
            }

            classification = new PurityEntry(
                classification: "pure",
                categories: ImmutableArray<string>.Empty,
                primaryCategory: "implicit_metadata_value_type_constructor",
                hasFreshArrayAllocationEvidence: false,
                freshnessClassification: "none",
                hasUnsupportedEffects: false,
                effectVisibilityClassification: "internal_only");
            return true;
        }

        public bool TryGetFieldPurity(IFieldSymbol fieldSymbol, Compilation compilation, out PurityEntry classification)
        {
            classification = default;
            if (fieldSymbol.Locations.FirstOrDefault()?.IsInMetadata != true || !fieldSymbol.IsStatic)
            {
                return false;
            }

            var staticConstructor = fieldSymbol.ContainingType?
                .GetMembers(".cctor")
                .OfType<IMethodSymbol>()
                .FirstOrDefault();
            if (staticConstructor == null)
            {
                var staticConstructorKeys = GetStaticConstructorKeys(fieldSymbol.ContainingType);
                return TryGetTrustedPurityByMethodKeys(fieldSymbol.ContainingAssembly, staticConstructorKeys, compilation, out classification);
            }

            return TryGetPurity(staticConstructor.OriginalDefinition, compilation, out classification);
        }

        internal bool TryGetSystemTypeRuntimeImplementationPurity(
            IMethodSymbol methodSymbol,
            Compilation compilation,
            out PurityEntry classification)
        {
            classification = default;
            if (methodSymbol == null ||
                methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true ||
                !string.Equals(methodSymbol.ContainingType?.ToDisplayString(), "System.Type", StringComparison.Ordinal))
            {
                return false;
            }

            var runtimeMethodKeys = EffectSummarySymbolKeyFactory.GetMethodSymbolKeysWithAlternateContainingType(
                methodSymbol.OriginalDefinition,
                "System.RuntimeType");
            if (runtimeMethodKeys.IsDefaultOrEmpty)
            {
                return false;
            }

            return TryGetTrustedPurityByMethodKeys(methodSymbol.ContainingAssembly, runtimeMethodKeys, compilation, out classification);
        }

        private static int CompareTrustedPurityEntries(SummaryEntry left, SummaryEntry right)
        {
            var sourcePriorityComparison = left.SourcePriority.CompareTo(right.SourcePriority);
            if (sourcePriorityComparison != 0)
            {
                return sourcePriorityComparison;
            }

            var leftPriority = GetClassificationPriority(left.Classification);
            var rightPriority = GetClassificationPriority(right.Classification);
            var priorityComparison = leftPriority.CompareTo(rightPriority);
            if (priorityComparison != 0)
            {
                return priorityComparison;
            }

            var leftPrimaryCategory = left.Classification.PrimaryCategory ?? string.Empty;
            var rightPrimaryCategory = right.Classification.PrimaryCategory ?? string.Empty;
            var primaryCategoryComparison = string.CompareOrdinal(leftPrimaryCategory, rightPrimaryCategory);
            if (primaryCategoryComparison != 0)
            {
                return primaryCategoryComparison;
            }

            return string.CompareOrdinal(left.Symbol, right.Symbol);
        }

        private static int GetClassificationPriority(PurityEntry classification)
        {
            return classification.Classification switch
            {
                "impure" => 3,
                "pure" => 2,
                "conservative_unknown" => 1,
                _ => 0,
            };
        }

        internal static bool TryCanMetadataMethodBeOverridden(IMethodSymbol methodSymbol, Compilation compilation, out bool canBeOverridden)
        {
            canBeOverridden = false;
            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            {
                return false;
            }

            var actualMethodIdentity = TryResolveActualMethodIdentity(methodSymbol, compilation);
            if (actualMethodIdentity == null)
            {
                return false;
            }

            canBeOverridden = actualMethodIdentity.CanBeOverridden;
            return true;
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
            if (document.RootElement.TryGetProperty("GeneratedPurityCatalog", out var generatedCatalogElement) &&
                generatedCatalogElement.ValueKind == JsonValueKind.Object &&
                generatedCatalogElement.TryGetProperty("Entries", out var entriesElement) &&
                entriesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entryElement in entriesElement.EnumerateArray())
                {
                    var symbol = CompatibilityHelpers.GetTrimmedStringProperty(entryElement, "ExactSymbolKey") ??
                        CompatibilityHelpers.GetTrimmedStringProperty(entryElement, "Symbol");
                    if (string.IsNullOrWhiteSpace(symbol) ||
                        !TryCreatePurityEntry(entryElement, out var purityEntry))
                    {
                        continue;
                    }

                    yield return new SummaryEntry(
                        symbol!.Trim(),
                        purityEntry,
                        SummaryAssemblyIdentity.FromJson(entryElement),
                        SummaryMethodIdentity.FromJson(entryElement),
                        sourcePriority);
                }

                yield break;
            }

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
                    if (symbol == null ||
                        !methodElement.TryGetProperty("PurityClassification", out var purityElement) ||
                        purityElement.ValueKind != JsonValueKind.Object ||
                        !TryCreatePurityEntry(purityElement, out var purityEntry))
                    {
                        continue;
                    }

                    yield return new SummaryEntry(
                        symbol,
                        purityEntry,
                        assemblyIdentity,
                        SummaryMethodIdentity.FromJson(methodElement),
                        sourcePriority);
                }
            }
        }

        private static bool TryCreatePurityEntry(JsonElement element, out PurityEntry purityEntry)
        {
            purityEntry = default;

            var classification = CompatibilityHelpers.GetTrimmedStringProperty(element, "Classification");
            if (string.IsNullOrWhiteSpace(classification))
            {
                return false;
            }

            var categories = ReadStringArray(element, "Categories");
            var primaryCategory = CompatibilityHelpers.GetTrimmedStringProperty(element, "PrimaryCategory");
            var freshnessClassification = CompatibilityHelpers.GetTrimmedStringProperty(element, "FreshnessClassification") ?? "none";
            var effectVisibilityClassification = CompatibilityHelpers.GetTrimmedStringProperty(element, "EffectVisibilityClassification") ?? "unknown";
            purityEntry = new PurityEntry(
                classification!.Trim(),
                categories,
                string.IsNullOrWhiteSpace(primaryCategory)
                    ? PrimaryCategoryFallback(categories)
                    : primaryCategory!.Trim(),
                ReadBooleanProperty(element, "HasFreshArrayAllocationEvidence"),
                freshnessClassification,
                ReadBooleanProperty(element, "HasUnsupportedEffects"),
                effectVisibilityClassification);
            return true;
        }

        private static string PrimaryCategoryFallback(ImmutableArray<string> categories) => categories.Length > 0
            ? categories[0]
            : "generated_purity_summary";

        private static bool ReadBooleanProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var valueElement) &&
                valueElement.ValueKind == JsonValueKind.True;
        }

        private static ImmutableArray<string> ReadStringArray(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var valuesElement) ||
                valuesElement.ValueKind != JsonValueKind.Array)
            {
                return ImmutableArray<string>.Empty;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var builder = ImmutableArray.CreateBuilder<string>();
            foreach (var valueElement in valuesElement.EnumerateArray())
            {
                if (valueElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = valueElement.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var trimmedValue = value!.Trim();
                    if (seen.Add(trimmedValue))
                    {
                        builder.Add(trimmedValue);
                    }
                }
            }

            return builder.ToImmutable();
        }

        private static IEnumerable<string> GetSymbolKeys(IMethodSymbol methodSymbol)
        {
            return EffectSummarySymbolKeyFactory.GetMethodSymbolKeys(methodSymbol);
        }

        private static void AppendFlattenedTypeArguments(INamedTypeSymbol namedType, ImmutableArray<ITypeSymbol>.Builder builder)
        {
            if (namedType.ContainingType != null)
            {
                AppendFlattenedTypeArguments(namedType.ContainingType, builder);
            }

            foreach (var typeArgument in namedType.TypeArguments)
            {
                builder.Add(typeArgument);
            }
        }

        private static ActualMethodIdentity? TryResolveActualMethodIdentity(IMethodSymbol methodSymbol, Compilation compilation)
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

            var implementationMethodMap = MethodIdentityCache.GetOrAdd(assemblyPath, static path => LoadMethodIdentities(path));
            foreach (var key in GetSymbolKeys(methodSymbol))
            {
                if (implementationMethodMap.TryGetValue(key, out var foundIdentity))
                {
                    identity = foundIdentity;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveMethodIdentityFromPath(
            IEnumerable<string> methodKeys,
            string assemblyPath,
            out ActualMethodIdentity identity)
        {
            identity = null!;
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            {
                return false;
            }

            var implementationMethodMap = MethodIdentityCache.GetOrAdd(assemblyPath, static path => LoadMethodIdentities(path));
            foreach (var key in methodKeys)
            {
                if (implementationMethodMap.TryGetValue(key, out var foundIdentity))
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
                    definition.RelativeVirtualAddress,
                    definition.Attributes);
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
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), CreateGenericContext(reader, definition));
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
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), genericContext: null);
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
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), CreateGenericContext(reader, definition));
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
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), genericContext: null);
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
            public EffectSummaryTypeNameProvider(MetadataReader reader)
            {
            }

            public string GetArrayType(string elementType, ArrayShape shape)
            {
                var rank = Math.Max(shape.Rank, 1);
                return elementType + "[" + new string(',', rank - 1) + "]";
            }

            public string GetByReferenceType(string elementType) => "ref " + elementType;
            public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";
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
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
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
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind)
                => SummaryMetadataNames.NormalizeExactTypeName(SummaryMetadataNames.GetTypeName(metadataReader, handle));
            public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind)
                => SummaryMetadataNames.NormalizeExactTypeName(SummaryMetadataNames.GetTypeReferenceName(metadataReader, handle));
            public string GetTypeFromSpecification(MetadataReader metadataReader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
                => metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
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

        private static ActualAssemblyIdentity? TryResolveActualAssemblyIdentity(IMethodSymbol methodSymbol, Compilation compilation)
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

        private bool TryGetTrustedPurityByMethodKeys(
            IAssemblySymbol? containingAssembly,
            ImmutableArray<string> methodKeys,
            Compilation compilation,
            out PurityEntry classification)
        {
            classification = default;
            if (containingAssembly == null || methodKeys.IsDefaultOrEmpty)
            {
                return false;
            }

            var implementationPath = TryResolveRuntimeImplementationAssemblyPath(containingAssembly, methodKeys, methodKeys[0]);
            if (!string.IsNullOrWhiteSpace(implementationPath))
            {
                var path = implementationPath!;
                if (File.Exists(path) &&
                    TryResolveMethodIdentityFromPath(methodKeys, path, out var implementationIdentity))
                {
                    var assemblyIdentity = AssemblyIdentityCache.GetOrAdd(path, static resolvedPath => ActualAssemblyIdentity.FromFile(resolvedPath));
                    if (TryMatchTrustedEntry(methodKeys, assemblyIdentity, implementationIdentity, out classification))
                    {
                        return true;
                    }
                }
            }

            foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
            {
                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                if (assemblySymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(assemblySymbol, containingAssembly))
                {
                    continue;
                }

                var referencePath = reference.FilePath;
                if (string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath))
                {
                    return false;
                }

                var path = referencePath!;
                var assemblyIdentity = AssemblyIdentityCache.GetOrAdd(path, static resolvedPath => ActualAssemblyIdentity.FromFile(resolvedPath));
                if (TryResolveMethodIdentityFromPath(methodKeys, path, out var referenceIdentity) &&
                    TryMatchTrustedEntry(methodKeys, assemblyIdentity, referenceIdentity, out classification))
                {
                    return true;
                }

                return false;
            }

            return false;
        }

        private bool TryMatchTrustedEntry(
            IEnumerable<string> methodKeys,
            ActualAssemblyIdentity? actualAssemblyIdentity,
            ActualMethodIdentity? actualMethodIdentity,
            out PurityEntry classification)
        {
            classification = default;
            SummaryEntry? bestEntry = null;
            foreach (var key in methodKeys)
            {
                if (!_entriesBySymbol.TryGetValue(key, out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (!entry.IsTrustedFor(actualAssemblyIdentity, actualMethodIdentity))
                    {
                        continue;
                    }

                    if (bestEntry == null || CompareTrustedPurityEntries(entry, bestEntry) > 0)
                    {
                        bestEntry = entry;
                    }
                }
            }

            if (bestEntry == null)
            {
                return false;
            }

            classification = bestEntry.Classification;
            return true;
        }

        private static string? TryResolveRuntimeImplementationAssemblyPath(IMethodSymbol methodSymbol)
        {
            var cacheKey = EffectSummarySymbolKeyFactory.GetMetadataDefinitionExactMethodKey(methodSymbol.OriginalDefinition);
            return RuntimeImplementationAssemblyPathCache.GetOrAdd(cacheKey, _ => ResolveRuntimeImplementationAssemblyPath(GetSymbolKeys(methodSymbol), methodSymbol.ContainingAssembly));
        }

        private static string? TryResolveRuntimeImplementationAssemblyPath(
            IAssemblySymbol? containingAssembly,
            ImmutableArray<string> methodKeys,
            string cacheKey)
        {
            return RuntimeImplementationAssemblyPathCache.GetOrAdd(cacheKey, _ => ResolveRuntimeImplementationAssemblyPath(methodKeys, containingAssembly));
        }

        private static string? ResolveRuntimeImplementationAssemblyPath(
            IEnumerable<string> methodKeys,
            IAssemblySymbol? containingAssembly)
        {
            var coreLibPath = typeof(object).Assembly.Location;
            if (!string.IsNullOrWhiteSpace(coreLibPath) &&
                File.Exists(coreLibPath) &&
                TryResolveMethodIdentityFromPath(methodKeys, coreLibPath, out _))
            {
                return coreLibPath;
            }

            var assemblyName = containingAssembly?.Identity.Name;
            if (!string.IsNullOrWhiteSpace(assemblyName) &&
                RuntimeImplementationAssemblyPathByAssemblyNameCache.TryGetValue(assemblyName!, out var cachedAssemblyPath) &&
                File.Exists(cachedAssemblyPath) &&
                TryResolveMethodIdentityFromPath(methodKeys, cachedAssemblyPath, out _))
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
                        TryResolveMethodIdentityFromPath(methodKeys, location, out _))
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

                if (TryResolveMethodIdentityFromPath(methodKeys, location, out _))
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

                if (TryResolveMethodIdentityFromPath(methodKeys, trustedPlatformAssemblyPath, out _))
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

        private static ImmutableArray<string> GetStaticConstructorKeys(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return ImmutableArray<string>.Empty;
            }

            var keys = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            EffectSummarySymbolKeyFactory.AddSymbolKey(keys, CreateStaticConstructorKey(typeSymbol, includeReturnType: false, useOrdinalGenericParameters: false, useMetadataTypeNames: false));
            EffectSummarySymbolKeyFactory.AddSymbolKey(keys, CreateStaticConstructorKey(typeSymbol, includeReturnType: true, useOrdinalGenericParameters: false, useMetadataTypeNames: false));
            EffectSummarySymbolKeyFactory.AddSymbolKey(keys, CreateStaticConstructorKey(typeSymbol, includeReturnType: false, useOrdinalGenericParameters: true, useMetadataTypeNames: false));
            EffectSummarySymbolKeyFactory.AddSymbolKey(keys, CreateStaticConstructorKey(typeSymbol, includeReturnType: true, useOrdinalGenericParameters: true, useMetadataTypeNames: false));
            EffectSummarySymbolKeyFactory.AddSymbolKey(keys, CreateStaticConstructorKey(typeSymbol, includeReturnType: false, useOrdinalGenericParameters: true, useMetadataTypeNames: true));
            EffectSummarySymbolKeyFactory.AddSymbolKey(keys, CreateStaticConstructorKey(typeSymbol, includeReturnType: true, useOrdinalGenericParameters: true, useMetadataTypeNames: true));
            return keys.ToImmutableArray();
        }

        private static string CreateStaticConstructorKey(
            ITypeSymbol typeSymbol,
            bool includeReturnType,
            bool useOrdinalGenericParameters,
            bool useMetadataTypeNames)
        {
            var containingTypeName = EffectSummarySymbolKeyFactory.FormatSummaryType(typeSymbol, useOrdinalGenericParameters, useMetadataTypeNames);
            var key = containingTypeName + "..cctor()";
            return includeReturnType ? key + "->void" : key;
        }

        private sealed class SummaryEntry
        {
            public SummaryEntry(
                string symbol,
                PurityEntry classification,
                SummaryAssemblyIdentity? assemblyIdentity,
                SummaryMethodIdentity? methodIdentity,
                int sourcePriority)
            {
                Symbol = symbol;
                Classification = classification;
                AssemblyIdentity = assemblyIdentity;
                MethodIdentity = methodIdentity;
                SourcePriority = sourcePriority;
            }

            public string Symbol { get; }
            public PurityEntry Classification { get; }
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

                return IsTrustedFor(actualAssemblyIdentity, actualMethodIdentity);
            }

            public bool IsTrustedFor(
                ActualAssemblyIdentity? actualAssemblyIdentity,
                ActualMethodIdentity? actualMethodIdentity)
            {
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

        internal readonly struct PurityEntry
        {
            public PurityEntry(
                string classification,
                ImmutableArray<string> categories,
                string primaryCategory,
                bool hasFreshArrayAllocationEvidence,
                string freshnessClassification,
                bool hasUnsupportedEffects,
                string effectVisibilityClassification)
            {
                Classification = classification;
                Categories = categories;
                PrimaryCategory = primaryCategory;
                HasFreshArrayAllocationEvidence = hasFreshArrayAllocationEvidence;
                FreshnessClassification = freshnessClassification;
                HasUnsupportedEffects = hasUnsupportedEffects;
                EffectVisibilityClassification = effectVisibilityClassification;
            }

            public string Classification { get; }
            public ImmutableArray<string> Categories { get; }
            public string PrimaryCategory { get; }
            public bool HasFreshArrayAllocationEvidence { get; }
            public string FreshnessClassification { get; }
            public bool HasUnsupportedEffects { get; }
            public string EffectVisibilityClassification { get; }
            public bool IsPure => string.Equals(Classification, "pure", StringComparison.Ordinal);
            public bool IsImpure => string.Equals(Classification, "impure", StringComparison.Ordinal);
            public bool IsConservativeUnknown => string.Equals(Classification, "conservative_unknown", StringComparison.Ordinal);
            public bool IsDefinitive => IsPure || IsImpure;
            public bool IsNonPure => IsImpure;
            public bool IsFreshArrayCandidate =>
                HasFreshArrayAllocationEvidence &&
                (string.Equals(FreshnessClassification, "fresh_array_candidate_via_local_helpers", StringComparison.Ordinal) ||
                 string.Equals(FreshnessClassification, "fresh_owned_array_write", StringComparison.Ordinal));
            public bool AllowsNonEscapingArrayReturn =>
                IsFreshArrayCandidate ||
                (IsPure &&
                 !HasFreshArrayAllocationEvidence &&
                 !HasUnsupportedEffects &&
                 string.Equals(FreshnessClassification, "none", StringComparison.Ordinal) &&
                 string.Equals(EffectVisibilityClassification, "internal_only", StringComparison.Ordinal));
        }

        private sealed class Scope : IDisposable
        {
            private readonly GeneratedPurityCatalog? _previous;

            public Scope(GeneratedPurityCatalog? previous, GeneratedPurityCatalog? current)
            {
                _previous = previous;
                CurrentCatalog.Value = current;
            }

            public void Dispose()
            {
                CurrentCatalog.Value = _previous;
            }
        }
    }
}
