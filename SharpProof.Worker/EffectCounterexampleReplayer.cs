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
        var replay = evidence.Replay ??
            throw new InvalidDataException("A refuted effect claim has no replay artifact.");
        WorkerEffectViolationWitness? violation = null;
        foreach (var effectEvent in replay.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observed = Interpret(effectEvent);
            if (observed == null)
            {
                return null;
            }
            if (violation == null && CompilerEffectViolation.IsViolation(evidence, observed))
            {
                violation = observed;
            }
        }
        return WitnessesEqual(violation, evidence.Witness) ? violation : null;
    }

    private static bool WitnessesEqual(WorkerEffectViolationWitness? left, WorkerEffectViolationWitness? right)
    {
        return left != null && right != null && left.Kind == right.Kind &&
            left.Detail == right.Detail && left.Effects == right.Effects && left.Capabilities == right.Capabilities &&
            left.ExactExceptionTypeHierarchy.SequenceEqual(right.ExactExceptionTypeHierarchy, StringComparer.Ordinal) &&
            left.Location.Path == right.Location.Path && left.Location.Start == right.Location.Start &&
            left.Location.Length == right.Location.Length && left.Location.Line == right.Location.Line &&
            left.Location.Column == right.Location.Column;
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
                    WorkerEffectSet.Throws),
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

    private static WorkerEffectViolationWitness CreateWitness(
        CompilerEffectReplayEventArtifact effectEvent,
        string kind,
        string detail,
        WorkerEffectSet effects,
        WorkerEffectCapabilitySet capabilities =
            WorkerEffectCapabilitySet.None)
    {
        return new WorkerEffectViolationWitness
        {
            Kind = kind,
            Detail = detail,
            Effects = effects,
            Capabilities = capabilities,
            ExactExceptionTypeHierarchy =
                [.. effectEvent.ExactExceptionTypeHierarchy],
            Location = CopyLocation(
                effectEvent.Location)
        };
    }

    private static string FirstNonblank(
        string? preferred,
        string fallback)
    {
        return !string.IsNullOrWhiteSpace(preferred)
            ? preferred
            : fallback;
    }

    private static WorkerSourceLocation CopyLocation(WorkerSourceLocation value)
    {
        return new WorkerSourceLocation
        {
            Path = value.Path,
            Start = value.Start,
            Length = value.Length,
            Line = value.Line,
            Column = value.Column
        };
    }
}
