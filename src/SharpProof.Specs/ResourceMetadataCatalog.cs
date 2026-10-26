
namespace SharpProof.Specs;

// Reviewed cost declarations are separate from functional/effect facets. A
// consumer must bind their assumption and actual reference image to its owner.
internal static class ResourceMetadataCatalog
{
    internal const string Version = "1";
    internal const string Runtime = "net8.0.32";
    internal const string CostModel = "loop-entries-and-primitive-array-elements-v3";
    internal const string EvidenceSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Math.cs";
    internal static string ContentSha256 { get; } = ResourceCatalogIdentity.Sha256Upper("resource-metadata-v" + Version + "\n" + ApiSpecTable.Default.ContentSha256 + "\n" +
        EvidenceSource + "\n" + Runtime + "\n" + CostModel + "\nbcl.math.max.int32-int32:0,0,0\nbcl.math.min.int32-int32:0,0,0\n");

    internal static bool IsReviewed(ResourceMetadataReference reference)
    {
        reference = ArgumentNullGuard.NotNull(reference, nameof(reference));
        return reference.CatalogSha256 == ContentSha256 &&
            reference.WitnessIdentifier is "bcl.math.max.int32-int32" or "bcl.math.min.int32-int32" &&
            ResourceCatalogIdentity.IsReviewedFrameworkReference(reference.AssemblyIdentity);
    }

    internal static string AssumptionId(string callableId, ResourceMetadataReference reference)
    {
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        reference = ArgumentNullGuard.NotNull(reference, nameof(reference));
        return "resource-metadata-" + Runtime + ":" + reference.CatalogSha256 + ":" + reference.WitnessIdentifier + ":" +
            EvidenceSource + ":owner:" + ResourceCatalogIdentity.OwnerHash(callableId, reference.AssemblyIdentity);
    }
}
