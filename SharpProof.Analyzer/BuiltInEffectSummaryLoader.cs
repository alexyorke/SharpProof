namespace SharpProof.Analyzer;

internal static class BuiltInEffectSummaryLoader
{
    internal const string SummaryFileName = "SharpProof.EffectSummary.json";

    internal static void LoadBuiltInSummaryJsonDocuments(Action<string> addJson)
    {
        LoadEmbeddedSummaryJsonDocuments(addJson);
    }

    internal static void LoadAdditionalSummaryJsonDocuments(
        AnalyzerOptions options,
        CancellationToken cancellationToken,
        Action<string, string> addJson)
    {
        foreach (var additionalFile in options.AdditionalFiles)
        {
            var path = additionalFile.Path ?? string.Empty;
            if (!IsSummaryFile(path)) continue;

            var text = additionalFile.GetText(cancellationToken)?.ToString();
            if (text == null || string.IsNullOrWhiteSpace(text)) continue;

            addJson(path, text);
        }
    }

    internal static bool HasAdditionalSummaryJsonDocuments(AnalyzerOptions options)
    {
        foreach (var additionalFile in options.AdditionalFiles)
            if (IsSummaryFile(additionalFile.Path))
                return true;

        return false;
    }

    internal static TCatalog LoadCatalogWithAdditionalDocuments<TCatalog, TEntry>(
        AnalyzerOptions options,
        CancellationToken cancellationToken,
        TCatalog builtInCatalog,
        Func<TCatalog, Dictionary<string, ImmutableArray<TEntry>.Builder>> cloneEntries,
        int sourcePriority,
        Func<EffectSummaryJsonDocument, int, string?, EffectSummaryCompatibilityReporter?, IEnumerable<TEntry>>
            parseEntries,
        Func<Dictionary<string, ImmutableArray<TEntry>.Builder>, TCatalog> createCatalog,
        EffectSummaryCompatibilityReporter compatibilityReporter)
        where TEntry : IEffectSummaryCatalogEntry
    {
        if (!HasAdditionalSummaryJsonDocuments(options)) return builtInCatalog;

        var entries = cloneEntries(builtInCatalog);
        LoadAdditionalSummaryJsonDocuments(
            options,
            cancellationToken,
            (path, json) => EffectSummaryCatalogEntryMap.AddJson(
                entries,
                json,
                sourcePriority,
                path,
                compatibilityReporter,
                parseEntries));
        return createCatalog(entries);
    }

    internal static TCatalog LoadBuiltInCatalog<TCatalog, TEntry>(
        int sourcePriority,
        Func<EffectSummaryJsonDocument, int, string?, EffectSummaryCompatibilityReporter?, IEnumerable<TEntry>>
            parseEntries,
        Func<Dictionary<string, ImmutableArray<TEntry>.Builder>, TCatalog> createCatalog)
        where TEntry : IEffectSummaryCatalogEntry
    {
        var entries = new Dictionary<string, ImmutableArray<TEntry>.Builder>(StringComparer.Ordinal);
        LoadBuiltInSummaryJsonDocuments(json => EffectSummaryCatalogEntryMap.AddJson(
            entries,
            json,
            sourcePriority,
            null,
            null,
            parseEntries));
        return createCatalog(entries);
    }

    internal static bool IsSummaryFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, SummaryFileName, StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith("." + SummaryFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static void LoadEmbeddedSummaryJsonDocuments(Action<string> addJson)
    {
        var assembly = typeof(BuiltInEffectSummaryLoader).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!IsSummaryResource(resourceName)) continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            addJson(reader.ReadToEnd());
        }
    }

    private static bool IsSummaryResource(string resourceName)
    {
        return resourceName.EndsWith("." + SummaryFileName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(resourceName, SummaryFileName, StringComparison.OrdinalIgnoreCase);
    }
}
