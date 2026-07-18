using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Identity;

namespace SharpProof.Analyzer;

internal sealed class GeneratedPurityCatalog
{
    private static readonly AsyncLocal<GeneratedPurityCatalog?> CurrentCatalog = new();

    private static readonly Lazy<GeneratedPurityCatalog> BuiltInCatalog =
        new(CreateBuiltInCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly EffectSummaryIdentityResolver IdentityResolver =
        new(
            true,
            false,
            RoslynStructuralMethodIdentity.GetCanonicalKey);

    public static readonly GeneratedPurityCatalog Empty = new(
        ImmutableDictionary<string, ImmutableArray<SummaryEntry>>.Empty);

    private readonly ImmutableDictionary<string, ImmutableArray<SummaryEntry>> _entriesBySymbol;

    private GeneratedPurityCatalog(ImmutableDictionary<string, ImmutableArray<SummaryEntry>> entriesBySymbol)
    {
        _entriesBySymbol = entriesBySymbol;
    }

    private bool IsEmpty => _entriesBySymbol.IsEmpty;

    public static GeneratedPurityCatalog Current => CurrentCatalog.Value ?? BuiltInCatalog.Value;

    public static GeneratedPurityCatalog FromOptions(
        AnalyzerOptions options,
        CancellationToken cancellationToken)
    {
        return FromOptionsWithCompatibilityReporter(
            options,
            cancellationToken,
            new EffectSummaryCompatibilityReporter());
    }

    internal static GeneratedPurityCatalog FromOptionsWithCompatibilityReporter(
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

    private static GeneratedPurityCatalog CreateBuiltInCatalog()
    {
        return BuiltInEffectSummaryLoader.LoadBuiltInCatalog(
            EffectSummaryCatalogSourcePriorities.BuiltIn,
            ParseEntries,
            CreateCatalog);
    }

    public static IDisposable UseCurrent(GeneratedPurityCatalog catalog)
    {
        return new Scope(CurrentCatalog.Value, catalog.IsEmpty ? null : catalog);
    }

    private static Dictionary<string, ImmutableArray<SummaryEntry>.Builder> CreateMutableEntries(
        GeneratedPurityCatalog catalog)
    {
        return EffectSummaryCatalogEntryMap.Clone(catalog._entriesBySymbol);
    }

    private static GeneratedPurityCatalog CreateCatalog(
        Dictionary<string, ImmutableArray<SummaryEntry>.Builder> entriesBySymbol)
    {
        if (entriesBySymbol.Count == 0) return Empty;

        return new GeneratedPurityCatalog(EffectSummaryCatalogEntryMap.Freeze(entriesBySymbol));
    }

    public bool TryGetPurity(IMethodSymbol methodSymbol, Compilation compilation, out PurityEntry classification)
    {
        classification = default;
        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true) return false;

        if (TryGetImplicitMetadataValueTypeConstructorPurity(methodSymbol, out classification)) return true;

        var actualAssemblyIdentity = IdentityResolver.TryResolveActualAssemblyIdentity(methodSymbol, compilation);
        var actualMethodIdentity = IdentityResolver.TryResolveActualMethodIdentity(methodSymbol, compilation);
        if (actualAssemblyIdentity == null || actualMethodIdentity == null) return false;

        var bestEntry = SelectBestEntry(
            RoslynStructuralMethodIdentity.GetCompatibleCanonicalKeys(methodSymbol),
            entry => !IsBuiltInAbstractInterfaceEntry(methodSymbol, entry) &&
                     entry.IsTrustedFor(methodSymbol, actualAssemblyIdentity, actualMethodIdentity));

        if (bestEntry == null) return TryGetBuiltInFrameworkEntryByKeyOnly(methodSymbol, out classification);

        classification = bestEntry.Classification;
        return true;
    }

    internal ImmutableArray<TrustedPurityEntry> GetTrustedPurityEntries(
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            return ImmutableArray<TrustedPurityEntry>.Empty;

        if (TryGetImplicitMetadataValueTypeConstructorPurity(methodSymbol, out var implicitClassification))
            return ImmutableArray.Create(new TrustedPurityEntry(
                "built_in_purity_catalog",
                "implicit_metadata_value_type_constructor",
                implicitClassification,
                true));

        var actualAssemblyIdentity = IdentityResolver.TryResolveActualAssemblyIdentity(methodSymbol, compilation);
        var actualMethodIdentity = IdentityResolver.TryResolveActualMethodIdentity(methodSymbol, compilation);
        var trustedEntries = new List<SummaryEntry>();
        var methodKeys = RoslynStructuralMethodIdentity.GetCompatibleCanonicalKeys(methodSymbol).ToArray();
        var bestEntry = actualAssemblyIdentity != null && actualMethodIdentity != null
            ? SelectBestEntry(
                methodKeys,
                entry => !IsBuiltInAbstractInterfaceEntry(methodSymbol, entry) &&
                         entry.IsTrustedFor(methodSymbol, actualAssemblyIdentity, actualMethodIdentity),
                trustedEntries)
            : null;

        if (bestEntry == null &&
            IsFrameworkAssemblyName(methodSymbol.ContainingAssembly?.Identity.Name))
            bestEntry = SelectBestEntry(
                methodKeys,
                entry => !IsBuiltInAbstractInterfaceEntry(methodSymbol, entry) &&
                         entry.SourcePriority == EffectSummaryCatalogSourcePriorities.BuiltIn &&
                         entry.AssemblyIdentity?.IsComplete == true &&
                         entry.MethodIdentity != null,
                trustedEntries);

        if (bestEntry == null) return ImmutableArray<TrustedPurityEntry>.Empty;

        var uniqueEntries = new Dictionary<string, TrustedPurityEntry>(StringComparer.Ordinal);
        foreach (var entry in trustedEntries)
        {
            var source = entry.SourcePriority == EffectSummaryCatalogSourcePriorities.Additional
                ? "additional_generated_summary"
                : "built_in_generated_summary";
            var value = entry.SourcePriority == EffectSummaryCatalogSourcePriorities.Additional
                ? entry.SourcePath ?? entry.DisplayName
                : entry.DisplayName;
            var key = source + "\u001f" + value + "\u001f" + entry.Classification.Classification;
            var isSelected = ReferenceEquals(entry, bestEntry);
            if (uniqueEntries.TryGetValue(key, out var existing))
            {
                if (isSelected && !existing.IsSelected)
                    uniqueEntries[key] = new TrustedPurityEntry(
                        source,
                        value,
                        entry.Classification,
                        true);
            }
            else
            {
                uniqueEntries.Add(key, new TrustedPurityEntry(
                    source,
                    value,
                    entry.Classification,
                    isSelected));
            }
        }

        return uniqueEntries.Values
            .OrderBy(static entry => entry.Source, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Value, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Classification.Classification, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private bool TryGetBuiltInFrameworkEntryByKeyOnly(IMethodSymbol methodSymbol, out PurityEntry classification)
    {
        classification = default;
        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true ||
            !IsFrameworkAssemblyName(methodSymbol.ContainingAssembly?.Identity.Name))
            return false;

        var bestEntry = SelectBestEntry(
            RoslynStructuralMethodIdentity.GetCompatibleCanonicalKeys(methodSymbol),
            entry => !IsBuiltInAbstractInterfaceEntry(methodSymbol, entry) &&
                     entry.SourcePriority == EffectSummaryCatalogSourcePriorities.BuiltIn &&
                     entry.AssemblyIdentity?.IsComplete == true &&
                     entry.MethodIdentity != null);

        if (bestEntry == null) return false;

        classification = bestEntry.Classification;
        return true;
    }

    private static bool IsBuiltInAbstractInterfaceEntry(IMethodSymbol methodSymbol, SummaryEntry entry)
    {
        return entry.SourcePriority == EffectSummaryCatalogSourcePriorities.BuiltIn &&
               methodSymbol.ContainingType?.TypeKind == TypeKind.Interface &&
               (string.Equals(entry.Classification.PrimaryCategory, "abstract", StringComparison.Ordinal) ||
                entry.Classification.Categories.Contains("abstract", StringComparer.Ordinal));
    }

    internal static bool IsFrameworkAssemblyName(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName)) return false;

        var name = assemblyName!;
        return name == "mscorlib" ||
               name == "netstandard" ||
               name == "System" ||
               name == "System.Private.CoreLib" ||
               name.StartsWith("System.", StringComparison.Ordinal) ||
               name.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    private static bool TryGetImplicitMetadataValueTypeConstructorPurity(IMethodSymbol methodSymbol,
        out PurityEntry classification)
    {
        classification = default;
        if (methodSymbol.MethodKind != MethodKind.Constructor ||
            !methodSymbol.IsImplicitlyDeclared ||
            methodSymbol.Parameters.Length != 0 ||
            methodSymbol.IsStatic ||
            methodSymbol.ContainingType?.IsValueType != true)
            return false;

        classification = new PurityEntry(
            "pure",
            ImmutableArray<string>.Empty,
            "implicit_metadata_value_type_constructor",
            false,
            "none",
            false,
            "internal_only");
        return true;
    }

    public bool TryGetFieldPurity(IFieldSymbol fieldSymbol, Compilation compilation, out PurityEntry classification)
    {
        classification = default;
        if (fieldSymbol.Locations.FirstOrDefault()?.IsInMetadata != true || !fieldSymbol.IsStatic) return false;

        var staticConstructor = fieldSymbol.ContainingType?
            .GetMembers(".cctor")
            .OfType<IMethodSymbol>()
            .FirstOrDefault();
        if (staticConstructor == null)
        {
            var staticConstructorKeys = GetStaticConstructorKeys(fieldSymbol.ContainingType);
            return TryGetTrustedPurityByMethodKeys(fieldSymbol.ContainingAssembly, staticConstructorKeys, compilation,
                out classification);
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
            return false;

        var runtimeMethodKeys = ImmutableArray.Create(
            RoslynStructuralMethodIdentity.Create(methodSymbol.OriginalDefinition)
                .WithContainingMetadataType("System.RuntimeType")
                .ToCanonicalKey());

        return TryGetTrustedPurityByMethodKeys(methodSymbol.ContainingAssembly, runtimeMethodKeys, compilation,
            out classification);
    }

    private static int CompareTrustedPurityEntries(SummaryEntry left, SummaryEntry right)
    {
        var sourcePriorityComparison = left.SourcePriority.CompareTo(right.SourcePriority);
        if (sourcePriorityComparison != 0) return sourcePriorityComparison;

        var leftPriority = GetClassificationPriority(left.Classification);
        var rightPriority = GetClassificationPriority(right.Classification);
        var priorityComparison = leftPriority.CompareTo(rightPriority);
        if (priorityComparison != 0) return priorityComparison;

        var leftPrimaryCategory = left.Classification.PrimaryCategory ?? string.Empty;
        var rightPrimaryCategory = right.Classification.PrimaryCategory ?? string.Empty;
        var primaryCategoryComparison = string.CompareOrdinal(leftPrimaryCategory, rightPrimaryCategory);
        if (primaryCategoryComparison != 0) return primaryCategoryComparison;

        return string.CompareOrdinal(left.DisplayName, right.DisplayName);
    }

    private static int GetClassificationPriority(PurityEntry classification)
    {
        return classification.Classification switch
        {
            "impure" => 3,
            "pure" => 2,
            "conservative_unknown" => 1,
            _ => 0
        };
    }

    private SummaryEntry? SelectBestEntry(
        IEnumerable<string> keys,
        Func<SummaryEntry, bool> isEligible,
        ICollection<SummaryEntry>? eligibleEntries = null)
    {
        SummaryEntry? bestEntry = null;
        foreach (var entry in EffectSummaryCatalogEntryMap.Enumerate(_entriesBySymbol, keys))
        {
            if (!isEligible(entry)) continue;

            eligibleEntries?.Add(entry);
            if (bestEntry == null || CompareTrustedPurityEntries(entry, bestEntry) > 0)
                bestEntry = entry;
        }

        return bestEntry;
    }

    internal static bool TryCanMetadataMethodBeOverridden(IMethodSymbol methodSymbol, Compilation compilation,
        out bool canBeOverridden)
    {
        canBeOverridden = false;
        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true) return false;

        var actualMethodIdentity = IdentityResolver.TryResolveActualMethodIdentity(methodSymbol, compilation);
        if (actualMethodIdentity == null) return false;

        canBeOverridden = actualMethodIdentity.CanBeOverridden;
        return true;
    }

    private static IEnumerable<SummaryEntry> ParseEntries(
        EffectSummaryJsonDocument document,
        int sourcePriority,
        string? sourcePath,
        EffectSummaryCompatibilityReporter? compatibilityReporter)
    {
        if (document.TryGetGeneratedPurityEntries(out var entriesElement))
        {
            foreach (var entryElement in entriesElement.EnumerateArray())
            {
                if (entryElement.ValueKind != JsonValueKind.Object ||
                    !StructuralMethodIdentityJson.TryReadMethod(entryElement, out _, out var canonicalKey) ||
                    !TryCreatePurityEntry(entryElement, out var purityEntry))
                    continue;
                var displayName = AnalyzerJsonElementReader.GetTrimmedStringProperty(entryElement, "DisplayName") ??
                                  canonicalKey;

                yield return new SummaryEntry(
                    canonicalKey,
                    displayName,
                    purityEntry,
                    SummaryAssemblyIdentity.FromJson(entryElement),
                    SummaryMethodIdentity.FromJson(entryElement),
                    EffectSummaryArtifactSource.FromJson(entryElement),
                    sourcePriority,
                    sourcePath,
                    compatibilityReporter);
            }

            yield break;
        }

        foreach (var assembly in document.EnumerateLegacyAssemblies())
        {
            var assemblyIdentity = SummaryAssemblyIdentity.FromJson(assembly.Element);
            var artifactSource = EffectSummaryArtifactSource.FromJson(assembly.Element);
            foreach (var methodElement in assembly.EnumerateMethods())
            {
                if (!StructuralMethodIdentityJson.TryReadMethod(methodElement, out _, out var canonicalKey) ||
                    !methodElement.TryGetProperty("PurityClassification", out var purityElement) ||
                    purityElement.ValueKind != JsonValueKind.Object ||
                    !TryCreatePurityEntry(purityElement, out var purityEntry))
                    continue;
                var displayName = AnalyzerJsonElementReader.GetTrimmedStringProperty(methodElement, "DisplayName") ??
                                  canonicalKey;

                yield return new SummaryEntry(
                    canonicalKey,
                    displayName,
                    purityEntry,
                    assemblyIdentity,
                    SummaryMethodIdentity.FromJson(methodElement),
                    artifactSource,
                    sourcePriority,
                    sourcePath,
                    compatibilityReporter);
            }
        }
    }

    private static bool TryCreatePurityEntry(JsonElement element, out PurityEntry purityEntry)
    {
        purityEntry = default;

        var classification = AnalyzerJsonElementReader.GetTrimmedStringProperty(element, "Classification");
        if (string.IsNullOrWhiteSpace(classification)) return false;

        var categories = ReadStringArray(element, "Categories");
        var primaryCategory = AnalyzerJsonElementReader.GetTrimmedStringProperty(element, "PrimaryCategory");
        var freshnessClassification =
            AnalyzerJsonElementReader.GetTrimmedStringProperty(element, "FreshnessClassification") ?? "none";
        var effectVisibilityClassification =
            AnalyzerJsonElementReader.GetTrimmedStringProperty(element, "EffectVisibilityClassification") ?? "unknown";
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

    private static string PrimaryCategoryFallback(ImmutableArray<string> categories)
    {
        return categories.Length > 0
            ? categories[0]
            : "generated_purity_summary";
    }

    private static bool ReadBooleanProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var valueElement) &&
               valueElement.ValueKind == JsonValueKind.True;
    }

    private static ImmutableArray<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valuesElement) ||
            valuesElement.ValueKind != JsonValueKind.Array)
            return ImmutableArray<string>.Empty;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var valueElement in valuesElement.EnumerateArray())
        {
            if (valueElement.ValueKind != JsonValueKind.String) continue;

            var value = valueElement.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                var trimmedValue = value!.Trim();
                if (seen.Add(trimmedValue)) builder.Add(trimmedValue);
            }
        }

        return builder.ToImmutable();
    }

    private bool TryGetTrustedPurityByMethodKeys(
        IAssemblySymbol? containingAssembly,
        ImmutableArray<string> methodKeys,
        Compilation compilation,
        out PurityEntry classification)
    {
        classification = default;
        if (containingAssembly == null || methodKeys.IsDefaultOrEmpty) return false;

        var implementationPath =
            IdentityResolver.TryResolveRuntimeImplementationAssemblyPath(containingAssembly, methodKeys, methodKeys[0]);
        if (!string.IsNullOrWhiteSpace(implementationPath))
        {
            var path = implementationPath!;
            if (IdentityResolver.TryResolveMethodIdentityFromPath(methodKeys, path, out var implementationIdentity))
            {
                var assemblyIdentity = IdentityResolver.GetAssemblyIdentity(path);
                if (TryMatchTrustedEntry(methodKeys, assemblyIdentity, implementationIdentity, out classification))
                    return true;
            }
        }

        var referencePath = SummaryAssemblyReferenceResolver.FindAssemblyReferencePath(containingAssembly, compilation);
        if (referencePath == null) return false;

        var referenceAssemblyIdentity = IdentityResolver.GetAssemblyIdentity(referencePath);
        return IdentityResolver.TryResolveMethodIdentityFromPath(methodKeys, referencePath,
                   out var referenceIdentity) &&
               TryMatchTrustedEntry(methodKeys, referenceAssemblyIdentity, referenceIdentity, out classification);
    }

    private bool TryMatchTrustedEntry(
        IEnumerable<string> methodKeys,
        ActualAssemblyIdentity? actualAssemblyIdentity,
        ActualMethodIdentity? actualMethodIdentity,
        out PurityEntry classification)
    {
        classification = default;
        var bestEntry = SelectBestEntry(
            methodKeys,
            entry => entry.IsTrustedFor(actualAssemblyIdentity, actualMethodIdentity));

        if (bestEntry == null) return false;

        classification = bestEntry.Classification;
        return true;
    }

    private static ImmutableArray<string> GetStaticConstructorKeys(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType) return ImmutableArray<string>.Empty;

        var identity = new StructuralMethodIdentity(
            RoslynStructuralMethodIdentity.GetMetadataTypeName(namedType),
            "static-constructor",
            ".cctor",
            0,
            Array.Empty<StructuralParameterIdentity>(),
            "named:System.Void",
            "none");
        return ImmutableArray.Create(identity.ToCanonicalKey());
    }

    private sealed class SummaryEntry : EffectSummaryCatalogEntry
    {
        public SummaryEntry(
            string symbol,
            string displayName,
            PurityEntry classification,
            SummaryAssemblyIdentity? assemblyIdentity,
            SummaryMethodIdentity? methodIdentity,
            EffectSummaryArtifactSource? artifactSource,
            int sourcePriority,
            string? sourcePath,
            EffectSummaryCompatibilityReporter? compatibilityReporter)
            : base(
                symbol,
                displayName,
                assemblyIdentity,
                methodIdentity,
                artifactSource,
                sourcePriority,
                sourcePath,
                compatibilityReporter)
        {
            DisplayName = displayName;
            Classification = classification;
        }

        public string DisplayName { get; }
        public PurityEntry Classification { get; }
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

        public bool IsConservativeUnknown =>
            string.Equals(Classification, "conservative_unknown", StringComparison.Ordinal);

        public bool IsDefinitive => IsPure || IsImpure;
        public bool IsNonPure => IsImpure;

        public bool IsFreshArrayCandidate =>
            HasFreshArrayAllocationEvidence &&
            (string.Equals(FreshnessClassification, "fresh_array_candidate_via_local_helpers",
                 StringComparison.Ordinal) ||
             string.Equals(FreshnessClassification, "fresh_owned_array_write", StringComparison.Ordinal));

        public bool AllowsNonEscapingArrayReturn =>
            IsFreshArrayCandidate ||
            (IsPure &&
             !HasFreshArrayAllocationEvidence &&
             !HasUnsupportedEffects &&
             string.Equals(FreshnessClassification, "none", StringComparison.Ordinal) &&
             string.Equals(EffectVisibilityClassification, "internal_only", StringComparison.Ordinal));
    }

    internal readonly struct TrustedPurityEntry
    {
        internal TrustedPurityEntry(
            string source,
            string value,
            PurityEntry classification,
            bool isSelected)
        {
            Source = source;
            Value = value;
            Classification = classification;
            IsSelected = isSelected;
        }

        internal string Source { get; }
        internal string Value { get; }
        internal PurityEntry Classification { get; }
        internal bool IsSelected { get; }
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
