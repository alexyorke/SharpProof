using Microsoft.CodeAnalysis;
namespace SharpProof.Test
{
    internal static class SharpProofVerifierReferences
    {
        internal static MetadataReference EnforcePureAttributeReference { get; } =
            MetadataReference.CreateFromFile(typeof(SharpProof.Attributes.EnforcePureAttribute).Assembly.Location);
    }
}
