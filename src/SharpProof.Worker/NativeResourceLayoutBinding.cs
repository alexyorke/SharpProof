namespace SharpProof.Worker;

internal sealed class NativeResourceLayoutBinding
{
    private readonly CompilerCallablePreparation _target;
    private WorkerResourceMetadataAssumption? _bound;

    internal NativeResourceLayoutBinding(CompilerCallablePreparation target)
    { _target = ArgumentNullGuard.NotNull(target, nameof(target)); }

    internal ResourceRuntimeLayoutContext? Bind()
    {
        _bound = null;
        if (_target.Entry.Assumptions is null || _target.Compilation.ResourceTargetPlatform != "X64" ||
            !(_target.Compilation.TargetFramework == "net8.0" || (_target.Compilation.TargetFramework?.StartsWith("net8.0-", StringComparison.Ordinal) ?? false)) ||
            _target.Compilation.ResourceMetadataReferences is not { Length: > 0 } references || references.Any(static reference => reference is null))
        { return null; }
        var candidates = references.Where(static reference => reference is null ? throw new ArgumentNullException(nameof(reference)) :
            reference.Kind == "Assembly" && reference.Identity is { } identity && ResourceLayoutCatalog.IsReviewedReference(identity)).ToArray();
        if (candidates.Length == 0)
        { return null; }
        var identities = candidates.Select(static reference => reference is null ? throw new ArgumentNullException(nameof(reference)) : reference.Identity)
            .Distinct(StringComparer.Ordinal).ToArray();
        // One core-library identity is captured for this layout dependency.
        if (identities.Length != 1 || candidates.Any(static reference => reference is null ? throw new ArgumentNullException(nameof(reference)) :
                reference.Modules is not { Length: 1 } || reference.Modules[0] is null || !WorkerProtocolJson.IsSha256(reference.Modules[0].Sha256)))
        { return null; }
        var hashes = candidates.Select(static reference => reference is null ? throw new ArgumentNullException(nameof(reference)) : reference.Modules[0].Sha256)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var id = ResourceLayoutCatalog.AssumptionId(_target.Entry.CallableId, identities[0]);
        if (hashes.Length != 1 || !_target.Entry.Assumptions.Any(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
            assumption.Id == id && assumption.Kind == WorkerAssumptionKind.TrustedBoundary))
        { return null; }
        _bound = new()
        {
            AssumptionId = id,
            WitnessIdentifier = ResourceLayoutCatalog.Witness,
            CatalogSha256 = ResourceLayoutCatalog.ContentSha256,
            ReferenceAssembly = identities[0],
            ReferenceSha256 = hashes[0],
            RuntimeModel = ResourceRuntimeLayout.ModelId,
            EvidenceSource = ResourceRuntimeLayout.EvidenceSource
        };
        return new(ResourceRuntimeLayout.RuntimeFamily, ResourceRuntimeLayout.RuntimeVersion,
            ResourceRuntimeLayout.TargetArchitecture, ResourceRuntimeLayout.PointerBytes);
    }

    internal void Project(WorkerClaimResult result)
    {
        result = ArgumentNullGuard.NotNull(result, nameof(result));
        if (_bound is not { } dependency || result.ResourceBound is null || result.Outcome is not (WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted))
        { throw new ArgumentException("A byte proof requires its owned layout dependency.", nameof(result)); }
        var used = new HashSet<string>(result.Assumptions.Where(static assumption => assumption is not null && assumption.Used)
            .Select(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id), StringComparer.Ordinal)
        { dependency.AssumptionId };
        result.Assumptions = CallableClaimResultAssembler.MarkAssumptionsUsed(_target, used);
        result.ResourceBound.MetadataAssumptions = [.. result.ResourceBound.MetadataAssumptions, dependency];
    }
}
