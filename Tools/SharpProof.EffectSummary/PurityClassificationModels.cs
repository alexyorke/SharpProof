internal sealed record PurityClassificationOutput(
    AssemblyEffectReport[] Assemblies,
    PurityClassificationReport Report,
    GeneratedPurityCatalogDocument GeneratedPurityCatalog);

internal sealed record PurityClassificationReport(
    int SchemaVersion,
    int MethodCount,
    int PureCount,
    int ImpureCount,
    int ConservativeUnknownCount,
    CatalogComparisonReport? CatalogComparison);

internal sealed record CatalogComparisonReport(
    CatalogComparisonRow[] KnownPureMembers,
    CatalogComparisonRow[] KnownImpureMembers,
    CatalogComparisonRow[] KnownFreshOwnedArrayReturningMembers);

internal sealed record CatalogComparisonRow(
    string DisplayName,
    string Catalog,
    string Classification,
    string[] Categories,
    string[] FirstBlockingCallChain,
    string EffectVisibilityClassification,
    string? Note,
    string[] MatchedCanonicalKeys);

internal sealed record GeneratedPurityCatalogDocument(
    int SchemaVersion,
    GeneratedPurityCatalogEntry[] Entries);

internal sealed record GeneratedPurityCatalogEntry(
    [property: JsonPropertyName("DisplayName"), JsonPropertyOrder(1)] string DisplayName,
    string CacheKey,
    string AssemblyName,
    string AssemblyPath,
    EffectSummaryArtifactSource? ArtifactSource,
    string AssemblySha256,
    string ModuleVersionId,
    string MetadataToken,
    string? MethodBodySha256,
    string Classification,
    string PrimaryCategory,
    string[] Categories,
    string[] FirstBlockingCallChain,
    bool HasFreshArrayAllocationEvidence,
    bool HasFreshObjectAllocationEvidence,
    bool HasUnsupportedEffects,
    string FreshnessClassification,
    string EffectVisibilityClassification) {
    [JsonIgnore] public string Symbol => DisplayName;

    [JsonPropertyOrder(2)]
    public StructuralMethodIdentity Identity { get; init; } = null!;

    [JsonPropertyOrder(3)]
    public string CanonicalKey => Identity.ToCanonicalKey();
}

internal sealed record MethodPurityClassification(
    string Classification,
    string[] Categories,
    string[] FirstBlockingCallChain,
    bool HasFreshArrayAllocationEvidence,
    bool HasFreshObjectAllocationEvidence,
    [property: JsonPropertyName("HasUnsupportedEffects")]
    bool HasUnsupportedEffects,
    string FreshnessClassification,
    string EffectVisibilityClassification);
