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
            foreach (var effectEvent in replay.Events ?? [])
            {
                effectEvent.OperationIdentitySha256 = ComputeReplayOperationSha256(effectEvent);
            }
        }
        value.EvidenceSha256 = ComputeSha256(value);
    }

    internal static void Validate(CompilerEffectClaimArtifact value)
    {
        Validate(value, null);
    }

    internal static void Validate(
        CompilerEffectClaimArtifact value,
        CompilerCompilationSnapshot? compilation)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ClaimId) ||
            string.IsNullOrWhiteSpace(value.Evidence) ||
            !WorkerProtocolJson.IsDefined(value.ContractKind, WorkerEffectContractKind.Unspecified) ||
            !Enum.IsDefined(typeof(WorkerClaimReason), value.Reason) ||
            !WorkerProtocolJson.IsDefined(value.Certainty, WorkerEffectEvidenceCertainty.Unspecified) ||
            !HasValidConstraint(value.ContractKind, value.Constraint) ||
            !HasValidReplay(value) ||
            !HasValidOutcome(value) ||
            value.EvidenceSha256 != ComputeSha256(value) ||
            (compilation != null && !HasValidReplayGeometry(value, compilation)))
        {
            throw new InvalidDataException("Compiler effect-claim evidence is invalid.");
        }
    }

    internal static bool HasValidReplayGeometry(
        CompilerEffectClaimArtifact? value,
        CompilerCompilationSnapshot? compilation)
    {
        if (value?.Replay == null)
        {
            return true;
        }

        if (compilation is not { SyntaxTrees: not null })
        {
            return false;
        }

        foreach (var effectEvent in value.Replay.Events ?? [])
        {
            if (effectEvent == null ||
                effectEvent.SyntaxTreeOrdinal < 0 ||
                effectEvent.SyntaxTreeOrdinal >= compilation.SyntaxTrees.Length)
            {
                return false;
            }

            var syntaxTree = compilation.SyntaxTrees[effectEvent.SyntaxTreeOrdinal];
            if (syntaxTree == null ||
                effectEvent.SyntaxTreeSha256 != syntaxTree.Sha256 ||
                effectEvent.SyntaxTreeSnapshotSha256 !=
                    CompilationFingerprint.ComputeSyntaxTreeSnapshotSha256(syntaxTree) ||
                effectEvent.SyntaxTreeLineMapSha256 != syntaxTree.LineMapSha256 ||
                effectEvent.SyntaxStart < 0 ||
                effectEvent.SyntaxLength <= 0 ||
                effectEvent.SyntaxStart > syntaxTree.TextLength ||
                effectEvent.SyntaxLength >
                    syntaxTree.TextLength - effectEvent.SyntaxStart)
            {
                return false;
            }

            if (CompilerSourceLocationAuthority.FindUniqueTree(
                    effectEvent.Location,
                    compilation) != effectEvent.SourceTreeOrdinal ||
                !CompilerSourceLocationAuthority.IsBound(
                    effectEvent.Location,
                    effectEvent.SourceTreeOrdinal,
                    effectEvent.SourceTreePath,
                    effectEvent.SourceTreeSha256,
                    effectEvent.SourceLineMapSha256,
                    compilation) ||
                effectEvent.Location.Start != effectEvent.SyntaxStart ||
                effectEvent.Location.Length != effectEvent.SyntaxLength)
            {
                return false;
            }
        }

        return true;
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
        var replay = value.Replay;
        if (replay == null)
        {
            return value.Outcome != WorkerClaimOutcome.Refuted;
        }

        if (value.Outcome != WorkerClaimOutcome.Refuted ||
            replay.PathKind != CompilerEffectEvidenceCatalog.ReplayPathKind ||
            replay.Events is not { Length: > 0 and <= CompilerEffectEvidenceCatalog.MaximumReplayEvents } ||
            replay.ConstraintSha256 != ComputeConstraintSha256(value.ContractKind, value.Constraint))
        {
            return false;
        }

        return replay.Events.Select((item, index) =>
            HasValidReplayEvent(item, index)).All(static valid => valid);
    }

    private static bool HasValidReplayEvent(CompilerEffectReplayEventArtifact? value, int ordinal)
    {
        if (value == null || value.Ordinal != ordinal ||
            !CompilerEffectEvidenceCatalog.SupportedReplayEventKinds.Contains(value.Kind) ||
            value.SyntaxTreeOrdinal < 0 ||
            !WorkerProtocolJson.IsSha256(value.SyntaxTreeSha256) ||
            !WorkerProtocolJson.IsSha256(value.SyntaxTreeSnapshotSha256) ||
            !WorkerProtocolJson.IsSha256(value.SyntaxTreeLineMapSha256) ||
            value.SourceTreeOrdinal < 0 ||
            value.SourceTreeOrdinal != value.SyntaxTreeOrdinal ||
            string.IsNullOrWhiteSpace(value.SourceTreePath) ||
            !WorkerProtocolJson.IsSha256(value.SourceTreeSha256) ||
            !WorkerProtocolJson.IsSha256(value.SourceLineMapSha256) ||
            value.SyntaxStart < 0 || value.SyntaxLength <= 0 ||
            value.SyntaxStart > int.MaxValue - value.SyntaxLength ||
            value.OperationIdentitySha256 != ComputeReplayOperationSha256(value) ||
            string.IsNullOrWhiteSpace(value.TypeIdentity) ||
            !HasOptionalText(value.MemberDocumentationId) ||
            !HasOptionalText(value.TypeDocumentationId) ||
            value.ScalarOperands is not { Length: 0 } ||
            value.ExactExceptionTypeHierarchy is not { } ||
            !WorkerProtocolJson.HasValidLocation(value.Location) ||
            value.Location.Start != value.SyntaxStart ||
            value.Location.Length != value.SyntaxLength)
        {
            return false;
        }

        if (value.SpecWitnessIdentifier != null)
        {
            return false;
        }

        return value.Kind switch
        {
            CompilerEffectReplayEventKind.ManagedObjectAllocation or
            CompilerEffectReplayEventKind.MonitorCall =>
                !string.IsNullOrWhiteSpace(value.MemberIdentity) &&
                value.ExactExceptionTypeHierarchy.Length == 0,
            CompilerEffectReplayEventKind.ManagedArrayAllocation or
            CompilerEffectReplayEventKind.EmptyLock =>
                string.IsNullOrEmpty(value.MemberIdentity) &&
                value.MemberDocumentationId == null &&
                value.ExactExceptionTypeHierarchy.Length == 0,
            CompilerEffectReplayEventKind.ExplicitThrow =>
                !string.IsNullOrWhiteSpace(value.MemberIdentity) &&
                value.ExactExceptionTypeHierarchy.Length > 0 &&
                HasCanonicalStrings(value.ExactExceptionTypeHierarchy) &&
                value.ExactExceptionTypeHierarchy.Contains(
                    value.TypeIdentity,
                    StringComparer.Ordinal),
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

    private static string ComputeSha256(CompilerEffectClaimArtifact value)
    {
        var witness = value.Witness;
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
        foreach (var effectEvent in replay?.Events ?? [])
        {
            AddReplayEvent(hash, effectEvent, includeOrdinal: true, includeOperationIdentity: true);
        }
        return hash.Add(witness?.Location.Path)
            .Add(witness?.Location.Start ?? -1)
            .Add(witness?.Location.Length ?? -1)
            .Add(witness?.Location.Line ?? -1)
            .Add(witness?.Location.Column ?? -1)
            .Add(value.Evidence)
            .Finish();
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
