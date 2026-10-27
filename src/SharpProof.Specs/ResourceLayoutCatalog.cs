
namespace SharpProof.Specs;

// Binding identity for the declared fresh-instance runtime layout dependency.
// Reference images identify compiler inputs; they do not detect deployment.
internal static class ResourceLayoutCatalog
{
    internal const string Witness = "runtime-layout.coreclr-x64";
    internal static string ContentSha256 { get; } = ResourceRuntimeLayout.ContentSha256.ToUpperInvariant();

    internal static bool IsReviewedReference(string assembly)
    {
        assembly = ArgumentNullGuard.NotNull(assembly, nameof(assembly));
        return ResourceCatalogIdentity.IsReviewedFrameworkReference(assembly);
    }

    internal static string AssumptionId(string callableId, string assembly)
    {
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        assembly = ArgumentNullGuard.NotNull(assembly, nameof(assembly));
        var owner = ResourceCatalogIdentity.OwnerHash(callableId, assembly);
        return "resource-metadata-" + ResourceRuntimeLayout.ModelId + ":" + ContentSha256 + ":" + Witness + ":" +
            ResourceRuntimeLayout.EvidenceSource + ":owner:" + owner;
    }
}
