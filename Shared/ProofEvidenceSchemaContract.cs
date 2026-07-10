namespace SharpProof.Schema;

internal static class ProofEvidenceSchemaContract
{
    internal const int LegacyUnversionedVersion = 0;
    internal const int CurrentVersion = 1;
    internal const int MinimumReadCompatibleVersion = LegacyUnversionedVersion;
    internal const string CompatibilityPolicy = "additive-v1";

    internal const string DiagnosticVersionProperty = "sharpproof.evidence.schema_version";
    internal const string DiagnosticCompatibilityProperty = "sharpproof.evidence.schema_compatibility";

    internal static bool IsReadCompatible(int schemaVersion)
    {
        return schemaVersion >= MinimumReadCompatibleVersion &&
               schemaVersion <= CurrentVersion;
    }
}
