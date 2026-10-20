namespace SharpProof.Worker.Protocol;

internal static partial class WorkerProtocolJson
{
    private static string CreateManifestPayload(WorkerClaimManifest manifest)
    {
        // The identity excludes its own hash and runtime assumption usage. Use the
        // same source-generated JSON contract as the protocol, with canonical arrays.
        manifest = RepositoryParameterGuard.NotNull(manifest, nameof(manifest));
        var payload = new WorkerClaimManifest
        {
            SchemaVersion = manifest.SchemaVersion,
            Hash = string.Empty,
            Callables = [.. (manifest.Callables ?? []).Select(static entry =>
                entry is null ? throw new System.ArgumentNullException(nameof(entry)) : (entry == null ? null! : new WorkerCallableManifestEntry
                {
                    CallableId = entry.CallableId,
                    SelectedFeatures = entry.SelectedFeatures,
                    SelectionReasons = entry.SelectionReasons,
                    Location = entry.Location,
                    ClaimIds = entry.ClaimIds,
                    Assumptions = [.. (entry.Assumptions ?? []).Select(static assumption =>
                        assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption == null ? null! : new WorkerAssumptionEvidence
                        {
                            Id = assumption.Id,
                            Kind = assumption.Kind
                        }))]
                }))],
            Claims = manifest.Claims
        };
        Canonicalize(payload);
        return SerializeBounded(payload);
    }
}
