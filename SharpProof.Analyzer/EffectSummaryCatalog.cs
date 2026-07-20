namespace SharpProof.Analyzer;

internal sealed class EffectSummaryCatalog
{
    private const int BuiltInSourcePriority = 0;
    private const int AdditionalSourcePriority = 1;

    private static readonly AsyncLocal<EffectSummaryCatalog?> CurrentCatalog = new();

    private static readonly Lazy<EffectSummaryCatalog> BuiltInCatalog =
        new(CreateBuiltInCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly EffectSummaryIdentityResolver PurityIdentityResolver =
        new(
            true,
            false,
            RoslynStructuralMethodIdentity.GetCanonicalKey);

    private static readonly EffectSummaryIdentityResolver ExceptionIdentityResolver =
        new(
            false,
            true,
            RoslynStructuralMethodIdentity.GetCanonicalKey);

    public static readonly EffectSummaryCatalog Empty = new(
        ImmutableDictionary<string, ImmutableArray<SummaryEntry>>.Empty);

    private readonly ImmutableDictionary<string, ImmutableArray<SummaryEntry>> _entriesBySymbol;

    private EffectSummaryCatalog(ImmutableDictionary<string, ImmutableArray<SummaryEntry>> entriesBySymbol)
    {
        _entriesBySymbol = entriesBySymbol;
    }

    private bool IsEmpty => _entriesBySymbol.IsEmpty;

    public static EffectSummaryCatalog Current => CurrentCatalog.Value ?? BuiltInCatalog.Value;

    public static EffectSummaryCatalog FromOptions(
        AnalyzerOptions options,
        CancellationToken cancellationToken)
    {
        return FromOptionsWithCompatibilityReporter(
            options,
            cancellationToken,
            new EffectSummaryCompatibilityReporter());
    }

    internal static EffectSummaryCatalog FromOptionsWithCompatibilityReporter(
        AnalyzerOptions options,
        CancellationToken cancellationToken,
        EffectSummaryCompatibilityReporter compatibilityReporter)
    {
        var builtInCatalog = BuiltInCatalog.Value;
        if (!BuiltInEffectSummaryLoader.HasAdditionalSummaryJsonDocuments(options)) return builtInCatalog;

        var entries = CloneEntries(builtInCatalog._entriesBySymbol);
        BuiltInEffectSummaryLoader.LoadAdditionalSummaryJsonDocuments(
            options,
            cancellationToken,
            (path, json) => AddJson(entries, json, AdditionalSourcePriority, path, compatibilityReporter));
        return CreateCatalog(entries);
    }

    private static EffectSummaryCatalog CreateBuiltInCatalog()
    {
        var entries = new Dictionary<string, ImmutableArray<SummaryEntry>.Builder>(StringComparer.Ordinal);
        BuiltInEffectSummaryLoader.LoadBuiltInSummaryJsonDocuments(
            json => AddJson(entries, json, BuiltInSourcePriority, null, null));
        return CreateCatalog(entries);
    }

    public static IDisposable UseCurrent(EffectSummaryCatalog catalog)
    {
        return new Scope(CurrentCatalog.Value, catalog.IsEmpty ? null : catalog);
    }

    private static Dictionary<string, ImmutableArray<SummaryEntry>.Builder> CloneEntries(
        ImmutableDictionary<string, ImmutableArray<SummaryEntry>> source)
    {
        var clone = new Dictionary<string, ImmutableArray<SummaryEntry>.Builder>(StringComparer.Ordinal);
        foreach (var item in source)
            clone.Add(item.Key, item.Value.ToBuilder());
        return clone;
    }

    private static EffectSummaryCatalog CreateCatalog(
        Dictionary<string, ImmutableArray<SummaryEntry>.Builder> entriesBySymbol)
    {
        if (entriesBySymbol.Count == 0) return Empty;

        return new EffectSummaryCatalog(entriesBySymbol.ToImmutableDictionary(
            static item => item.Key,
            static item => item.Value.ToImmutable(),
            StringComparer.Ordinal));
    }

    public bool TryGetPurity(IMethodSymbol methodSymbol, Compilation compilation, out PurityEntry classification)
    {
        classification = default;
        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true) return false;

        if (TryGetImplicitMetadataValueTypeConstructorPurity(methodSymbol, out classification)) return true;

        var actualAssemblyIdentity = PurityIdentityResolver.TryResolveActualAssemblyIdentity(methodSymbol, compilation);
        var actualMethodIdentity = PurityIdentityResolver.TryResolveActualMethodIdentity(methodSymbol, compilation);
        if (actualAssemblyIdentity == null || actualMethodIdentity == null) return false;

        var bestEntry = SelectBestEntry(
            RoslynStructuralMethodIdentity.GetCanonicalKeys(methodSymbol),
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

        var actualAssemblyIdentity = PurityIdentityResolver.TryResolveActualAssemblyIdentity(methodSymbol, compilation);
        var actualMethodIdentity = PurityIdentityResolver.TryResolveActualMethodIdentity(methodSymbol, compilation);
        var trustedEntries = new List<SummaryEntry>();
        var methodKeys = RoslynStructuralMethodIdentity.GetCanonicalKeys(methodSymbol).ToArray();
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
                         entry.SourcePriority == BuiltInSourcePriority &&
                         entry.AssemblyIdentity?.IsComplete == true &&
                         entry.MethodIdentity != null,
                trustedEntries);

        if (bestEntry == null) return ImmutableArray<TrustedPurityEntry>.Empty;

        var uniqueEntries = new Dictionary<string, TrustedPurityEntry>(StringComparer.Ordinal);
        foreach (var entry in trustedEntries)
        {
            var source = entry.SourcePriority == AdditionalSourcePriority
                ? "additional_generated_summary"
                : "built_in_generated_summary";
            var value = entry.SourcePriority == AdditionalSourcePriority
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
            RoslynStructuralMethodIdentity.GetCanonicalKeys(methodSymbol),
            entry => !IsBuiltInAbstractInterfaceEntry(methodSymbol, entry) &&
                     entry.SourcePriority == BuiltInSourcePriority &&
                     entry.AssemblyIdentity?.IsComplete == true &&
                     entry.MethodIdentity != null);

        if (bestEntry == null) return false;

        classification = bestEntry.Classification;
        return true;
    }

    private static bool IsBuiltInAbstractInterfaceEntry(IMethodSymbol methodSymbol, SummaryEntry entry)
    {
        return entry.SourcePriority == BuiltInSourcePriority &&
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

        var matchedSources = new Dictionary<string, ImmutableSortedSet<string>.Builder>(StringComparer.Ordinal);
        var matchedEdges =
            new Dictionary<string, Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>>(
                StringComparer.Ordinal);
        var actualAssemblyIdentity = compilation is null
            ? null
            : ExceptionIdentityResolver.TryResolveActualAssemblyIdentity(methodSymbol, compilation);
        var actualMethodIdentity = compilation is null
            ? null
            : ExceptionIdentityResolver.TryResolveActualMethodIdentity(methodSymbol, compilation);

        foreach (var entry in EnumerateEntries(
                     RoslynStructuralMethodIdentity.GetCanonicalKeys(methodSymbol)))
        {
            if (entry.ExceptionInfos.IsDefaultOrEmpty ||
                !entry.IsTrustedFor(methodSymbol, actualAssemblyIdentity, actualMethodIdentity))
                continue;

            foreach (var exceptionInfo in entry.ExceptionInfos)
            {
                if (!matchedSources.TryGetValue(exceptionInfo.ExceptionType, out var sources))
                {
                    sources = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                    matchedSources.Add(exceptionInfo.ExceptionType, sources);
                }

                sources.UnionWith(exceptionInfo.Sources);
                if (exceptionInfo.Edges.IsDefaultOrEmpty) continue;

                if (!matchedEdges.TryGetValue(exceptionInfo.ExceptionType, out var edgeMap))
                {
                    edgeMap = new Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>(
                        SummaryExceptionEdgeInfoComparer.Instance);
                    matchedEdges.Add(exceptionInfo.ExceptionType, edgeMap);
                }

                foreach (var edge in exceptionInfo.Edges) edgeMap[edge] = edge;
            }
        }

        if (matchedSources.Count == 0)
        {
            exceptionInfos = ImmutableArray<SummaryExceptionInfo>.Empty;
            return false;
        }

        exceptionInfos = matchedSources
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(item => new SummaryExceptionInfo(
                item.Key,
                item.Value.ToImmutableArray(),
                matchedEdges.TryGetValue(item.Key, out var edgeMap)
                    ? OrderExceptionEdges(edgeMap.Values)
                    : ImmutableArray<SummaryExceptionEdgeInfo>.Empty))
            .ToImmutableArray();
        return true;
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
        foreach (var entry in EnumerateEntries(keys))
        {
            if (!entry.HasPurity || !isEligible(entry)) continue;

            eligibleEntries?.Add(entry);
            if (bestEntry == null || CompareTrustedPurityEntries(entry, bestEntry) > 0)
                bestEntry = entry;
        }

        return bestEntry;
    }

    private IEnumerable<SummaryEntry> EnumerateEntries(IEnumerable<string> keys)
    {
        foreach (var key in keys)
            if (_entriesBySymbol.TryGetValue(key, out var entries))
                foreach (var entry in entries)
                    yield return entry;
    }

    internal static bool TryCanMetadataMethodBeOverridden(IMethodSymbol methodSymbol, Compilation compilation,
        out bool canBeOverridden)
    {
        canBeOverridden = false;
        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true) return false;

        var actualMethodIdentity = PurityIdentityResolver.TryResolveActualMethodIdentity(methodSymbol, compilation);
        if (actualMethodIdentity == null) return false;

        canBeOverridden = actualMethodIdentity.CanBeOverridden;
        return true;
    }

    private static void AddJson(
        Dictionary<string, ImmutableArray<SummaryEntry>.Builder> entriesBySymbol,
        string json,
        int sourcePriority,
        string? sourcePath,
        EffectSummaryCompatibilityReporter? compatibilityReporter)
    {
        if (!EffectSummaryJsonParser.TryParse(json, out var document, out _)) return;

        using (document)
        {
            var root = document.RootElement;
            foreach (var entry in ParseEntries(root, sourcePriority, sourcePath, compatibilityReporter))
            {
                if (!entriesBySymbol.TryGetValue(entry.Symbol, out var entries))
                {
                    entries = ImmutableArray.CreateBuilder<SummaryEntry>();
                    entriesBySymbol.Add(entry.Symbol, entries);
                }

                entries.Add(entry);
            }
        }
    }

    private static IEnumerable<SummaryEntry> ParseEntries(
        JsonElement root,
        int sourcePriority,
        string? sourcePath,
        EffectSummaryCompatibilityReporter? compatibilityReporter)
    {
        JsonElement entriesElement = default;
        var hasGeneratedPurity =
            root.TryGetProperty("GeneratedPurityCatalog", out var generatedCatalog) &&
            generatedCatalog.ValueKind == JsonValueKind.Object &&
            generatedCatalog.TryGetProperty("SchemaVersion", out var generatedSchemaVersionElement) &&
            generatedSchemaVersionElement.ValueKind == JsonValueKind.Number &&
            generatedSchemaVersionElement.TryGetInt32(out var generatedSchemaVersion) &&
            generatedSchemaVersion == EffectSummarySchemaContract.CurrentVersion &&
            generatedCatalog.TryGetProperty("Entries", out entriesElement) &&
            entriesElement.ValueKind == JsonValueKind.Array;
        if (hasGeneratedPurity)
        {
            foreach (var entryElement in entriesElement.EnumerateArray())
            {
                if (!EffectSummaryContractReader.TryReadMethod(entryElement, out var entry) ||
                    !TryCreatePurityEntry(entry.FlatPurity, out var purityEntry))
                    continue;
                var canonicalKey = entry.CanonicalKey!.Trim();
                var displayName = Normalize(entry.DisplayName) ?? canonicalKey;

                yield return new SummaryEntry(
                    canonicalKey,
                    displayName,
                    purityEntry,
                    ImmutableArray<SummaryExceptionInfo>.Empty,
                    SummaryAssemblyIdentity.FromContract(entry.AssemblyName, entry.AssemblySha256,
                        entry.ModuleVersionId),
                    SummaryMethodIdentity.FromContract(entry.MetadataToken, entry.MethodBodySha256),
                    EffectSummaryArtifactSource.FromContract(entry.ArtifactSource),
                    sourcePriority,
                    sourcePath,
                    compatibilityReporter);
            }
        }

        if (!root.TryGetProperty("Assemblies", out var assemblies) ||
            assemblies.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var assemblyElement in assemblies.EnumerateArray())
        {
            if (!EffectSummaryContractReader.TryReadAssembly(assemblyElement, out var assembly))
                continue;

            var assemblyIdentity = SummaryAssemblyIdentity.FromContract(
                assembly.AssemblyName,
                assembly.AssemblySha256,
                assembly.ModuleVersionId);
            var artifactSource = EffectSummaryArtifactSource.FromContract(assembly.ArtifactSource);
            foreach (var methodElement in Values(assembly.Methods))
            {
                if (!EffectSummaryContractReader.TryReadMethod(methodElement, out var method))
                    continue;
                var canonicalKey = method.CanonicalKey!.Trim();

                PurityEntry? purityEntry = null;
                if (!hasGeneratedPurity && method.PurityClassification != null &&
                    TryCreatePurityEntry(method.PurityClassification, out var parsedPurity))
                    purityEntry = parsedPurity;

                var exceptionInfos = ReadExceptionInfos(method);
                if (!purityEntry.HasValue && exceptionInfos.IsDefaultOrEmpty) continue;

                var displayName = Normalize(method.DisplayName) ?? canonicalKey;

                yield return new SummaryEntry(
                    canonicalKey,
                    displayName,
                    purityEntry,
                    exceptionInfos,
                    assemblyIdentity,
                    SummaryMethodIdentity.FromContract(method.MetadataToken, method.MethodBodySha256),
                    artifactSource,
                    sourcePriority,
                    sourcePath,
                    compatibilityReporter);
            }
        }
    }

    private static ImmutableArray<SummaryExceptionInfo> ReadExceptionInfos(EffectSummaryMethodContract method)
    {
        var exceptionTypes = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        var exceptionSources = new Dictionary<string, ImmutableSortedSet<string>.Builder>(StringComparer.Ordinal);
        var exceptionEdges =
            new Dictionary<string, Dictionary<SummaryExceptionEdgeInfo, SummaryExceptionEdgeInfo>>(
                StringComparer.Ordinal);
        exceptionTypes.UnionWith(Normalize(method.ThrownExceptionTypes));
        exceptionTypes.UnionWith(Normalize(method.TransitiveThrownExceptionTypes));
        foreach (var provenance in Values(method.ThrownExceptionProvenance)
                     .Concat(Values(method.TransitiveThrownExceptionProvenance)))
            AddExceptionSource(exceptionTypes, exceptionSources, provenance.ExceptionType, provenance.SourcePath);
        foreach (var edge in Values(method.TransitiveThrownExceptionEdges))
        {
            var exceptionType = Normalize(edge.ExceptionType);
            if (exceptionType == null) continue;
            AddExceptionSource(exceptionTypes, exceptionSources, exceptionType, edge.SourcePath);
            if (!exceptionEdges.TryGetValue(exceptionType, out var edgeMap))
                exceptionEdges.Add(exceptionType, edgeMap = new(
                    SummaryExceptionEdgeInfoComparer.Instance));
            var value = new SummaryExceptionEdgeInfo(
                Normalize(edge.SourcePath),
                edge.CallChain.IsDefault ? ImmutableArray<StructuralMethodIdentity>.Empty : edge.CallChain,
                edge.CalleeIdentity,
                edge.Depth);
            edgeMap[value] = value;
        }
        return exceptionTypes
            .Select(exceptionType => new SummaryExceptionInfo(
                exceptionType,
                exceptionSources.TryGetValue(exceptionType, out var sources)
                    ? sources.ToImmutableArray()
                    : ImmutableArray<string>.Empty,
                exceptionEdges.TryGetValue(exceptionType, out var edges)
                    ? OrderExceptionEdges(edges.Values)
                    : ImmutableArray<SummaryExceptionEdgeInfo>.Empty))
            .ToImmutableArray();
    }

    private static bool TryCreatePurityEntry(EffectSummaryPurityContract contract, out PurityEntry purityEntry)
    {
        purityEntry = default;
        var classification = Normalize(contract.Classification);
        if (classification == null) return false;
        var categories = Normalize(contract.Categories);
        var primaryCategory = Normalize(contract.PrimaryCategory);
        purityEntry = new PurityEntry(
            classification,
            categories,
            primaryCategory ?? PrimaryCategoryFallback(categories),
            contract.HasFreshArrayAllocationEvidence,
            Normalize(contract.FreshnessClassification) ?? "none",
            contract.HasUnsupportedEffects,
            Normalize(contract.EffectVisibilityClassification) ?? "unknown");
        return true;
    }

    private static string PrimaryCategoryFallback(ImmutableArray<string> categories)
    {
        return categories.Length > 0
            ? categories[0]
            : "generated_purity_summary";
    }

    private static ImmutableArray<string> Normalize(ImmutableArray<string> values) => values.IsDefault
        ? ImmutableArray<string>.Empty
        : values.Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim()).Distinct(StringComparer.Ordinal).ToImmutableArray();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static IEnumerable<T> Values<T>(ImmutableArray<T> values) =>
        values.IsDefault ? Enumerable.Empty<T>() : values;

    private static ImmutableArray<SummaryExceptionEdgeInfo> OrderExceptionEdges(
        IEnumerable<SummaryExceptionEdgeInfo> edges) => edges
        .OrderBy(static edge => edge.Depth)
        .ThenBy(static edge => edge.CalleeIdentity?.ToCanonicalKey(), StringComparer.Ordinal)
        .ThenBy(static edge => string.Join(">", edge.CallChain.Select(static item => item.ToCanonicalKey())),
            StringComparer.Ordinal)
        .ThenBy(static edge => edge.SourcePath, StringComparer.Ordinal)
        .ToImmutableArray();

    private static void AddExceptionSource(
        ImmutableSortedSet<string>.Builder exceptionTypes,
        Dictionary<string, ImmutableSortedSet<string>.Builder> exceptionSources,
        string? exceptionType,
        string? sourcePath)
    {
        exceptionType = Normalize(exceptionType);
        if (exceptionType == null) return;
        exceptionTypes.Add(exceptionType);
        sourcePath = Normalize(sourcePath);
        if (sourcePath == null) return;

        if (!exceptionSources.TryGetValue(exceptionType, out var sources))
        {
            sources = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
            exceptionSources.Add(exceptionType, sources);
        }

        sources.Add(sourcePath);
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
            PurityIdentityResolver.TryResolveRuntimeImplementationAssemblyPath(
                containingAssembly, methodKeys, methodKeys[0]);
        if (!string.IsNullOrWhiteSpace(implementationPath))
        {
            var path = implementationPath!;
            if (PurityIdentityResolver.TryResolveMethodIdentityFromPath(
                    methodKeys, path, out var implementationIdentity))
            {
                var assemblyIdentity = PurityIdentityResolver.GetAssemblyIdentity(path);
                if (TryMatchTrustedEntry(methodKeys, assemblyIdentity, implementationIdentity, out classification))
                    return true;
            }
        }

        var referencePath = SummaryAssemblyReferenceResolver.FindAssemblyReferencePath(containingAssembly, compilation);
        if (referencePath == null) return false;

        var referenceAssemblyIdentity = PurityIdentityResolver.GetAssemblyIdentity(referencePath);
        return PurityIdentityResolver.TryResolveMethodIdentityFromPath(methodKeys, referencePath,
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

    private sealed class SummaryEntry(
        string symbol,
        string displayName,
        PurityEntry? classification,
        ImmutableArray<SummaryExceptionInfo> exceptionInfos,
        SummaryAssemblyIdentity? assemblyIdentity,
        SummaryMethodIdentity? methodIdentity,
        EffectSummaryArtifactSource? artifactSource,
        int sourcePriority,
        string? sourcePath,
        EffectSummaryCompatibilityReporter? compatibilityReporter)
    {
        private readonly EffectSummaryEntryTrustMetadata _trust = new(
            assemblyIdentity,
            methodIdentity,
            artifactSource,
            sourcePriority,
            BuiltInSourcePriority,
            AdditionalSourcePriority,
            sourcePath,
            compatibilityReporter);

        public string Symbol { get; } = symbol;
        public string DisplayName { get; } = displayName;
        public bool HasPurity => classification.HasValue;
        public PurityEntry Classification => classification!.Value;
        public ImmutableArray<SummaryExceptionInfo> ExceptionInfos { get; } = exceptionInfos;
        public SummaryAssemblyIdentity? AssemblyIdentity => _trust.AssemblyIdentity;
        public SummaryMethodIdentity? MethodIdentity => _trust.MethodIdentity;
        public int SourcePriority => _trust.SourcePriority;
        public string? SourcePath => _trust.SourcePath;

        public bool IsTrustedFor(
            IMethodSymbol methodSymbol,
            ActualAssemblyIdentity? actualAssemblyIdentity,
            ActualMethodIdentity? actualMethodIdentity) =>
            _trust.IsTrustedFor(methodSymbol, actualAssemblyIdentity, actualMethodIdentity, DisplayName);

        public bool IsTrustedFor(
            ActualAssemblyIdentity? actualAssemblyIdentity,
            ActualMethodIdentity? actualMethodIdentity) =>
            _trust.IsTrustedFor(actualAssemblyIdentity, actualMethodIdentity, DisplayName);
    }

    internal readonly struct PurityEntry(
        string classification,
        ImmutableArray<string> categories,
        string primaryCategory,
        bool hasFreshArrayAllocationEvidence,
        string freshnessClassification,
        bool hasUnsupportedEffects,
        string effectVisibilityClassification)
    {
        public string Classification { get; } = classification;
        public ImmutableArray<string> Categories { get; } = categories;
        public string PrimaryCategory { get; } = primaryCategory;
        public bool HasFreshArrayAllocationEvidence { get; } = hasFreshArrayAllocationEvidence;
        public string FreshnessClassification { get; } = freshnessClassification;
        public bool HasUnsupportedEffects { get; } = hasUnsupportedEffects;
        public string EffectVisibilityClassification { get; } = effectVisibilityClassification;
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

    internal readonly struct TrustedPurityEntry(
        string source,
        string value,
        PurityEntry classification,
        bool isSelected)
    {
        internal string Source { get; } = source;
        internal string Value { get; } = value;
        internal PurityEntry Classification { get; } = classification;
        internal bool IsSelected { get; } = isSelected;
    }

    internal sealed record SummaryExceptionInfo(
        string ExceptionType,
        ImmutableArray<string> Sources,
        ImmutableArray<SummaryExceptionEdgeInfo> Edges);

    internal sealed record SummaryExceptionEdgeInfo(
        string? SourcePath,
        ImmutableArray<StructuralMethodIdentity> CallChain,
        StructuralMethodIdentity? CalleeIdentity,
        int? Depth);

    private sealed class SummaryExceptionEdgeInfoComparer : IEqualityComparer<SummaryExceptionEdgeInfo>
    {
        internal static readonly SummaryExceptionEdgeInfoComparer Instance = new();

        public bool Equals(SummaryExceptionEdgeInfo? left, SummaryExceptionEdgeInfo? right) =>
            ReferenceEquals(left, right) ||
            left is not null && right is not null &&
            string.Equals(left.SourcePath, right.SourcePath, StringComparison.Ordinal) &&
            left.CallChain.SequenceEqual(right.CallChain) &&
            object.Equals(left.CalleeIdentity, right.CalleeIdentity) &&
            left.Depth == right.Depth;

        public int GetHashCode(SummaryExceptionEdgeInfo edge)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (edge.SourcePath == null ? 0 : StringComparer.Ordinal.GetHashCode(edge.SourcePath));
                foreach (var identity in edge.CallChain) hash = hash * 31 + identity.GetHashCode();
                hash = hash * 31 + (edge.CalleeIdentity?.GetHashCode() ?? 0);
                return hash * 31 + (edge.Depth ?? 0);
            }
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly EffectSummaryCatalog? _previous;

        public Scope(EffectSummaryCatalog? previous, EffectSummaryCatalog? current)
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
