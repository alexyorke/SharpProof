using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerEffectClaimArtifactCodec
{
    internal static void Seal(CompilerEffectClaimArtifact value)
    {
        if (value.Replay is { } replay)
        {
            replay.ConstraintSha256 = ComputeConstraintSha256(value.ContractKind, value.Constraint);
            using var hash = StartEvidenceHash(value);
            foreach (var effectEvent in replay.Events ?? [])
            {
                effectEvent.OperationIdentitySha256 = ComputeReplayOperationSha256(effectEvent);
                AddReplayEvent(hash, effectEvent, includeOrdinal: true, includeOperationIdentity: true);
            }
            value.EvidenceSha256 = FinishEvidenceHash(hash, value);
            return;
        }
        value.EvidenceSha256 = ComputeSha256(value);
    }

    internal static void Validate(CompilerEffectClaimArtifact value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ClaimId) ||
            string.IsNullOrWhiteSpace(value.Evidence) ||
            !WorkerProtocolJson.IsDefined(value.ContractKind, WorkerEffectContractKind.Unspecified) ||
            !Enum.IsDefined(typeof(WorkerClaimReason), value.Reason) ||
            !WorkerProtocolJson.IsDefined(value.Certainty, WorkerEffectEvidenceCertainty.Unspecified) ||
            !HasValidConstraint(value.ContractKind, value.Constraint))
        {
            throw new InvalidDataException("Compiler effect-claim evidence is invalid.");
        }

        if (!HasValidOutcome(value) || !WorkerProtocolJson.IsSha256(value.EvidenceSha256) ||
            !HasValidReplay(value))
        {
            throw new InvalidDataException("Compiler effect-claim evidence is invalid.");
        }
    }
    private static bool HasValidOutcome(CompilerEffectClaimArtifact value)
    {
        return CompilerEffectEvidenceCatalog.HasValidEffectTuple(
            value.Outcome, value.Reason, value.Certainty) &&
        (value.Outcome, value.Reason, value.Certainty, value.Witness, value.Replay) switch
        {
            (WorkerClaimOutcome.Proven, WorkerClaimReason.None, _, null, null) => true,
            (WorkerClaimOutcome.Refuted, WorkerClaimReason.None,
                _, { } witness, { }) => WorkerProtocolJson.HasValidEffectWitness(witness) &&
                    HasCanonicalStrings(witness.ExactExceptionTypeHierarchy) &&
                    WorkerProtocolJson.HasValidLocation(witness.Location),
            (WorkerClaimOutcome.Unknown,
                var reason, _, null, null) when
                CompilerEffectEvidenceCatalog.UnknownReasons.Contains(reason) => true,
            _ => false
        };
    }

    private static bool HasValidConstraint(
        WorkerEffectContractKind kind,
        CompilerEffectConstraintArtifact? constraint)
    {
        if (constraint is not { } value ||
            !WorkerProtocolJson.HasKnownEffects(
                value.AllowedEffects, value.AllowedCapabilities) ||
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

    private static bool HasValidReplay(CompilerEffectClaimArtifact value)
    {
        if (value.Replay == null)
        {
            return value.Outcome != WorkerClaimOutcome.Refuted;
        }
        var replay = value.Replay;
        if (value.Outcome != WorkerClaimOutcome.Refuted ||
            replay.PathKind != CompilerEffectEvidenceCatalog.ReplayPathKind ||
            replay.Events is not { Length: > 0 and <= CompilerEffectEvidenceCatalog.MaximumReplayEvents })
        {
            return false;
        }
        for (var ordinal = 0; ordinal < replay.Events.Length; ordinal++)
        {
            if (!HasValidReplayEvent(replay.Events[ordinal], ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasValidReplayEvent(CompilerEffectReplayEventArtifact? value, int ordinal)
    {
        if (value == null || value.Ordinal != ordinal ||
            !CompilerEffectEvidenceCatalog.SupportedReplayEventKinds.Contains(value.Kind) ||
            string.IsNullOrWhiteSpace(value.TypeIdentity) ||
            !HasOptionalText(value.MemberDocumentationId) ||
            !HasOptionalText(value.TypeDocumentationId) ||
            value.ScalarOperands is not { Length: 0 } ||
            !HasCanonicalStrings(value.ExactExceptionTypeHierarchy) ||
            !WorkerProtocolJson.HasValidLocation(value.Location))
        {
            return false;
        }
        return value.Kind switch
        {
            CompilerEffectReplayEventKind.ManagedObjectAllocation or CompilerEffectReplayEventKind.MonitorCall =>
                !string.IsNullOrWhiteSpace(value.MemberIdentity) && value.ExactExceptionTypeHierarchy.Length == 0,
            CompilerEffectReplayEventKind.ManagedArrayAllocation or CompilerEffectReplayEventKind.EmptyLock =>
                string.IsNullOrEmpty(value.MemberIdentity) && value.MemberDocumentationId == null &&
                value.ExactExceptionTypeHierarchy.Length == 0,
            CompilerEffectReplayEventKind.ExplicitThrow =>
                !string.IsNullOrWhiteSpace(value.MemberIdentity) &&
                value.ExactExceptionTypeHierarchy.Contains(value.TypeIdentity, StringComparer.Ordinal),
            _ => false
        };
    }
    private static bool HasOptionalText(string? value)
    {
        return value == null || !string.IsNullOrWhiteSpace(value);
    }

    private static bool HasCanonicalStrings(string[]? values)
    {
        if (values == null)
        {
            return false;
        }

        string? previous = null;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                previous != null &&
                StringComparer.Ordinal.Compare(previous, value) >= 0)
            {
                return false;
            }

            previous = value;
        }

        return true;
    }

    internal static string ComputeConstraintSha256(
        WorkerEffectContractKind kind,
        CompilerEffectConstraintArtifact constraint)
    {
        constraint = ArgumentNullGuard.NotNull(constraint, nameof(constraint));

        using var hash = new CanonicalHashWriter();
        hash.Add(CompilerEffectEvidenceCatalog.ConstraintDomain)
            .Add(CompilerEffectEvidenceCatalog.ConstraintVersion)
            .Add(kind)
            .Add(constraint.AllowedEffects)
            .Add(constraint.AllowedCapabilities);
        AddSortedStrings(hash, constraint.AllowedExceptionTypes ?? []);

        return hash.Finish();
    }

    internal static string ComputeReplayOperationSha256(
        CompilerEffectReplayEventArtifact value)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));

        using var hash = new CanonicalHashWriter();
        hash.Add(CompilerEffectEvidenceCatalog.OperationDomain)
            .Add(CompilerEffectEvidenceCatalog.OperationVersion);
        AddReplayEvent(hash, value, includeOrdinal: false, includeOperationIdentity: false);
        return hash.Finish();
    }

    private static CanonicalHashWriter StartEvidenceHash(
        CompilerEffectClaimArtifact value)
    {
        var witness = value.Witness;
        var constraint = value.Constraint;
        var hash = new CanonicalHashWriter();
        hash.Add(CompilerEffectEvidenceCatalog.EvidenceDomain)
            .Add(CompilerEffectEvidenceCatalog.EvidenceVersion)
            .Add(value.ClaimId)
            .Add(value.ContractKind)
            .Add(value.Outcome)
            .Add(value.Reason)
            .Add(value.Certainty)
            .Add(constraint.AllowedEffects)
            .Add(constraint.AllowedCapabilities);
        AddSortedStrings(hash, constraint.AllowedExceptionTypes);

        hash.Add(witness?.Kind)
            .Add(witness?.Detail)
            .Add(witness?.Effects ?? WorkerEffectSet.None)
            .Add(witness?.Capabilities ?? WorkerEffectCapabilitySet.None);
        AddSortedStrings(hash, witness?.ExactExceptionTypeHierarchy ?? []);

        var replay = value.Replay;
        hash.Add(replay != null)
            .Add(replay?.PathKind ?? CompilerEffectReplayPathKind.Unspecified)
            .Add(replay?.ConstraintSha256)
            .Add(replay?.Events?.Length ?? -1);
        return hash;
    }

    private static string FinishEvidenceHash(
        CanonicalHashWriter hash,
        CompilerEffectClaimArtifact value)
    {
        var witness = value.Witness;
        return hash.Add(witness?.Location.Path)
            .Add(witness?.Location.Start ?? -1)
            .Add(witness?.Location.Length ?? -1)
            .Add(witness?.Location.Line ?? -1)
            .Add(witness?.Location.Column ?? -1)
            .Add(value.Evidence)
            .Finish();
    }

    private static string ComputeSha256(CompilerEffectClaimArtifact value)
    {
        using var hash = StartEvidenceHash(value);
        foreach (var effectEvent in value.Replay?.Events ?? [])
        {
            AddReplayEvent(
                hash,
                effectEvent,
                includeOrdinal: true,
                includeOperationIdentity: true);
        }
        return FinishEvidenceHash(hash, value);
    }

    private static void AddSortedStrings(
        CanonicalHashWriter hash,
        string[] values)
    {
        foreach (var value in values.OrderBy(static item => item, StringComparer.Ordinal))
        {
            hash.Add(value);
        }
    }

    private static void AddReplayEvent(
        CanonicalHashWriter hash,
        CompilerEffectReplayEventArtifact value,
        bool includeOrdinal,
        bool includeOperationIdentity)
    {
        if (includeOrdinal)
        {
            hash.Add(value.Ordinal);
        }

        hash.Add(value.Kind)
            .Add(value.SyntaxTreeOrdinal)
            .Add(value.SyntaxTreeSha256)
            .Add(value.SyntaxTreeSnapshotSha256)
            .Add(value.SyntaxTreeLineMapSha256)
            .Add(value.SyntaxStart)
            .Add(value.SyntaxLength);
        if (includeOperationIdentity)
        {
            hash.Add(value.OperationIdentitySha256);
        }

        // Array-allocation events canonically have no member identity. Treat
        // the wire-level null and empty representations as the same value so
        // replay semantics and operation hashes cannot diverge.
        hash.Add(value.MemberIdentity ?? string.Empty)
            .Add(value.MemberDocumentationId)
            .Add(value.TypeIdentity)
            .Add(value.TypeDocumentationId)
            .Add(value.SpecWitnessIdentifier);
        hash.Add(value.SourceTreeOrdinal)
            .Add(value.SourceTreePath)
            .Add(value.SourceTreeSha256)
            .Add(value.SourceLineMapSha256);
        var operands = value.ScalarOperands ?? [];
        hash.Add(operands.Length);
        foreach (var operand in operands)
        {
            hash.Add(operand);
        }

        var exceptionTypes = value.ExactExceptionTypeHierarchy ?? [];
        hash.Add(exceptionTypes.Length);
        foreach (var type in exceptionTypes)
        {
            hash.Add(type);
        }

        var location = value.Location;
        hash.Add(location?.Path)
            .Add(location?.Start ?? -1)
            .Add(location?.Length ?? -1)
            .Add(location?.Line ?? -1)
            .Add(location?.Column ?? -1);
    }

}
