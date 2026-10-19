using System.Security.Cryptography;
using System.Text;

namespace SharpProof.Specs;

// Reviewed cost declarations are separate from functional/effect facets. A
// consumer must bind their assumption and actual reference image to its owner.
internal static class ResourceMetadataCatalog
{
    internal const string Version = "1";
    internal const string Runtime = "net8.0.32";
    internal const string CostModel = "loop-entries-and-primitive-array-elements-v3";
    internal const string EvidenceSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Math.cs";
    internal static string ContentSha256 { get; } = Hash("resource-metadata-v" + Version + "\n" + ApiSpecTable.Default.ContentSha256 + "\n" +
        EvidenceSource + "\n" + Runtime + "\n" + CostModel + "\nbcl.math.max.int32-int32:0,0,0\nbcl.math.min.int32-int32:0,0,0\n");

    internal static bool IsReviewed(ResourceMetadataReference reference)
    {
        reference = ArgumentNullGuard.NotNull(reference, nameof(reference));
        return reference.CatalogSha256 == ContentSha256 &&
            reference.WitnessIdentifier is "bcl.math.max.int32-int32" or "bcl.math.min.int32-int32" &&
            reference.AssemblyIdentity is
                "System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e" or
                "System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
    }

    internal static string AssumptionId(string callableId, ResourceMetadataReference reference)
    {
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        reference = ArgumentNullGuard.NotNull(reference, nameof(reference));
        return "resource-metadata-" + Runtime + ":" + reference.CatalogSha256 + ":" + reference.WitnessIdentifier + ":" +
            EvidenceSource + ":owner:" + Hash(callableId + "\n" + reference.AssemblyIdentity);
    }

    private static string Hash(string value)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
    }
}
