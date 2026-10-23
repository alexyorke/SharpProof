namespace SharpProof.Compiler.Semantics;

// Explicit assumption boundary, independent of effect/termination facets.
// These methods were reviewed in the pinned .NET 8 source. Other methods
// and runtime families receive no resource summary from this catalog.
internal static class ResourceMetadataCostCatalog
{
    internal const string Version = ResourceMetadataCatalog.Version;
    internal const string EvidenceSource = ResourceMetadataCatalog.EvidenceSource;
    internal static string ContentSha256 => ResourceMetadataCatalog.ContentSha256;

    internal static bool Contains(ResolvedApiSpec spec)
    {
        spec = ArgumentNullGuard.NotNull(spec, nameof(spec));
        return ResourceMetadataCatalog.IsReviewed(new ResourceMetadataReference(spec.Template.Target.WitnessIdentifier,
            ContentSha256, spec.Symbol.ContainingAssembly.Identity.ToString()));
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
        return arguments.Add(new Cost(Bound.Zero, Bound.Zero, Bound.Zero, ResourceProgram.MetadataCall(new ResourceMetadataReference(spec.Template.Target.WitnessIdentifier,
            ResourceMetadataCostCatalog.ContentSha256, spec.Symbol.ContainingAssembly.Identity.ToString()))));
    }
}
