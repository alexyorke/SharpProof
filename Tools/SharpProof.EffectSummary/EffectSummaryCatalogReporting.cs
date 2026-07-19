internal static class EffectSummaryCatalogReporting
{
    internal static PurityClassificationReport BuildReport(
        IReadOnlyList<MethodEffectSummary> methods,
        bool includeCatalogComparison)
    {
        var pureCount = methods.Count(static method => string.Equals(
            method.PurityClassification?.Classification,
            "pure",
            StringComparison.Ordinal));
        var impureCount = methods.Count(static method => string.Equals(
            method.PurityClassification?.Classification,
            "impure",
            StringComparison.Ordinal));
        var unknownCount = methods.Count - pureCount - impureCount;

        return new PurityClassificationReport(
            EffectSummarySchemaContract.CurrentVersion,
            methods.Count,
            pureCount,
            impureCount,
            unknownCount,
            includeCatalogComparison
                ? BuildCatalogComparison(methods)
                : null);
    }

    internal static CatalogComparisonReport BuildCatalogComparison(
        IReadOnlyList<MethodEffectSummary> _) =>
        new(
            Array.Empty<CatalogComparisonRow>(),
            Array.Empty<CatalogComparisonRow>(),
            Array.Empty<CatalogComparisonRow>());

    internal static GeneratedPurityCatalogDocument BuildGeneratedPurityCatalog(
        IReadOnlyList<AssemblyEffectReport> assemblies)
    {
        return new GeneratedPurityCatalogDocument(
            EffectSummarySchemaContract.CurrentVersion,
            assemblies
                .SelectMany(assembly => assembly.Methods.Select(method => CreateGeneratedPurityEntry(assembly, method)))
                .OrderBy(static entry => entry.CanonicalKey, StringComparer.Ordinal)
                .ToArray());
    }

    internal static Dictionary<string, GeneratedPurityCatalogEntry> MergeGeneratedPurityEntries(
        IEnumerable<GeneratedPurityCatalogEntry> entries)
    {
        var candidatesByKey = new Dictionary<string, List<GeneratedPurityCatalogEntry>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!candidatesByKey.TryGetValue(entry.CanonicalKey, out var candidates))
            {
                candidates = new List<GeneratedPurityCatalogEntry>();
                candidatesByKey.Add(entry.CanonicalKey, candidates);
            }

            candidates.Add(entry);
        }

        var resolvedEntries = new Dictionary<string, GeneratedPurityCatalogEntry>(StringComparer.Ordinal);
        foreach (var pair in candidatesByKey)
        {
            var resolvedEntry = ResolveGeneratedPurityEntryCandidates(pair.Value);
            if (resolvedEntry != null) resolvedEntries[pair.Key] = resolvedEntry;
        }

        return resolvedEntries;
    }

    internal static GeneratedPurityCatalogEntry? ResolveGeneratedPurityEntryCandidates(
        IReadOnlyList<GeneratedPurityCatalogEntry> candidates)
    {
        GeneratedPurityCatalogEntry? bestEntry = null;
        foreach (var implementationGroup in candidates
                     .GroupBy(CreateGeneratedPurityImplementationKey, StringComparer.Ordinal))
        {
            var resolvedEntry = ResolveSameImplementationGeneratedPurityEntries(
                implementationGroup.ToArray());
            if (resolvedEntry == null) continue;

            bestEntry = bestEntry == null
                ? resolvedEntry
                : ResolveDominantGeneratedPurityEntry(bestEntry, resolvedEntry);
            if (bestEntry == null) return null;
        }

        return bestEntry;
    }

    internal static GeneratedPurityCatalogEntry? ResolveSameImplementationGeneratedPurityEntries(
        IReadOnlyList<GeneratedPurityCatalogEntry> candidates)
    {
        if (candidates.Count == 0) return null;

        GeneratedPurityCatalogEntry? bestEntry = candidates[0];
        for (var i = 1; i < candidates.Count; i++)
        {
            bestEntry = ResolveDominantGeneratedPurityEntry(bestEntry, candidates[i]);
            if (bestEntry == null) return null;
        }

        return bestEntry;
    }

    private static GeneratedPurityCatalogEntry? ResolveDominantGeneratedPurityEntry(
        GeneratedPurityCatalogEntry left,
        GeneratedPurityCatalogEntry right)
    {
        if (GeneratedPurityCatalogEntryRelations.AreEquivalent(left, right)) return left;

        var leftDominates = GeneratedPurityCatalogEntryRelations.DoesDominate(left, right);
        var rightDominates = GeneratedPurityCatalogEntryRelations.DoesDominate(right, left);
        return leftDominates == rightDominates ? null : rightDominates ? right : left;
    }

    internal static bool HaveSameGeneratedPurityEntryMap(
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> left,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> right)
    {
        if (left.Count != right.Count) return false;

        foreach (var pair in left)
            if (!right.TryGetValue(pair.Key, out var rightEntry) ||
                !GeneratedPurityCatalogEntryRelations.AreEquivalent(pair.Value, rightEntry))
                return false;

        return true;
    }

    internal static string CreateGeneratedPurityImplementationKey(GeneratedPurityCatalogEntry entry)
    {
        return string.Join(
            "|",
            entry.AssemblyName,
            entry.AssemblySha256,
            entry.ModuleVersionId,
            entry.MetadataToken,
            entry.MethodBodySha256 ?? string.Empty);
    }

    internal static GeneratedPurityCatalogEntry CreateGeneratedPurityEntry(
        AssemblyEffectReport assembly,
        MethodEffectSummary method)
    {
        var classification = method.PurityClassification ?? CreateUnknown(
            new[] { "missing_classification" },
            Array.Empty<string>(),
            method);

        return new GeneratedPurityCatalogEntry(
            method.Symbol,
            method.CacheKey,
            assembly.AssemblyName,
            assembly.AssemblyPath,
            assembly.ArtifactSource,
            assembly.AssemblySha256,
            assembly.ModuleVersionId,
            method.MetadataToken,
            method.MethodBodySha256,
            classification.Classification,
            GetPrimaryCategory(classification.Categories),
            classification.Categories,
            classification.FirstBlockingCallChain,
            classification.HasFreshArrayAllocationEvidence,
            classification.HasFreshObjectAllocationEvidence,
            classification.HasUnsupportedEffects,
            classification.FreshnessClassification,
            classification.EffectVisibilityClassification)
        {
            Identity = method.Identity
        };
    }

    internal static string GetPrimaryCategory(IReadOnlyList<string> categories)
    {
        if (categories.Contains("global_state_write", StringComparer.Ordinal)) return "global_state_write";

        return categories.FirstOrDefault() ?? "generated_purity_summary";
    }

}
