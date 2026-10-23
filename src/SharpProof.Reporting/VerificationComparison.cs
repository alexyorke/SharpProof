using SharpProof.Worker.Protocol;

namespace SharpProof.Reporting;

internal sealed class VerificationChange
{
    internal VerificationChange(string kind, string identity, bool regression)
    {
        global::SharpProof.Internal.InputValidation.Require(kind, nameof(kind));
        global::SharpProof.Internal.InputValidation.Require(identity, nameof(identity));
        Kind = kind;
        Identity = identity;
        IsRegression = regression;
    }

    internal string Kind { get; }
    internal string Identity { get; }
    internal bool IsRegression { get; }
}

internal static class VerificationComparison
{
    internal static IReadOnlyList<VerificationChange> Compare(WorkerVerifyResponse baseline, WorkerVerifyResponse current)
    {
        if (baseline == null)
        {
            throw new ArgumentNullException(nameof(baseline));
        }

        if (current == null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        if (baseline.ProtocolVersion != current.ProtocolVersion || baseline.Summary.Versions.ApiSpecContentSha256 != current.Summary.Versions.ApiSpecContentSha256)
        {
            throw new InvalidDataException("Baseline and current evidence use incompatible protocol or specification identities.");
        }

        var changes = new List<VerificationChange>();
        var oldClaims = baseline.Manifest.Claims.ToDictionary(static claim =>
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            return claim.ClaimId;
        }, StringComparer.Ordinal);
        foreach (var claim in current.Manifest.Claims)
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            if (oldClaims.TryGetValue(claim.ClaimId, out var oldClaim) &&
                (oldClaim.CallableId != claim.CallableId || oldClaim.Kind != claim.Kind ||
                    oldClaim.EffectContractKind != claim.EffectContractKind))
            {
                throw new InvalidDataException("The same claim identifier refers to different callable or contract identities.");
            }
        }
        var previous = baseline.ClaimResults.ToDictionary(static claim =>
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            return claim.ClaimId;
        }, StringComparer.Ordinal);
        var present = current.ClaimResults.ToDictionary(static claim =>
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            return claim.ClaimId;
        }, StringComparer.Ordinal);
        foreach (var old in previous.Values)
        {
            if (!present.TryGetValue(old.ClaimId, out var next))
            {
                changes.Add(new("Removed claim/coverage", old.ClaimId, true));
                continue;
            }

            if (old.Outcome != next.Outcome)
            {
                changes.Add(new(old.Outcome + " -> " + next.Outcome, old.ClaimId, next.Outcome == WorkerClaimOutcome.Refuted || old.Outcome == WorkerClaimOutcome.Proven));
            }

            if (old.Vacuity == WorkerVacuityKind.None && next.Vacuity != WorkerVacuityKind.None)
            {
                changes.Add(new("New vacuity: " + next.Vacuity, old.ClaimId, true));
            }
        }

        foreach (var claim in present.Values.Where(claim =>
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            return !previous.ContainsKey(claim.ClaimId);
        }))
        {
            changes.Add(new("Added " + claim.Outcome + " claim", claim.ClaimId, claim.Outcome != WorkerClaimOutcome.Proven || claim.Vacuity != WorkerVacuityKind.None));
        }

        var oldCallables = baseline.CallableResults.ToDictionary(static callable =>
        {
            global::SharpProof.Internal.InputValidation.Require(callable, nameof(callable));
            return callable.CallableId;
        }, StringComparer.Ordinal);
        var newCallables = current.CallableResults.ToDictionary(static callable =>
        {
            global::SharpProof.Internal.InputValidation.Require(callable, nameof(callable));
            return callable.CallableId;
        }, StringComparer.Ordinal);
        foreach (var old in oldCallables.Values.Where(old =>
        {
            global::SharpProof.Internal.InputValidation.Require(old, nameof(old));
            return !newCallables.ContainsKey(old.CallableId);
        }))
        {
            changes.Add(new("Removed selected callable", old.CallableId, true));
        }

        var oldSelections = baseline.Manifest.Callables.ToDictionary(static callable =>
        {
            global::SharpProof.Internal.InputValidation.Require(callable, nameof(callable));
            return callable.CallableId;
        }, StringComparer.Ordinal);
        foreach (var callable in current.Manifest.Callables)
        {
            global::SharpProof.Internal.InputValidation.Require(callable, nameof(callable));
            if (oldSelections.TryGetValue(callable.CallableId, out var oldSelection))
            {
                foreach (var feature in oldSelection.SelectedFeatures.Except(callable.SelectedFeatures))
                {
                    changes.Add(new("Removed selected feature: " + feature, callable.CallableId, true));
                }
            }
        }

        foreach (var callable in newCallables.Values)
        {
            oldCallables.TryGetValue(callable.CallableId, out var old);
            if (callable.Coverage == WorkerCallableCoverage.Incomplete && old?.Coverage != WorkerCallableCoverage.Incomplete)
            {
                changes.Add(new("New incomplete coverage: " + callable.Reason, callable.CallableId, true));
            }

            var oldAssumptions = new HashSet<(WorkerAssumptionKind Kind, string Id)>(old?.Assumptions.Select(static value =>
            {
                global::SharpProof.Internal.InputValidation.Require(value, nameof(value));
                return (value.Kind, value.Id);
            }) ?? []);
            foreach (var assumption in callable.Assumptions.Where(value =>
            {
                global::SharpProof.Internal.InputValidation.Require(value, nameof(value));
                return !oldAssumptions.Contains((value.Kind, value.Id));
            }))
            {
                if (assumption.Kind is WorkerAssumptionKind.Precondition or WorkerAssumptionKind.UserAssume or WorkerAssumptionKind.TrustedBoundary or WorkerAssumptionKind.ApiSpecification)
                {
                    changes.Add(new("Added/changed assumption: " + assumption.Kind, callable.CallableId + " " + assumption.Id, old != null));
                }
            }
        }

        if (baseline.RunStatus == WorkerRunStatus.Complete && current.RunStatus != WorkerRunStatus.Complete)
        {
            changes.Add(new("Run became " + current.RunStatus, current.FailureReason.ToString(), true));
        }

        return changes.OrderBy(static change =>
        {
            global::SharpProof.Internal.InputValidation.Require(change, nameof(change));
            return change.Identity;
        }, StringComparer.Ordinal).ThenBy(static change =>
        {
            global::SharpProof.Internal.InputValidation.Require(change, nameof(change));
            return change.Kind;
        }, StringComparer.Ordinal).ToArray();
    }
}
