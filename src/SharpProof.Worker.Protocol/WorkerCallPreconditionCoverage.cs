using System.Collections.Immutable;

namespace SharpProof.Worker.Protocol;

// Call-precondition claims are generated, never written by the user. One that
// runs out of time does not fail require-proven, because machine load alone
// must not decide a strict build. It still reports, and its call counts as
// unchecked.
internal static class WorkerCallPreconditionCoverage
{
    internal static bool IsTimingOnly(WorkerClaimManifestEntry claim, WorkerClaimResult result)
    {
        claim = RepositoryParameterGuard.NotNull(claim, nameof(claim));
        result = RepositoryParameterGuard.NotNull(result, nameof(result));
        return claim.Kind == WorkerClaimKind.CallPrecondition && result.Outcome == WorkerClaimOutcome.Unknown &&
            result.Reason is WorkerClaimReason.MethodTimeout or WorkerClaimReason.ProjectTimeout;
    }

    // The incomplete callables whose only gaps are timed-out call-precondition claims.
    internal static ImmutableHashSet<string> TimingOnlyCallables(WorkerVerifyResponse response)
    {
        response = RepositoryParameterGuard.NotNull(response, nameof(response));
        var claims = Claims(response);
        var timedOut = new HashSet<string>(StringComparer.Ordinal);
        var blocking = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in response.ClaimResults)
        {
            if (result is null || result.Outcome != WorkerClaimOutcome.Unknown || !claims.TryGetValue(result.ClaimId, out var claim))
            { continue; }
            (IsTimingOnly(claim, result) ? timedOut : blocking).Add(claim.CallableId);
        }
        return response.CallableResults
            .Where(callable => callable.Coverage == WorkerCallableCoverage.Incomplete &&
                callable.Reason is WorkerCallableCoverageReason.MethodTimeout or WorkerCallableCoverageReason.ProjectTimeout &&
                timedOut.Contains(callable.CallableId) && !blocking.Contains(callable.CallableId))
            .Select(static callable => callable.CallableId)
            .ToImmutableHashSet(StringComparer.Ordinal);
    }

    // The manifest counts the calls the compiler could not select. A call whose
    // claim ran out of time is not checked either.
    internal static int UncheckedCallSites(WorkerVerifyResponse response)
    {
        response = RepositoryParameterGuard.NotNull(response, nameof(response));
        var claims = Claims(response);
        return response.Manifest.UncheckedCallSites + response.ClaimResults
            .Where(result => claims.TryGetValue(result.ClaimId, out var claim) && IsTimingOnly(claim, result))
            .Select(result => claims[result.ClaimId].Location)
            .Distinct()
            .Count();
    }

    private static Dictionary<string, WorkerClaimManifestEntry> Claims(WorkerVerifyResponse response)
    {
        var claims = new Dictionary<string, WorkerClaimManifestEntry>(StringComparer.Ordinal);
        foreach (var claim in response.Manifest.Claims)
        {
            if (claim is not null && !claims.ContainsKey(claim.ClaimId))
            { claims.Add(claim.ClaimId, claim); }
        }
        return claims;
    }
}
