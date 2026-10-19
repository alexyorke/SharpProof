using System.Security.Cryptography;
using System.Text;

namespace SharpProof.Compiler.Semantics;

// Explicit assumption boundary, independent of effect/termination facets.
// These methods were reviewed in the pinned .NET 8 source. Other methods
// and runtime families receive no resource summary from this catalog.
internal static class ResourceMetadataCostCatalog
{
    internal const string Version = "1";
    internal const string EvidenceSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Math.cs";
    internal static string ContentSha256 { get; } = ComputeDigest();

    private static string ComputeDigest()
    {
        var declaration = "resource-metadata-v" + Version + "\n" + ApiSpecTable.Default.ContentSha256 + "\n" +
            EvidenceSource + "\nnet8.0.32\nloop-entries-and-primitive-array-elements-v3\n" +
            "bcl.math.max.int32-int32:0,0,0\nbcl.math.min.int32-int32:0,0,0\n";
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(declaration))).Replace("-", string.Empty);
    }

    internal static bool Contains(ResolvedApiSpec spec)
    {
        spec = ArgumentNullGuard.NotNull(spec, nameof(spec));
        return spec.Symbol.ContainingAssembly.Identity.Version.Major == 8 &&
            spec.Template.Target.WitnessIdentifier is "bcl.math.max.int32-int32" or "bcl.math.min.int32-int32";
    }
}

internal sealed partial class ResourceBoundsAnalysis
{
    private Cost MetadataCall(IInvocationOperation invocation, int depth)
    {
        invocation = ArgumentNullGuard.NotNull(invocation, nameof(invocation));
        if (_compilation is null || invocation.Instance is not null ||
            !invocation.TargetMethod.IsStatic || !invocation.TargetMethod.ContainingType.StaticConstructors.IsEmpty)
        { return Unknown("OpaqueCall"); }
        _resourceSpecifications ??= new ApiSpecResolver(ApiSpecTable.Default).Resolve(_compilation);
        if (!_resourceSpecifications.TryGet(invocation.TargetMethod, out var spec) || !ResourceMetadataCostCatalog.Contains(spec))
        { return Unknown("OpaqueCall"); }
        var arguments = Cost.Zero;
        foreach (var argument in invocation.Arguments)
        {
            arguments = arguments.Add(Visit(argument.Value, depth + 1));
            if (_unknown is not null)
            { return Cost.Zero; }
        }
        _assumptions.Add("resource-metadata-net8.0.32:" + ResourceMetadataCostCatalog.ContentSha256 + ":" +
            spec.Template.Target.WitnessIdentifier + ":" + ResourceMetadataCostCatalog.EvidenceSource);
        return arguments.Add(new Cost(Bound.Zero, Bound.Zero, Bound.Zero, ResourceProgram.MetadataBoundary));
    }
}
