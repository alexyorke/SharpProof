using System.Security.Cryptography;
using System.Text;

namespace SharpProof.Specs;

// Shared identity rules for the reviewed resource catalogs. Catalog owner
// hashes use uppercase hex; they are part of persisted assumption identities.
internal static class ResourceCatalogIdentity
{
    internal static bool IsReviewedFrameworkReference(string assembly)
    {
        assembly = ArgumentNullGuard.NotNull(assembly, nameof(assembly));
        return assembly is
            "System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e" or
            "System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
    }

    internal static string OwnerHash(string callableId, string assembly)
    {
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        assembly = ArgumentNullGuard.NotNull(assembly, nameof(assembly));
        return Sha256Upper(callableId + "\n" + assembly);
    }

    internal static string Sha256Upper(string value)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
    }
}
