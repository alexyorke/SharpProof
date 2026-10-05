using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

// A compiler effect claim declares a contract and its constraint; Z3 decides it
// in the worker. Only a trusted complete boundary carries an outcome.
internal static class CompilerEffectClaimArtifactCodec
{
    internal static void Seal(CompilerEffectClaimArtifact value)
    { value.EvidenceSha256 = ComputeSha256(value); }

    internal static void Validate(CompilerEffectClaimArtifact value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ClaimId) ||
            string.IsNullOrWhiteSpace(value.Evidence) ||
            !WorkerProtocolJson.IsDefined(value.ContractKind, WorkerEffectContractKind.Unspecified) ||
            !Enum.IsDefined(typeof(WorkerClaimReason), value.Reason) ||
            !WorkerProtocolJson.IsDefined(value.Certainty, WorkerEffectEvidenceCertainty.Unspecified) ||
            !HasValidConstraint(value.ContractKind, value.Constraint) ||
            !HasValidOutcome(value) || !WorkerProtocolJson.IsSha256(value.EvidenceSha256))
        {
            throw new InvalidDataException("Compiler effect-claim evidence is invalid.");
        }
    }

    private static bool HasValidOutcome(CompilerEffectClaimArtifact value)
    {
        return CompilerEffectEvidenceCatalog.HasValidEffectTuple(value.Outcome, value.Reason, value.Certainty) &&
            (value.Outcome, value.Reason) switch
            {
                (WorkerClaimOutcome.Proven, WorkerClaimReason.None) => true,
                (WorkerClaimOutcome.Unknown, var reason) => CompilerEffectEvidenceCatalog.UnknownReasons.Contains(reason),
                _ => false
            };
    }

    private static bool HasValidConstraint(
        WorkerEffectContractKind kind,
        CompilerEffectConstraintArtifact? constraint)
    {
        if (constraint is not { } value ||
            !WorkerProtocolJson.HasKnownEffects(value.AllowedEffects, value.AllowedCapabilities) ||
            !HasCanonicalStrings(value.AllowedExceptionTypes))
        {
            return false;
        }

        var rule = CompilerEffectEvidenceCatalog.ConstraintRules
            .FirstOrDefault(candidate => candidate.Kind == kind);
        return rule.Kind == kind &&
            (!rule.EffectsMustBeEmpty || value.AllowedEffects == WorkerEffectSet.None) &&
            (!rule.CapabilitiesMustBeEmpty || value.AllowedCapabilities == WorkerEffectCapabilitySet.None) &&
            (!rule.ExceptionsMustBeEmpty || value.AllowedExceptionTypes.Length == 0);
    }

    private static bool HasCanonicalStrings(string[]? values)
    {
        if (values == null)
        { return false; }
        string? previous = null;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                previous != null && StringComparer.Ordinal.Compare(previous, value) >= 0)
            { return false; }
            previous = value;
        }
        return true;
    }

    private static string ComputeSha256(CompilerEffectClaimArtifact value)
    {
        var constraint = value.Constraint;
        using var hash = new CanonicalHashWriter();
        hash.Add(CompilerEffectEvidenceCatalog.EvidenceDomain)
            .Add(CompilerEffectEvidenceCatalog.EvidenceVersion)
            .Add(value.ClaimId)
            .Add(value.ContractKind)
            .Add(value.Outcome)
            .Add(value.Reason)
            .Add(value.Certainty)
            .Add(constraint.AllowedEffects)
            .Add(constraint.AllowedCapabilities);
        foreach (var type in constraint.AllowedExceptionTypes.OrderBy(static item => item, StringComparer.Ordinal))
        { hash.Add(type); }
        return hash.Add(value.Evidence).Finish();
    }
}
