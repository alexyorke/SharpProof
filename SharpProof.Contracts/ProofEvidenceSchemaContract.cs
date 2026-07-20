namespace SharpProof.Schema;

public static class SharpProofEvidenceSchema {
    public const int CurrentVersion = 2;

    public const string DiagnosticVersionProperty = "sharpproof.evidence.schema_version";

    internal static ImmutableDictionary<string, string?> AddDiagnosticProperties(
        ImmutableDictionary<string, string?> properties) => properties
        .SetItem(
            DiagnosticVersionProperty,
            CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
