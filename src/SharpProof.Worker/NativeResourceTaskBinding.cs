namespace SharpProof.Worker;

// The compiler owns the closed task projection; this independently binds its
// conditional runtime premise to the owning manifest and reference image.
internal static class NativeResourceTaskBinding
{
    internal static WorkerResourceMetadataAssumption? Bind(CompilerCallablePreparation target, CancellationToken cancellation)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return BindCore(target, false, cancellation);
    }

    internal static WorkerResourceMetadataAssumption? BindStorage(CompilerCallablePreparation target, CancellationToken cancellation)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return BindCore(target, true, cancellation);
    }

    private static WorkerResourceMetadataAssumption? BindCore(CompilerCallablePreparation target, bool storage, CancellationToken cancellation)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        cancellation.ThrowIfCancellationRequested();
        if (target.ForkJoinProjection is not { } projection || target.TotalEntry is not { } entry || target.Total != null ||
            entry.CallableId != target.Entry.CallableId || projection.RootCallableId != entry.CallableId ||
            !ResourceTaskCatalog.IsReviewedReference(projection.FrameworkAssemblyIdentity) ||
            !(target.Compilation.TargetFramework == "net8.0" || (target.Compilation.TargetFramework?.StartsWith("net8.0-", StringComparison.Ordinal) ?? false)))
        { return null; }
        var id = storage ? ResourceTaskStorageCatalog.AssumptionId(entry.CallableId, projection.FrameworkAssemblyIdentity, projection.ConfiguredAwait) :
            ResourceTaskCatalog.AssumptionId(entry.CallableId, projection.FrameworkAssemblyIdentity, projection.ConfiguredAwait);
        if (target.Entry.Assumptions.IsDefault || target.Entry.Assumptions.Count(assumption => assumption is not null &&
                assumption.Id == id && assumption.Kind == WorkerAssumptionKind.TrustedBoundary) != 1 ||
            target.Compilation.ResourceMetadataReferences is not { Length: > 0 } references || references.Any(static reference => reference is null))
        { return null; }
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            cancellation.ThrowIfCancellationRequested();
            if (reference.Identity != projection.FrameworkAssemblyIdentity)
            { continue; }
            if (reference.Kind != "Assembly" || reference.Modules is not { Length: 1 } || reference.Modules[0] is not { } module ||
                !WorkerProtocolJson.IsSha256(module.Sha256))
            { return null; }
            hashes.Add(module.Sha256);
        }
        if (hashes.Count != 1)
        { return null; }
        return new()
        {
            AssumptionId = id,
            WitnessIdentifier = storage ? ResourceTaskStorageCatalog.Witness : ResourceTaskCatalog.Witness,
            CatalogSha256 = storage ? projection.ConfiguredAwait ? ResourceTaskStorageCatalog.ConfiguredContentSha256 : ResourceTaskStorageCatalog.ContentSha256 :
                projection.ConfiguredAwait ? ResourceTaskCatalog.ConfiguredContentSha256 : ResourceTaskCatalog.ContentSha256,
            ReferenceAssembly = projection.FrameworkAssemblyIdentity,
            ReferenceSha256 = hashes.Single(),
            RuntimeModel = storage ? projection.ConfiguredAwait ? ResourceTaskStorageCatalog.ConfiguredModel : ResourceTaskStorageCatalog.Model :
                projection.ConfiguredAwait ? ResourceTaskCatalog.ConfiguredModel : ResourceTaskCatalog.Model,
            EvidenceSource = ResourceTaskCatalog.EvidenceSource
        };
    }
}
