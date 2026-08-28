namespace SharpProof.Worker.Protocol;

public static partial class WorkerProtocolJson
{
    private static void WriteManifestPayload(
        WorkerClaimManifest manifest,
        ManifestWriter writer)
    {
        writer.Add(WorkerManifestIdentityCatalog.Domain)
            .Add(WorkerManifestVersions.Current);
        writer.Add("manifest.schemaVersion").Add(manifest.SchemaVersion);
        writer.Add("manifest.callables").Add(manifest.Callables?.Length ?? -1);
        foreach (var entry in (manifest.Callables ?? [])
            .OrderBy(static value => value?.CallableId, StringComparer.Ordinal))
        {
            writer.Add("callable");
            writer.Add("callable.id").Add(entry?.CallableId);
            writer.AddItems("callable.selectedFeatures", entry?.SelectedFeatures,
                SortManifestEnums(entry?.SelectedFeatures),
                static (target, value) => target.Add(ManifestName(value)));
            writer.AddItems("callable.selectionReasons", entry?.SelectionReasons,
                SortManifestEnums(entry?.SelectionReasons),
                static (target, value) => target.Add(ManifestName(value)));
            writer.AddLocation("callable.location", entry?.Location);
            writer.AddItems("callable.claimIds", entry?.ClaimIds,
                (entry?.ClaimIds ?? []).OrderBy(static value => value, StringComparer.Ordinal),
                static (target, value) => target.Add(value));
            writer.AddItems("callable.assumptions", entry?.Assumptions,
                CanonicalizeAssumptions(entry?.Assumptions),
                static (target, value) => target.Add(value.Id).Add(ManifestName(value.Kind)));
        }
        writer.Add("manifest.claims").Add(manifest.Claims?.Length ?? -1);
        foreach (var entry in (manifest.Claims ?? [])
            .OrderBy(static value => value?.CallableId, StringComparer.Ordinal)
            .ThenBy(static value => value?.Ordinal ?? int.MinValue)
            .ThenBy(static value => value?.ClaimId, StringComparer.Ordinal))
        {
            writer.Add("claim");
            writer.Add("claim.id").Add(entry?.ClaimId);
            writer.Add("claim.callableId").Add(entry?.CallableId);
            writer.Add("claim.ordinal").Add(entry?.Ordinal ?? -1);
            writer.Add("claim.kind").Add(ManifestName(entry?.Kind ?? WorkerClaimKind.Unspecified));
            writer.Add("claim.evidence").Add(ManifestName(entry?.Evidence ?? WorkerClaimEvidence.Unspecified));
            writer.Add("claim.effectContractKind").Add(ManifestName(entry?.EffectContractKind ?? WorkerEffectContractKind.Unspecified));
            writer.AddLocation("claim.location", entry?.Location);
        }
    }
}
