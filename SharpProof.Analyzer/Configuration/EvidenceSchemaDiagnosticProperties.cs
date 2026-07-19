using System.Globalization;

namespace SharpProof.Analyzer.Configuration;

internal static class EvidenceSchemaDiagnosticProperties
{
    internal static ImmutableDictionary<string, string?> Add(
        ImmutableDictionary<string, string?> properties)
    {
        return properties
            .SetItem(
                SharpProofDiagnostics.EvidenceSchemaVersionProperty,
                SharpProofEvidenceSchema.CurrentVersion.ToString(CultureInfo.InvariantCulture))
            .SetItem(
                SharpProofDiagnostics.EvidenceSchemaCompatibilityProperty,
                SharpProofEvidenceSchema.CompatibilityPolicy);
    }
}
