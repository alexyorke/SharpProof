namespace SharpProof.Analyzer;

internal enum EffectSummaryJsonFailureKind
{
    None,
    MalformedJson,
    NonObjectRoot,
    MissingSchemaVersion,
    UnsupportedSchemaVersion
}

internal readonly record struct EffectSummaryJsonFailure(
    EffectSummaryJsonFailureKind Kind,
    int? SchemaVersion = null);

internal sealed class EffectSummaryJsonDocument : IDisposable
{
    private readonly JsonDocument _document;

    private EffectSummaryJsonDocument(JsonDocument document)
    {
        _document = document;
    }

    internal JsonElement Root => _document.RootElement;

    internal static bool TryParse(
        string json,
        out EffectSummaryJsonDocument document,
        out EffectSummaryJsonFailure failure)
    {
        JsonDocument parsedDocument;
        try
        {
            parsedDocument = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            document = null!;
            failure = new EffectSummaryJsonFailure(EffectSummaryJsonFailureKind.MalformedJson);
            return false;
        }

        var root = parsedDocument.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            parsedDocument.Dispose();
            document = null!;
            failure = new EffectSummaryJsonFailure(EffectSummaryJsonFailureKind.NonObjectRoot);
            return false;
        }

        if (!root.TryGetProperty("SchemaVersion", out var schemaVersionElement) ||
            schemaVersionElement.ValueKind != JsonValueKind.Number ||
            !schemaVersionElement.TryGetInt32(out var schemaVersion))
        {
            parsedDocument.Dispose();
            document = null!;
            failure = new EffectSummaryJsonFailure(EffectSummaryJsonFailureKind.MissingSchemaVersion);
            return false;
        }

        if (schemaVersion != EffectSummarySchemaContract.CurrentVersion)
        {
            parsedDocument.Dispose();
            document = null!;
            failure = new EffectSummaryJsonFailure(
                EffectSummaryJsonFailureKind.UnsupportedSchemaVersion,
                schemaVersion);
            return false;
        }

        document = new EffectSummaryJsonDocument(parsedDocument);
        failure = default;
        return true;
    }

    internal bool TryGetGeneratedPurityEntries(out JsonElement entries)
    {
        if (Root.TryGetProperty("GeneratedPurityCatalog", out var generatedCatalog) &&
            generatedCatalog.ValueKind == JsonValueKind.Object &&
            generatedCatalog.TryGetProperty("SchemaVersion", out var schemaVersionElement) &&
            schemaVersionElement.ValueKind == JsonValueKind.Number &&
            schemaVersionElement.TryGetInt32(out var schemaVersion) &&
            schemaVersion == EffectSummarySchemaContract.CurrentVersion &&
            generatedCatalog.TryGetProperty("Entries", out entries) &&
            entries.ValueKind == JsonValueKind.Array)
            return true;

        entries = default;
        return false;
    }

    internal IEnumerable<EffectSummaryJsonAssembly> EnumerateLegacyAssemblies()
    {
        if (!Root.TryGetProperty("Assemblies", out var assemblies) ||
            assemblies.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var assembly in assemblies.EnumerateArray())
        {
            if (assembly.ValueKind != JsonValueKind.Object ||
                !assembly.TryGetProperty("Methods", out var methods) ||
                methods.ValueKind != JsonValueKind.Array)
                continue;

            yield return new EffectSummaryJsonAssembly(assembly, methods);
        }
    }

    public void Dispose()
    {
        _document.Dispose();
    }
}

internal readonly record struct EffectSummaryJsonAssembly(
    JsonElement Element,
    JsonElement Methods)
{
    internal IEnumerable<JsonElement> EnumerateMethods()
    {
        foreach (var method in Methods.EnumerateArray())
            if (method.ValueKind == JsonValueKind.Object)
                yield return method;
    }
}
