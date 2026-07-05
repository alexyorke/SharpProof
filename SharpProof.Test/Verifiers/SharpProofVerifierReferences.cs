using Microsoft.CodeAnalysis;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    internal static class SharpProofVerifierReferences
    {
        internal static MetadataReference AnalyzerReference { get; } =
            MetadataReference.CreateFromFile(typeof(SharpProofAnalyzer).Assembly.Location);

        internal static MetadataReference EnforcePureAttributeReference { get; } =
            MetadataReference.CreateFromFile(typeof(SharpProof.Attributes.EnforcePureAttribute).Assembly.Location);

        internal static MetadataReference PureAttributeReference { get; } =
            MetadataReference.CreateFromFile(typeof(SharpProof.Attributes.PureAttribute).Assembly.Location);
    }
}
