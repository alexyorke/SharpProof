namespace SharpProof.Analyzer;

internal enum EffectSummaryJsonFailureKind {
    None,
    MalformedJson,
    NonObjectRoot,
    MissingSchemaVersion,
    UnsupportedSchemaVersion
}

internal readonly record struct EffectSummaryJsonFailure(
    EffectSummaryJsonFailureKind Kind,
    int? SchemaVersion = null);

internal static class EffectSummaryJsonParser {
    internal static bool TryParse(
        string json,
        out JsonDocument document,
        out EffectSummaryJsonFailure failure) {
        try {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException) {
            document = null!;
            failure = new EffectSummaryJsonFailure(EffectSummaryJsonFailureKind.MalformedJson);
            return false;
        }

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return Fail(EffectSummaryJsonFailureKind.NonObjectRoot, ref document, out failure);

        if (!root.TryGetProperty("SchemaVersion", out var schemaVersionElement) ||
            schemaVersionElement.ValueKind != JsonValueKind.Number ||
            !schemaVersionElement.TryGetInt32(out var schemaVersion))
            return Fail(EffectSummaryJsonFailureKind.MissingSchemaVersion, ref document, out failure);

        if (schemaVersion != EffectSummarySchemaContract.CurrentVersion)
            return Fail(
                EffectSummaryJsonFailureKind.UnsupportedSchemaVersion,
                ref document,
                out failure,
                schemaVersion);

        failure = default;
        return true;
    }

    private static bool Fail(
        EffectSummaryJsonFailureKind kind,
        ref JsonDocument document,
        out EffectSummaryJsonFailure failure,
        int? schemaVersion = null) {
        document.Dispose();
        document = null!;
        failure = new EffectSummaryJsonFailure(kind, schemaVersion);
        return false;
    }
}
