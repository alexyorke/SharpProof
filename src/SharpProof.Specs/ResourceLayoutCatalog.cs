using System.Security.Cryptography;
using System.Text;

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
        return assembly is
            "System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e" or
            "System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
    }

    internal static string AssumptionId(string callableId, string assembly)
    {
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        assembly = ArgumentNullGuard.NotNull(assembly, nameof(assembly));
        using var hash = SHA256.Create();
        var owner = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(callableId + "\n" + assembly))).Replace("-", string.Empty);
        return "resource-metadata-" + ResourceRuntimeLayout.ModelId + ":" + ContentSha256 + ":" + Witness + ":" +
            ResourceRuntimeLayout.EvidenceSource + ":owner:" + owner;
    }
}
