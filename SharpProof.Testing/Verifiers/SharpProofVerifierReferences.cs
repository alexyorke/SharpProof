using Microsoft.CodeAnalysis;
using SharpProof.Attributes;

namespace SharpProof.Test;

internal static class SharpProofVerifierReferences
{
    internal static MetadataReference EnforcePureAttributeReference { get; } =
        MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location);
}
