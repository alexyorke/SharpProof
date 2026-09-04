namespace SharpProof.Worker;

internal static class EffectCounterexampleReplayer
{
    internal static WorkerEffectViolationWitness? Replay(
        CompilerCallablePreparation target,
        CompilerEffectClaimArtifact evidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(evidence);
        cancellationToken.ThrowIfCancellationRequested();
        CompilerEffectClaimArtifactCodec.Validate(evidence, target.Compilation);

        var replay = evidence.Replay ??
            throw Malformed("A refuted effect claim has no replay artifact.");
        if (replay.ConstraintSha256 !=
            ComputeConstraintIdentity(
                evidence.ContractKind,
                evidence.Constraint))
        {
            throw Malformed(
                "The effect replay constraint does not equal the selected contract.");
        }

        if (replay.PathKind !=
            CompilerEffectReplayPathKind.Unconditional)
        {
            throw Malformed(
                "An effect replay artifact has an invalid path kind.");
        }

        WorkerEffectViolationWitness? violation = null;
        var treeSnapshotHashes = target.Compilation.SyntaxTrees
            .Select(CompilationFingerprint.ComputeSyntaxTreeSnapshotSha256)
            .ToArray();
        for (var index = 0; index < replay.Events.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var effectEvent = replay.Events[index];
            ValidateEvent(target, effectEvent, index, treeSnapshotHashes);
            var observed = Interpret(effectEvent);
            if (observed == null)
            {
                return null;
            }

            if (violation == null &&
                CompilerEffectViolationAuthority.IsViolation(evidence, observed))
            {
                violation = observed;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return violation != null &&
            WitnessesEqual(violation, evidence.Witness)
                ? violation
                : null;
    }

    private static void ValidateEvent(
        CompilerCallablePreparation target,
        CompilerEffectReplayEventArtifact effectEvent,
        int ordinal,
        string[] treeSnapshotHashes)
    {
        if (effectEvent == null ||
            effectEvent.Ordinal != ordinal ||
            effectEvent.OperationIdentitySha256 !=
            ComputeOperationIdentity(effectEvent))
        {
            throw Malformed(
                "An effect replay event has an invalid identity or order.");
        }

        var trees = target.Compilation.SyntaxTrees;
        if (trees == null ||
            effectEvent.SyntaxTreeOrdinal < 0 ||
            effectEvent.SyntaxTreeOrdinal >= trees.Length)
        {
            throw Malformed(
                "An effect replay event names an unknown syntax tree.");
        }

        var tree = trees[effectEvent.SyntaxTreeOrdinal];
        if (tree == null ||
            effectEvent.SyntaxTreeSha256 != tree.Sha256 ||
            effectEvent.SyntaxTreeSnapshotSha256 != treeSnapshotHashes[effectEvent.SyntaxTreeOrdinal] ||
            effectEvent.SyntaxTreeLineMapSha256 != tree.LineMapSha256 ||
            effectEvent.SyntaxStart < 0 ||
            effectEvent.SyntaxLength <= 0 ||
            effectEvent.SyntaxStart > tree.TextLength ||
            effectEvent.SyntaxLength >
            tree.TextLength - effectEvent.SyntaxStart)
        {
            throw Malformed(
                "An effect replay event does not fit its syntax tree.");
        }

        var location = effectEvent.Location;
        if (CompilerSourceLocationAuthority.FindUniqueTree(
                location,
                target.Compilation) != effectEvent.SourceTreeOrdinal ||
            !CompilerSourceLocationAuthority.IsBound(
                location,
                effectEvent.SourceTreeOrdinal,
                effectEvent.SourceTreePath,
                effectEvent.SourceTreeSha256,
                effectEvent.SourceLineMapSha256,
                target.Compilation) ||
            location.Start != effectEvent.SyntaxStart ||
            location.Length != effectEvent.SyntaxLength)
        {
            throw Malformed(
                "An effect replay event has an invalid mapped location.");
        }
    }

    private static WorkerEffectViolationWitness? Interpret(
        CompilerEffectReplayEventArtifact effectEvent)
    {
        if (string.IsNullOrWhiteSpace(effectEvent.TypeIdentity) ||
            effectEvent.SpecWitnessIdentifier != null ||
            effectEvent.ScalarOperands.Length != 0)
        {
            return null;
        }

        return effectEvent.Kind switch
        {
            CompilerEffectReplayEventKind.ManagedObjectAllocation when
                !string.IsNullOrWhiteSpace(effectEvent.MemberIdentity) &&
                effectEvent.ExactExceptionTypeHierarchy.Length == 0 =>
                CreateWitness(
                    effectEvent,
                    "managed-allocation",
                    FirstNonblank(
                        effectEvent.MemberDocumentationId,
                        effectEvent.MemberIdentity),
                    WorkerEffectSet.Allocates),
            CompilerEffectReplayEventKind.ManagedArrayAllocation when
                string.IsNullOrEmpty(effectEvent.MemberIdentity) &&
                effectEvent.MemberDocumentationId == null &&
                effectEvent.ExactExceptionTypeHierarchy.Length == 0 =>
                CreateWitness(
                    effectEvent,
                    "managed-array-allocation",
                    FirstNonblank(
                        effectEvent.TypeDocumentationId,
                        effectEvent.TypeIdentity),
                    WorkerEffectSet.Allocates),
            CompilerEffectReplayEventKind.ExplicitThrow when
                !string.IsNullOrWhiteSpace(effectEvent.MemberIdentity) &&
                effectEvent.ExactExceptionTypeHierarchy.Length > 0 &&
                effectEvent.ExactExceptionTypeHierarchy.Contains(
                    effectEvent.TypeIdentity,
                    StringComparer.Ordinal) =>
                CreateWitness(
                    effectEvent,
                    "explicit-throw",
                    FirstNonblank(
                        effectEvent.TypeDocumentationId,
                        effectEvent.TypeIdentity),
                    WorkerEffectSet.Throws,
                    exceptions:
                        effectEvent.ExactExceptionTypeHierarchy),
            CompilerEffectReplayEventKind.MonitorCall when
                !string.IsNullOrWhiteSpace(effectEvent.MemberIdentity) &&
                effectEvent.ExactExceptionTypeHierarchy.Length == 0 =>
                CreateWitness(
                    effectEvent,
                    "synchronization-call",
                    FirstNonblank(
                        effectEvent.MemberDocumentationId,
                        effectEvent.MemberIdentity),
                    WorkerEffectSet.Synchronizes,
                    WorkerEffectCapabilitySet.Synchronization),
            CompilerEffectReplayEventKind.EmptyLock when
                string.IsNullOrEmpty(effectEvent.MemberIdentity) &&
                effectEvent.MemberDocumentationId == null &&
                effectEvent.ExactExceptionTypeHierarchy.Length == 0 =>
                CreateWitness(
                    effectEvent,
                    "synchronization-lock",
                    FirstNonblank(
                        effectEvent.TypeDocumentationId,
                        effectEvent.TypeIdentity),
                    WorkerEffectSet.Synchronizes,
                    WorkerEffectCapabilitySet.Synchronization),
            _ => null
        };
    }

    private static WorkerEffectViolationWitness? CreateWitness(
        CompilerEffectReplayEventArtifact effectEvent,
        string kind,
        string? detail,
        WorkerEffectSet effects,
        WorkerEffectCapabilitySet capabilities =
            WorkerEffectCapabilitySet.None,
        string[]? exceptions = null)
    {
        return detail == null
            ? null
            : new WorkerEffectViolationWitness
            {
                Kind = kind,
                Detail = detail,
                Effects = effects,
                Capabilities = capabilities,
                ExactExceptionTypeHierarchy = exceptions == null
                    ? []
                    : [.. exceptions],
                Location = CompilerSourceLocationAuthority.CopyLocation(effectEvent.Location)
            };
    }

    private static bool WitnessesEqual(
        WorkerEffectViolationWitness actual,
        WorkerEffectViolationWitness? claimed)
    {
        if (claimed == null)
        {
            return false;
        }

        return (actual.Kind, actual.Detail, actual.Effects,
                   actual.Capabilities) ==
               (claimed.Kind, claimed.Detail, claimed.Effects,
                   claimed.Capabilities) &&
               actual.ExactExceptionTypeHierarchy.SequenceEqual(
                claimed.ExactExceptionTypeHierarchy,
                StringComparer.Ordinal) &&
               CompilerSourceLocationAuthority.LocationsEqual(
                   actual.Location,
                   claimed.Location);
    }

    private static string? FirstNonblank(
        string? preferred,
        string fallback)
    {
        return !string.IsNullOrWhiteSpace(preferred)
            ? preferred
            : !string.IsNullOrWhiteSpace(fallback)
                ? fallback
                : null;
    }

    internal static string ComputeConstraintIdentity(
        WorkerEffectContractKind kind,
        CompilerEffectConstraintArtifact constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        using var hash = new CanonicalHashWriter();
        hash.Add("SharpProof.CompilerEffectReplayConstraint")
            .Add(1)
            .Add(kind)
            .Add(constraint.AllowedEffects)
            .Add(constraint.AllowedCapabilities);
        foreach (var type in constraint.AllowedExceptionTypes
                     .OrderBy(static item => item, StringComparer.Ordinal))
        {
            hash.Add(type);
        }

        return hash.Finish();
    }

    internal static string ComputeOperationIdentity(
        CompilerEffectReplayEventArtifact effectEvent)
    {
        ArgumentNullException.ThrowIfNull(effectEvent);
        var location = effectEvent.Location;
        using var hash = new CanonicalHashWriter();
        hash.Add("SharpProof.CompilerEffectReplayOperation")
            .Add(1)
            .Add(effectEvent.Kind)
            .Add(effectEvent.SyntaxTreeOrdinal)
            .Add(effectEvent.SyntaxTreeSha256)
            .Add(effectEvent.SyntaxTreeSnapshotSha256)
            .Add(effectEvent.SyntaxTreeLineMapSha256)
            .Add(effectEvent.SyntaxStart)
            .Add(effectEvent.SyntaxLength)
            .Add(effectEvent.MemberIdentity)
            .Add(effectEvent.MemberDocumentationId)
            .Add(effectEvent.TypeIdentity)
            .Add(effectEvent.TypeDocumentationId)
            .Add(effectEvent.SpecWitnessIdentifier);
        hash.Add(effectEvent.SourceTreeOrdinal)
            .Add(effectEvent.SourceTreePath)
            .Add(effectEvent.SourceTreeSha256)
            .Add(effectEvent.SourceLineMapSha256);
        hash.Add(effectEvent.ScalarOperands.Length);
        foreach (var operand in effectEvent.ScalarOperands)
        {
            hash.Add(operand);
        }

        hash.Add(effectEvent.ExactExceptionTypeHierarchy.Length);
        foreach (var type in effectEvent.ExactExceptionTypeHierarchy)
        {
            hash.Add(type);
        }

        return hash.Add(location?.Path)
            .Add(location?.Start ?? -1)
            .Add(location?.Length ?? -1)
            .Add(location?.Line ?? -1)
            .Add(location?.Column ?? -1)
            .Finish();
    }

    private static InvalidDataException Malformed(string message)
    {
        return new InvalidDataException(message);
    }

}
