namespace SharpProof.Schema;

internal static class ProofEvidenceSchemaContract
{
    internal const int CurrentVersion = 2;
    internal const int MinimumReadCompatibleVersion = CurrentVersion;
    internal const string CompatibilityPolicy = "exact-v2";

    internal const string DiagnosticVersionProperty = "sharpproof.evidence.schema_version";
    internal const string DiagnosticCompatibilityProperty = "sharpproof.evidence.schema_compatibility";

    internal static bool IsReadCompatible(int schemaVersion)
    {
        return schemaVersion >= MinimumReadCompatibleVersion &&
               schemaVersion <= CurrentVersion;
    }
}
