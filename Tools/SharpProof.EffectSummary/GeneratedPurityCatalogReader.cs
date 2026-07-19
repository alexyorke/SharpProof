internal static class GeneratedPurityCatalogReader
{
    public static IEnumerable<GeneratedPurityCatalogEntry> ReadEntries(string path) =>
        JsonSerializer.Deserialize<EffectSummaryDocument>(File.ReadAllText(path))
            ?.GeneratedPurityCatalog?.Entries ?? Array.Empty<GeneratedPurityCatalogEntry>();
}
