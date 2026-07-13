internal sealed record PurityClassificationContext(
    AssemblyEffectReport Assembly,
    IReadOnlyDictionary<string, MethodEffectSummary> BySymbol,
    IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> ExternalGeneratedPurityEntries,
    IReadOnlyDictionary<string, GeneratedPurityCatalogEntry> ReviewedGeneratedPurityEntries)
{
    internal Dictionary<string, MethodPurityClassification> Memo { get; } =
        new(StringComparer.Ordinal);

    internal Dictionary<string, bool> FreshOwnedInitializationMemo { get; } =
        new(StringComparer.Ordinal);

    internal Dictionary<string, bool> ValidationThrowHelperMemo { get; } =
        new(StringComparer.Ordinal);

    internal HashSet<string> Visiting { get; } = new(StringComparer.Ordinal);
}
