namespace SharpProof.Worker.Protocol;

internal static partial class WorkerProtocolJson
{
    public static void Canonicalize(WorkerClaimManifest manifest)
    {
        manifest = RepositoryParameterGuard.NotNull(manifest, nameof(manifest));
        _ = CanonicalizeManifest(manifest);
    }

    private static OrdinalIdentityIndex<WorkerClaimManifestEntry>
        CanonicalizeManifest(WorkerClaimManifest manifest)
    {
        _ = ArgumentNullGuard.NotNull(manifest, nameof(manifest));
        manifest.Claims = [
            .. (manifest.Claims ?? [])
                .OrderBy(
                    static (WorkerClaimManifestEntry /* SPINT001: all values valid - null manifest entries are validated after sorting */ value) => value?.CallableId,
                    StringComparer.Ordinal)
                .ThenBy(static (WorkerClaimManifestEntry /* SPINT001: all values valid - null manifest entries are validated after sorting */ value) =>
                    value?.Ordinal ?? int.MinValue)
                .ThenBy(
                    static (WorkerClaimManifestEntry /* SPINT001: all values valid - null manifest entries are validated after sorting */ value) => value?.ClaimId,
                    StringComparer.Ordinal)
        ];
        var claimsById = CreateClaimIndex(manifest);
        manifest.Callables = SortOrdinal(
            manifest.Callables,
            static (WorkerCallableManifestEntry /* SPINT001: all values valid - null manifest entries are validated after sorting */ value) => value?.CallableId);
        foreach (var callable in manifest.Callables.Where(
                     static value => value != null))
        {
            callable.SelectedFeatures =
                SortManifestEnums(callable.SelectedFeatures);
            callable.SelectionReasons =
                SortManifestEnums(callable.SelectionReasons);
            callable.ClaimIds = [
                .. (callable.ClaimIds ?? [])
                    .OrderBy(id =>
                        id is null ? throw new System.ArgumentNullException(nameof(id)) : (FindClaimOrdinal(claimsById, id)))
                    .ThenBy(
                        static id => id is null ? throw new System.ArgumentNullException(nameof(id)) : (id),
                        StringComparer.Ordinal)
            ];
            callable.Assumptions =
                CanonicalizeAssumptions(callable.Assumptions);
        }

        return claimsById;
    }

    public static string ComputeManifestHash(
        WorkerClaimManifest manifest)
    {
        _ = ArgumentNullGuard.NotNull(manifest, nameof(manifest));
        return ComputeSha256(System.Text.Encoding.UTF8.GetBytes(CreateManifestPayload(manifest)));
    }

    public static void SealManifest(WorkerClaimManifest manifest)
    {
        _ = ArgumentNullGuard.NotNull(manifest, nameof(manifest));
        Canonicalize(manifest);
        manifest.Hash = ComputeManifestHash(manifest);
    }

    public static WorkerProtocolValidationResult ValidateManifest(
        WorkerClaimManifest? manifest)
    {
        var errors = new Validator();
        ValidateManifestCore(
            manifest,
            "manifest",
            errors,
            out _,
            out _,
            out _);
        return errors.Result;
    }

    public static bool ManifestsEqual(
        WorkerClaimManifest? left,
        WorkerClaimManifest? right)
    {
        return left != null &&
            right != null &&
            left.Hash == right.Hash &&
            CreateManifestPayload(left) ==
            CreateManifestPayload(right);
    }
}
