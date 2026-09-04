using System.Text;

namespace SharpProof.Worker.Protocol;

public static partial class WorkerProtocolJson
{
    public static void Canonicalize(WorkerClaimManifest manifest)
    {
        _ = manifest ?? throw new ArgumentNullException(nameof(manifest));
        manifest.Claims = [
            .. (manifest.Claims ?? [])
                .OrderBy(
                    static value => value?.CallableId,
                    StringComparer.Ordinal)
                .ThenBy(static value =>
                    value?.Ordinal ?? int.MinValue)
                .ThenBy(
                    static value => value?.ClaimId,
                    StringComparer.Ordinal)
        ];
        var claimsById = CreateClaimIndex(manifest);
        manifest.Callables = SortOrdinal(
            manifest.Callables,
            static value => value?.CallableId);
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
                        FindClaimOrdinal(claimsById, id))
                    .ThenBy(
                        static id => id,
                        StringComparer.Ordinal)
            ];
            callable.Assumptions =
                CanonicalizeAssumptions(callable.Assumptions);
        }
    }

    public static string ComputeManifestHash(
        WorkerClaimManifest manifest)
    {
        return ComputeSha256(Encoding.UTF8.GetBytes(
            CreateManifestPayload(
                manifest ??
                throw new ArgumentNullException(nameof(manifest)))));
    }

    public static void SealManifest(WorkerClaimManifest manifest)
    {
        Canonicalize(manifest);
        manifest.Hash = ComputeManifestHash(manifest);
    }

    public static WorkerProtocolValidationResult ValidateManifest(
        WorkerClaimManifest? manifest)
    {
        var errors = new Validator();
        ValidateManifestCore(manifest, "manifest", errors, out _);
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
