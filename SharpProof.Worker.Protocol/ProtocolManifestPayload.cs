namespace SharpProof.Worker.Protocol;

public static partial class WorkerProtocolJson
{
    private static string CreateManifestPayload(WorkerClaimManifest manifest)
    {
        // The identity excludes its own hash and runtime assumption usage. Use the
        // same source-generated JSON contract as the protocol, with canonical arrays.
        var payload = new WorkerClaimManifest
        {
            SchemaVersion = manifest.SchemaVersion,
            Hash = string.Empty,
            Callables = [.. (manifest.Callables ?? []).Select(static entry =>
                entry == null ? null! : new WorkerCallableManifestEntry
                {
                    CallableId = entry.CallableId,
                    SelectedFeatures = entry.SelectedFeatures,
                    SelectionReasons = entry.SelectionReasons,
                    Location = entry.Location,
                    ClaimIds = entry.ClaimIds,
                    Assumptions = [.. (entry.Assumptions ?? []).Select(static assumption =>
                        assumption == null ? null! : new WorkerAssumptionEvidence
                        {
                            Id = assumption.Id,
                            Kind = assumption.Kind
                        })]
                })],
            Claims = manifest.Claims
        };
        Canonicalize(payload);
        return SerializeBounded(payload);
    }
}
