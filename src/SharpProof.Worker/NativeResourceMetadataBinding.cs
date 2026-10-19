namespace SharpProof.Worker;

// The catalog supplies reviewed declarations; the sealed owning manifest and
// captured compiler reference images supply their callable/runtime binding.
internal sealed class NativeResourceMetadataBinding
{
    private readonly CompilerCallablePreparation _target;
    private readonly SortedDictionary<string, WorkerResourceMetadataAssumption> _bound = new(StringComparer.Ordinal);

    internal NativeResourceMetadataBinding(CompilerCallablePreparation target)
    { _target = ArgumentNullGuard.NotNull(target, nameof(target)); }

    internal bool Bind(ResourceMetadataReference reference)
    {
        reference = ArgumentNullGuard.NotNull(reference, nameof(reference));
        if (!ResourceMetadataCatalog.IsReviewed(reference) ||
            !(_target.Compilation.TargetFramework == "net8.0" || (_target.Compilation.TargetFramework?.StartsWith("net8.0-", StringComparison.Ordinal) ?? false)))
        { return false; }
        var id = ResourceMetadataCatalog.AssumptionId(_target.Entry.CallableId, reference);
        if (!_target.Entry.Assumptions.Any(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
            assumption.Id == id && assumption.Kind == WorkerAssumptionKind.TrustedBoundary))
        { return false; }
        if (_target.Compilation.ResourceMetadataReferences is null || _target.Compilation.ResourceMetadataReferences.Any(static candidate => candidate is null))
        { return false; }
        var references = _target.Compilation.ResourceMetadataReferences.Where(candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) :
            candidate.Kind == "Assembly" && candidate.Identity == reference.AssemblyIdentity).ToArray();
        if (references.Length == 0 || references.Any(static candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) :
            candidate.Modules is not { Length: 1 } || candidate.Modules[0] is null || !WorkerProtocolJson.IsSha256(candidate.Modules[0].Sha256)))
        { return false; }
        var hashes = references.Select(static candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) : candidate.Modules[0].Sha256)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hashes.Length != 1)
        { return false; }
        _bound[id] = new()
        {
            AssumptionId = id,
            WitnessIdentifier = reference.WitnessIdentifier,
            CatalogSha256 = reference.CatalogSha256,
            ReferenceAssembly = reference.AssemblyIdentity,
            ReferenceSha256 = hashes[0],
            RuntimeModel = ResourceMetadataCatalog.Runtime,
            EvidenceSource = ResourceMetadataCatalog.EvidenceSource
        };
        return true;
    }

    internal void Project(WorkerClaimResult result)
    {
        result = ArgumentNullGuard.NotNull(result, nameof(result));
        if (result.ResourceBound is null || result.Outcome is not (WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted))
        { throw new ArgumentException("Metadata dependencies require a decided owned resource claim.", nameof(result)); }
        result.ResourceBound.MetadataAssumptions = [.. _bound.Values];
        result.Assumptions = CallableClaimResultAssembler.MarkAssumptionsUsed(_target, _bound.Keys.ToHashSet(StringComparer.Ordinal));
    }
}
