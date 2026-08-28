using System.Globalization;
using System.Security.Cryptography;

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
        var claimIndex = CreateClaimIndex(manifest);
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
                        claimIndex.FindOrdinal(id))
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
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var writer = new ManifestWriter(hash))
        {
            WriteManifestPayload(manifest, writer);
        }

        return string.Concat(hash.GetHashAndReset()
            .Select(static value => value.ToString(
                "x2",
                CultureInfo.InvariantCulture)));
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
        ValidateManifestCore(manifest, "manifest", errors);
        return errors.Result;
    }

    public static bool ManifestsEqual(
        WorkerClaimManifest? left,
        WorkerClaimManifest? right)
    {
        return left != null &&
            right != null &&
            left.Hash == right.Hash &&
            ManifestFieldsEqual(left, right);
    }

    private static bool ManifestFieldsEqual(
        WorkerClaimManifest left,
        WorkerClaimManifest right)
    {
        return left.SchemaVersion == right.SchemaVersion &&
            CallableArraysEqual(left.Callables, right.Callables) &&
            ClaimArraysEqual(left.Claims, right.Claims);
    }

    private static bool CallableArraysEqual(
        WorkerCallableManifestEntry[]? left,
        WorkerCallableManifestEntry[]? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        var orderedLeft = left.OrderBy(
                static value => value?.CallableId,
                StringComparer.Ordinal)
            .ToArray();
        var orderedRight = right.OrderBy(
                static value => value?.CallableId,
                StringComparer.Ordinal)
            .ToArray();
        if (orderedLeft.Length != orderedRight.Length)
        {
            return false;
        }

        for (var index = 0; index < orderedLeft.Length; index++)
        {
            var a = orderedLeft[index];
            var b = orderedRight[index];
            if (a == null || b == null)
            {
                if (a != null || b != null)
                {
                    return false;
                }

                continue;
            }

            if (a.CallableId != b.CallableId ||
                !ManifestEnumArraysEqual(
                    a.SelectedFeatures,
                    b.SelectedFeatures) ||
                !ManifestEnumArraysEqual(
                    a.SelectionReasons,
                    b.SelectionReasons) ||
                !LocationEqual(a.Location, b.Location) ||
                !StringArraysEqual(a.ClaimIds, b.ClaimIds) ||
                !AssumptionArraysEqual(a.Assumptions, b.Assumptions))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ClaimArraysEqual(
        WorkerClaimManifestEntry[]? left,
        WorkerClaimManifestEntry[]? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        var orderedLeft = left.OrderBy(
                static value => value?.CallableId,
                StringComparer.Ordinal)
            .ThenBy(static value => value?.Ordinal ?? int.MinValue)
            .ThenBy(static value => value?.ClaimId, StringComparer.Ordinal)
            .ToArray();
        var orderedRight = right.OrderBy(
                static value => value?.CallableId,
                StringComparer.Ordinal)
            .ThenBy(static value => value?.Ordinal ?? int.MinValue)
            .ThenBy(static value => value?.ClaimId, StringComparer.Ordinal)
            .ToArray();
        if (orderedLeft.Length != orderedRight.Length)
        {
            return false;
        }

        for (var index = 0; index < orderedLeft.Length; index++)
        {
            var a = orderedLeft[index];
            var b = orderedRight[index];
            if (a == null || b == null)
            {
                if (a != null || b != null)
                {
                    return false;
                }

                continue;
            }

            if (a.ClaimId != b.ClaimId ||
                a.CallableId != b.CallableId ||
                a.Ordinal != b.Ordinal ||
                ManifestName(a.Kind) != ManifestName(b.Kind) ||
                ManifestName(a.Evidence) != ManifestName(b.Evidence) ||
                ManifestName(a.EffectContractKind) !=
                    ManifestName(b.EffectContractKind) ||
                !LocationEqual(a.Location, b.Location))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ManifestEnumArraysEqual<T>(
        T[]? left,
        T[]? right)
        where T : struct, Enum
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        var orderedLeft = SortManifestEnums(left);
        var orderedRight = SortManifestEnums(right);
        if (orderedLeft.Length != orderedRight.Length)
        {
            return false;
        }

        for (var index = 0; index < orderedLeft.Length; index++)
        {
            if (ManifestName(orderedLeft[index]) !=
                ManifestName(orderedRight[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StringArraysEqual(
        string[]? left,
        string[]? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        var orderedLeft = left.OrderBy(static value => value, s_ordinal).ToArray();
        var orderedRight = right.OrderBy(static value => value, s_ordinal).ToArray();
        return orderedLeft.SequenceEqual(orderedRight, s_ordinal);
    }

    private static bool AssumptionArraysEqual(
        WorkerAssumptionEvidence[]? left,
        WorkerAssumptionEvidence[]? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        var orderedLeft = CanonicalizeAssumptions(left);
        var orderedRight = CanonicalizeAssumptions(right);
        if (orderedLeft.Length != orderedRight.Length)
        {
            return false;
        }

        for (var index = 0; index < orderedLeft.Length; index++)
        {
            var a = orderedLeft[index];
            var b = orderedRight[index];
            if (a == null || b == null)
            {
                if (a != null || b != null)
                {
                    return false;
                }

                continue;
            }

            if (a.Id != b.Id || ManifestName(a.Kind) != ManifestName(b.Kind))
            {
                return false;
            }
        }

        return true;
    }

    private static bool LocationEqual(
        WorkerSourceLocation? left,
        WorkerSourceLocation? right)
    {
        return left == null || right == null
            ? left == null && right == null
            : left.Path == right.Path &&
                left.Start == right.Start &&
                left.Length == right.Length &&
                left.Line == right.Line &&
                left.Column == right.Column;
    }
}
