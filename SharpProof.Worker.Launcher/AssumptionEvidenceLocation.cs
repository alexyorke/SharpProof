using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static class AssumptionEvidenceLocation
{
    internal static WorkerSourceLocation Find(WorkerVerifyResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var callable = response.Manifest.Callables.FirstOrDefault(
            static candidate => HasPolicyAssumption(candidate.Assumptions));
        if (callable != null)
        {
            return callable.Location;
        }

        var claimIds = response.ClaimResults
            .Where(static result => HasPolicyAssumption(result.Assumptions))
            .Select(static result => result.ClaimId)
            .ToHashSet(StringComparer.Ordinal);
        var claim = response.Manifest.Claims.FirstOrDefault(
            candidate => claimIds.Contains(candidate.ClaimId));
        if (claim != null)
        {
            return response.Manifest.Callables.FirstOrDefault(
                candidate => candidate.CallableId == claim.CallableId)?.Location ?? new();
        }

        return response.Manifest.Callables.FirstOrDefault()?.Location ?? new();
    }

    private static bool HasPolicyAssumption(WorkerAssumptionEvidence[]? assumptions)
    {
        return (assumptions ?? []).Any(static assumption =>
            assumption?.Kind is WorkerAssumptionKind.UserAssume or
                WorkerAssumptionKind.TrustedBoundary);
    }
}
