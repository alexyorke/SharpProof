internal static class GeneratedPurityCatalogReader
{
    public static IEnumerable<GeneratedPurityCatalogEntry> ReadEntries(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("GeneratedPurityCatalog", out var catalog) ||
            catalog.ValueKind != JsonValueKind.Object ||
            !catalog.TryGetProperty("Entries", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var element in entries.EnumerateArray())
            if (TryReadEntry(element, out var entry))
                yield return entry;
    }

    private static bool TryReadEntry(JsonElement element, out GeneratedPurityCatalogEntry entry)
    {
        entry = null!;
        if (!element.TryGetProperty("DisplayName", out var displayNameElement) ||
            displayNameElement.ValueKind != JsonValueKind.String ||
            displayNameElement.GetString() is not { Length: > 0 } displayName ||
            !element.TryGetProperty("CanonicalKey", out var canonicalKeyElement) ||
            canonicalKeyElement.ValueKind != JsonValueKind.String ||
            !StructuralMethodIdentity.TryParseCanonicalKey(canonicalKeyElement.GetString(), out var identity))
            return false;

        static string ReadString(JsonElement source, string name) =>
            source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        static bool ReadBoolean(JsonElement source, string name) =>
            source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        static string[] ReadStrings(JsonElement source, string name) =>
            source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString() ?? string.Empty)
                    .ToArray()
                : Array.Empty<string>();

        EffectSummaryArtifactSource? artifactSource = null;
        if (element.TryGetProperty("ArtifactSource", out var artifactSourceElement) &&
            artifactSourceElement.ValueKind == JsonValueKind.Object)
            artifactSource = JsonSerializer.Deserialize<EffectSummaryArtifactSource>(artifactSourceElement.GetRawText());

        entry = new GeneratedPurityCatalogEntry(
            displayName,
            ReadString(element, "CacheKey"),
            ReadString(element, "AssemblyName"),
            ReadString(element, "AssemblyPath"),
            artifactSource,
            ReadString(element, "AssemblySha256"),
            ReadString(element, "ModuleVersionId"),
            ReadString(element, "MetadataToken"),
            element.TryGetProperty("MethodBodySha256", out var bodyHash) &&
            bodyHash.ValueKind == JsonValueKind.String
                ? bodyHash.GetString()
                : null,
            ReadString(element, "Classification"),
            ReadString(element, "PrimaryCategory"),
            ReadStrings(element, "Categories"),
            ReadStrings(element, "FirstBlockingCallChain"),
            ReadBoolean(element, "HasFreshArrayAllocationEvidence"),
            ReadBoolean(element, "HasFreshObjectAllocationEvidence"),
            ReadBoolean(element, "HasUnsupportedEffects"),
            ReadString(element, "FreshnessClassification"),
            ReadString(element, "EffectVisibilityClassification"))
        {
            Identity = identity
        };
        return true;
    }
}
