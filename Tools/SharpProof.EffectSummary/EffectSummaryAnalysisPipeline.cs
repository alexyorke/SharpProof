internal static class EffectSummaryAnalysisPipeline
{
    public static EffectSummaryDocument Analyze(
        CliOptions options,
        IReadOnlyList<string>? inputAssemblies = null,
        IReadOnlyDictionary<string, GeneratedPurityCatalogEntry>? externalGeneratedPurityEntries = null)
    {
        var assemblies = inputAssemblies ?? EffectSummaryInputResolver.ResolveAssemblies(options);
        var reports = assemblies
            .Select(path => AssemblyEffectSummarizer.Summarize(
                path,
                options.Limit,
                options.SymbolPrefixes,
                options.ExactSymbols,
                options.CanonicalKeys,
                options.IncludeCallees,
                options.MaxDepth,
                options.IncludeTransitiveRoots,
                options.MaxExceptionEdges) with
            {
                ArtifactSource = options.GetArtifactSource(path)
            })
            .ToArray();

        if (options.ExcludedSymbolPrefixes.Count > 0)
            reports = reports
                .Select(report => ArtifactSpecSymbolFilter.Exclude(report, options.ExcludedSymbolPrefixes))
                .ToArray();

        PurityClassificationReport? purityClassificationReport = null;
        GeneratedPurityCatalogDocument? generatedPurityCatalog = null;
        if (options.IncludePurityClassification || options.CompareManualCatalogs)
        {
            var classificationOutput = PurityClassificationEngine.Classify(
                reports,
                options.CompareManualCatalogs,
                externalGeneratedPurityEntries);
            reports = classificationOutput.Assemblies;
            purityClassificationReport = classificationOutput.Report;
            generatedPurityCatalog = classificationOutput.GeneratedPurityCatalog;
        }

        var bclFallbackInventory = options.IncludeBclFallbackInventory
            ? BclFallbackInventoryBuilder.Build(reports)
            : null;

        return new EffectSummaryDocument(
            EffectSummarySchemaContract.CurrentVersion,
            DateTimeOffset.UtcNow,
            reports,
            purityClassificationReport,
            generatedPurityCatalog,
            bclFallbackInventory);
    }
}
