namespace SharpProof.Schema;

public static class SharpProofEvidenceSchema
{
    public const int CurrentVersion = 2;
    public const int MinimumReadCompatibleVersion = CurrentVersion;
    public const string CompatibilityPolicy = "exact-v2";

    public const string DiagnosticVersionProperty = "sharpproof.evidence.schema_version";
    public const string DiagnosticCompatibilityProperty = "sharpproof.evidence.schema_compatibility";

    public static bool IsReadCompatible(int schemaVersion)
    {
        return schemaVersion >= MinimumReadCompatibleVersion &&
               schemaVersion <= CurrentVersion;
    }

    internal static ImmutableDictionary<string, string?> AddDiagnosticProperties(
        ImmutableDictionary<string, string?> properties) => properties
        .SetItem(
            DiagnosticVersionProperty,
            CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .SetItem(DiagnosticCompatibilityProperty, CompatibilityPolicy);
}
