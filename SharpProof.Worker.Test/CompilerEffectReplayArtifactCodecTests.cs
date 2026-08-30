using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerEffectReplayArtifactCodecTests
{
    [Test]
    public void CanonicalHashesBindConstraintSemanticsAndEveryOperationField()
    {
        var constraint = new CompilerEffectConstraintArtifact
        {
            AllowedEffects = WorkerEffectSet.Throws,
            AllowedCapabilities = WorkerEffectCapabilitySet.IO,
            AllowedExceptionTypes = ["type-b", "type-a"]
        };
        var reordered = new CompilerEffectConstraintArtifact
        {
            AllowedEffects = constraint.AllowedEffects,
            AllowedCapabilities = constraint.AllowedCapabilities,
            AllowedExceptionTypes = ["type-a", "type-b"]
        };
        var constraintHash =
            CompilerEffectClaimArtifactCodec.ComputeConstraintSha256(
                WorkerEffectContractKind.EffectContract,
                constraint);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CompilerEffectClaimArtifactCodec.ComputeConstraintSha256(
                    WorkerEffectContractKind.EffectContract,
                    reordered),
                Is.EqualTo(constraintHash));
            Assert.That(
                CompilerEffectClaimArtifactCodec.ComputeConstraintSha256(
                    WorkerEffectContractKind.AllowedExceptions,
                    reordered),
                Is.Not.EqualTo(constraintHash));
        }

        var baseline = ReplayEvent();
        var operationHash =
            CompilerEffectClaimArtifactCodec.ComputeReplayOperationSha256(
                baseline);
        Action<CompilerEffectReplayEventArtifact>[] mutations =
        [
            value => value.Kind =
                CompilerEffectReplayEventKind.ManagedArrayAllocation,
            value => value.SyntaxTreeOrdinal++,
            value => value.SyntaxTreeSha256 = new string('b', 64),
            value => value.SyntaxTreeSnapshotSha256 = new string('b', 64),
            value => value.SyntaxTreeLineMapSha256 = new string('b', 64),
            value => value.SyntaxStart++,
            value => value.SyntaxLength++,
            value => value.MemberIdentity += "-changed",
            value => value.MemberDocumentationId = "M:Changed",
            value => value.TypeIdentity += "-changed",
            value => value.TypeDocumentationId = "T:Changed",
            value => value.SpecWitnessIdentifier = "spec",
            value => value.ScalarOperands = [1],
            value => value.ExactExceptionTypeHierarchy = ["exception"],
            value => value.SourceTreeOrdinal++,
            value => value.SourceTreePath += ":changed",
            value => value.SourceTreeSha256 = new string('b', 64),
            value => value.SourceLineMapSha256 = new string('b', 64),
            value => value.Location.Path = "Mapped.cs",
            value => value.Location.Start++,
            value => value.Location.Length++,
            value => value.Location.Line++,
            value => value.Location.Column++
        ];

        foreach (var mutate in mutations)
        {
            var changed = ReplayEvent();
            mutate(changed);
            Assert.That(
                CompilerEffectClaimArtifactCodec
                    .ComputeReplayOperationSha256(changed),
                Is.Not.EqualTo(operationHash));
        }
    }

    [Test]
    public void CodecRequiresSealedUnconditionalAllocationReplayForRefutation()
    {
        var evidence = RefutedEvidence();

        Assert.DoesNotThrow(
            (Action)(() =>
                CompilerEffectClaimArtifactCodec.Validate(evidence)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evidence.Replay!.ConstraintSha256,
                Is.EqualTo(
                    CompilerEffectClaimArtifactCodec.ComputeConstraintSha256(
                        evidence.ContractKind,
                        evidence.Constraint)));
            Assert.That(
                evidence.Replay.Events[0].OperationIdentitySha256,
                Is.EqualTo(
                    CompilerEffectClaimArtifactCodec
                        .ComputeReplayOperationSha256(
                            evidence.Replay.Events[0])));
        }

        AssertRejected(static value => value.Replay = null);
        AssertRejected(static value =>
            value.Replay!.PathKind =
                CompilerEffectReplayPathKind.Unspecified);
        AssertRejected(static value =>
            value.Replay!.Events[0].Kind =
                CompilerEffectReplayEventKind.ExplicitThrow);
        AssertRejected(static value =>
            value.Replay!.Events[0].SyntaxLength = 0);
        AssertRejected(static value =>
            value.Replay!.Events[0].ScalarOperands = [1]);
        AssertRejected(static value =>
            value.Replay!.Events[0].ExactExceptionTypeHierarchy =
                ["exception"]);
        AssertRejected(static value =>
            value.Replay!.Events[0].Ordinal = 1);
        AssertRejected(static value =>
        {
            value.Outcome = WorkerClaimOutcome.Proven;
            value.Certainty =
                WorkerEffectEvidenceCertainty.CompleteMayEffectSummary;
            value.Witness = null;
        });
        AssertRejected(static value =>
        {
            value.Outcome = WorkerClaimOutcome.Unknown;
            value.Reason =
                WorkerClaimReason.CounterexampleNotReplayable;
            value.Certainty =
                WorkerEffectEvidenceCertainty.Unavailable;
            value.Witness = null;
        });
    }

    [TestCase((int)CompilerEffectReplayEventKind.ExplicitThrow)]
    [TestCase((int)CompilerEffectReplayEventKind.MonitorCall)]
    [TestCase((int)CompilerEffectReplayEventKind.EmptyLock)]
    public void CodecAcceptsAuthenticatedCapabilityAndExceptionReplayShapes(
        int kindValue)
    {
        var kind = (CompilerEffectReplayEventKind)kindValue;
        var evidence = RefutedEvidence(kind);

        Assert.DoesNotThrow((Action)(() =>
            CompilerEffectClaimArtifactCodec.Validate(evidence)));
    }

    [Test]
    public void CodecRejectsMalformedCapabilityAndExceptionReplayShapes()
    {
        AssertRejected(
            CompilerEffectReplayEventKind.ExplicitThrow,
            static value =>
                value.Replay!.Events[0].ExactExceptionTypeHierarchy =
                [ExceptionIdentity]);
        AssertRejected(
            CompilerEffectReplayEventKind.ExplicitThrow,
            static value => Array.Reverse(
                value.Replay!.Events[0]
                    .ExactExceptionTypeHierarchy));
        AssertRejected(
            CompilerEffectReplayEventKind.MonitorCall,
            static value =>
                value.Replay!.Events[0].MemberIdentity = string.Empty);
        AssertRejected(
            CompilerEffectReplayEventKind.EmptyLock,
            static value =>
                value.Replay!.Events[0].MemberIdentity = "member");
    }

    [Test]
    public void CodecRejectsNoncanonicalAllowedExceptionOrdering()
    {
        var evidence = new CompilerEffectClaimArtifact
        {
            ClaimId = "allowed-exceptions",
            ContractKind = WorkerEffectContractKind.AllowedExceptions,
            Outcome = WorkerClaimOutcome.Proven,
            Reason = WorkerClaimReason.None,
            Certainty =
                WorkerEffectEvidenceCertainty.CompleteMayEffectSummary,
            Constraint = new CompilerEffectConstraintArtifact
            {
                AllowedExceptionTypes = ["exception-a", "exception-b"]
            },
            Evidence = "complete-exception-summary"
        };
        CompilerEffectClaimArtifactCodec.Seal(evidence);
        Assert.DoesNotThrow((Action)(() =>
            CompilerEffectClaimArtifactCodec.Validate(evidence)));

        Array.Reverse(evidence.Constraint.AllowedExceptionTypes);

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerEffectClaimArtifactCodec.Validate(evidence)));
    }

    private static void AssertRejected(
        Action<CompilerEffectClaimArtifact> mutate)
    {
        var evidence = RefutedEvidence();
        mutate(evidence);
        CompilerEffectClaimArtifactCodec.Seal(evidence);

        Assert.Throws<InvalidDataException>(
            (Action)(() =>
                CompilerEffectClaimArtifactCodec.Validate(evidence)));
    }

    private static void AssertRejected(
        CompilerEffectReplayEventKind kind,
        Action<CompilerEffectClaimArtifact> mutate)
    {
        var evidence = RefutedEvidence(kind);
        mutate(evidence);
        CompilerEffectClaimArtifactCodec.Seal(evidence);

        Assert.Throws<InvalidDataException>(
            (Action)(() =>
                CompilerEffectClaimArtifactCodec.Validate(evidence)));
    }

    private static CompilerEffectClaimArtifact RefutedEvidence(
        CompilerEffectReplayEventKind kind)
    {
        var evidence = RefutedEvidence();
        var effectEvent = evidence.Replay!.Events[0];
        switch (kind)
        {
            case CompilerEffectReplayEventKind.ExplicitThrow:
                effectEvent.Kind = kind;
                effectEvent.MemberIdentity =
                    "assembly::M:System.InvalidOperationException.#ctor";
                effectEvent.MemberDocumentationId =
                    "M:System.InvalidOperationException.#ctor";
                effectEvent.TypeIdentity =
                    InvalidOperationExceptionIdentity;
                effectEvent.TypeDocumentationId =
                    "T:System.InvalidOperationException";
                effectEvent.ExactExceptionTypeHierarchy =
                    [ExceptionIdentity, InvalidOperationExceptionIdentity];
                evidence.ContractKind =
                    WorkerEffectContractKind.EffectContract;
                evidence.Constraint.AllowedEffects =
                    WorkerEffectSet.Throws;
                evidence.Constraint.AllowedExceptionTypes =
                    [ArgumentExceptionIdentity];
                evidence.Witness!.Kind = "explicit-throw";
                evidence.Witness.Detail =
                    effectEvent.TypeDocumentationId;
                evidence.Witness.Effects = WorkerEffectSet.Throws;
                evidence.Witness.ExactExceptionTypeHierarchy =
                    [.. effectEvent.ExactExceptionTypeHierarchy];
                break;
            case CompilerEffectReplayEventKind.MonitorCall:
                effectEvent.Kind = kind;
                effectEvent.MemberIdentity =
                    "assembly::M:System.Threading.Monitor.Enter";
                effectEvent.MemberDocumentationId =
                    "M:System.Threading.Monitor.Enter(System.Object)";
                effectEvent.TypeIdentity = MonitorIdentity;
                effectEvent.TypeDocumentationId =
                    "T:System.Threading.Monitor";
                SetSynchronizationWitness(
                    evidence,
                    "synchronization-call",
                    effectEvent.MemberDocumentationId);
                break;
            case CompilerEffectReplayEventKind.EmptyLock:
                effectEvent.Kind = kind;
                effectEvent.MemberIdentity = string.Empty;
                effectEvent.MemberDocumentationId = null;
                effectEvent.TypeIdentity = MonitorIdentity;
                effectEvent.TypeDocumentationId =
                    "T:System.Threading.Monitor";
                SetSynchronizationWitness(
                    evidence,
                    "synchronization-lock",
                    effectEvent.TypeDocumentationId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        CompilerEffectClaimArtifactCodec.Seal(evidence);
        return evidence;
    }

    private static void SetSynchronizationWitness(
        CompilerEffectClaimArtifact evidence,
        string kind,
        string detail)
    {
        evidence.ContractKind = WorkerEffectContractKind.EffectContract;
        evidence.Constraint.AllowedEffects =
            WorkerEffectSet.Synchronizes;
        evidence.Witness!.Kind = kind;
        evidence.Witness.Detail = detail;
        evidence.Witness.Effects = WorkerEffectSet.Synchronizes;
        evidence.Witness.Capabilities =
            WorkerEffectCapabilitySet.Synchronization;
    }

    private static CompilerEffectClaimArtifact RefutedEvidence()
    {
        var effectEvent = ReplayEvent();
        var evidence = new CompilerEffectClaimArtifact
        {
            ClaimId = "effect-allocation",
            ContractKind = WorkerEffectContractKind.ZeroAllocations,
            Outcome = WorkerClaimOutcome.Refuted,
            Reason = WorkerClaimReason.None,
            Certainty =
                WorkerEffectEvidenceCertainty.DefiniteViolation,
            Constraint = new CompilerEffectConstraintArtifact(),
            Witness = new WorkerEffectViolationWitness
            {
                Kind = "managed-object-allocation",
                Detail = effectEvent.MemberDocumentationId!,
                Effects = WorkerEffectSet.Allocates,
                Location = Location()
            },
            Replay = new CompilerEffectReplayArtifact
            {
                PathKind =
                    CompilerEffectReplayPathKind.Unconditional,
                Events = [effectEvent]
            },
            Evidence = "unconditional-allocation"
        };
        CompilerEffectClaimArtifactCodec.Seal(evidence);
        return evidence;
    }

    private static CompilerEffectReplayEventArtifact ReplayEvent()
    {
        return new CompilerEffectReplayEventArtifact
        {
            Ordinal = 0,
            Kind =
                CompilerEffectReplayEventKind.ManagedObjectAllocation,
            SyntaxTreeOrdinal = 0,
            SyntaxTreeSha256 = new string('a', 64),
            SyntaxTreeSnapshotSha256 = new string('c', 64),
            SyntaxTreeLineMapSha256 = new string('d', 64),
            SyntaxStart = 10,
            SyntaxLength = 12,
            MemberIdentity = "assembly::M:System.Object.#ctor",
            MemberDocumentationId = "M:System.Object.#ctor",
            TypeIdentity = "assembly::T:System.Object",
            TypeDocumentationId = "T:System.Object",
            Location = Location(),
            SourceTreeOrdinal = 0,
            SourceTreePath = "Subject.cs",
            SourceTreeSha256 = new string('a', 64),
            SourceLineMapSha256 = new string('d', 64)
        };
    }

    private static WorkerSourceLocation Location()
    {
        return new WorkerSourceLocation
        {
            Path = "Subject.cs",
            Start = 10,
            Length = 12,
            Line = 1,
            Column = 1
        };
    }

    private const string ArgumentExceptionIdentity =
        "assembly::T:System.ArgumentException";
    private const string ExceptionIdentity =
        "assembly::T:System.Exception";
    private const string InvalidOperationExceptionIdentity =
        "assembly::T:System.InvalidOperationException";
    private const string MonitorIdentity =
        "assembly::T:System.Threading.Monitor";
}
