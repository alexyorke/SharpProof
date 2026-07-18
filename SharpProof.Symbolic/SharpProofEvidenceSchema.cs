using SharpProof.Schema;

namespace SharpProof.Symbolic;

public static class SharpProofEvidenceSchema
{
    public const int CurrentVersion = ProofEvidenceSchemaContract.CurrentVersion;
    public const int MinimumReadCompatibleVersion = ProofEvidenceSchemaContract.MinimumReadCompatibleVersion;
    public const string CompatibilityPolicy = ProofEvidenceSchemaContract.CompatibilityPolicy;
    public const string DiagnosticVersionPropertyName = ProofEvidenceSchemaContract.DiagnosticVersionProperty;
    public const string DiagnosticCompatibilityPropertyName =
        ProofEvidenceSchemaContract.DiagnosticCompatibilityProperty;

    public static bool IsReadCompatible(int schemaVersion)
    {
        return ProofEvidenceSchemaContract.IsReadCompatible(schemaVersion);
    }
}
