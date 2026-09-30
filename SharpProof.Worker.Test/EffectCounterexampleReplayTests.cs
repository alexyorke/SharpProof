using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EffectCounterexampleReplayTests
{
    private static readonly CompilerEffectReplayEventKind[]
        s_allocationEventKinds = [
            CompilerEffectReplayEventKind.ManagedObjectAllocation,
            CompilerEffectReplayEventKind.ManagedArrayAllocation
        ];

    [Test]
    public void UnconditionalObjectAndArrayAllocationsAreIndependentlyConfirmed()
    {
        foreach (var kind in s_allocationEventKinds)
        {
            var fixture = CreateFixture(kind);
            var result = EffectClaimResultAssembler.Assemble(
                fixture.Target,
                fixture.Evidence,
                CallableEntryFeasibility.Feasible,
                CancellationToken.None);

            AssertRefuted(
                fixture.Evidence.Witness!,
                result,
                requireDistinctWitness: true);
        }
    }

    [TestCase("event-order")]
    [TestCase("path-kind")]
    [TestCase("type")]
    [TestCase("location")]
    public void StructurallyMalformedReplayEvidenceIsRejectedAtArtifactBoundary(string mutation)
    {
        var fixture = CreateFixture(CompilerEffectReplayEventKind.ManagedObjectAllocation);
        switch (mutation)
        {
            case "event-order":
                fixture.Event.Ordinal = 1;
                break;
            case "path-kind":
                fixture.Evidence.Replay!.PathKind = CompilerEffectReplayPathKind.Unspecified;
                break;
            case "type":
                fixture.Event.TypeIdentity = string.Empty;
                break;
            case "location":
                fixture.Event.Location.Line = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.Throws<InvalidDataException>((Action)(() => CompilerEffectClaimArtifactCodec.Validate(fixture.Evidence)));
    }
    [TestCase("kind")]
    [TestCase("detail")]
    [TestCase("effects")]
    [TestCase("capabilities")]
    [TestCase("exception-hierarchy")]
    [TestCase("location")]
    public void SemanticWitnessMismatchRemainsTypedUnknown(
        string tampering)
    {
        var fixture = CreateFixture(
            CompilerEffectReplayEventKind.ManagedArrayAllocation);
        var witness = fixture.Evidence.Witness!;
        switch (tampering)
        {
            case "kind":
                witness.Kind += ":tampered";
                break;
            case "detail":
                witness.Detail += ":tampered";
                break;
            case "effects":
                witness.Effects = WorkerEffectSet.Throws;
                break;
            case "capabilities":
                witness.Capabilities =
                    WorkerEffectCapabilitySet.IO;
                break;
            case "exception-hierarchy":
                witness.Effects |= WorkerEffectSet.Throws;
                witness.ExactExceptionTypeHierarchy = [
                    "System.Private.CoreLib::" +
                    "T:System.InvalidOperationException"
                ];
                break;
            case "location":
                witness.Location.Path = "Tampered.cs";
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(tampering));
        }

        CompilerEffectClaimArtifactCodec.Seal(fixture.Evidence);

        var result = EffectClaimResultAssembler.Assemble(
            fixture.Target,
            fixture.Evidence,
            CallableEntryFeasibility.Feasible,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                result.Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleReplayFailed));
            Assert.That(
                result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(result.EffectWitness, Is.Null);
        }
    }

    [Test]
    public void AllocationCannotRefuteAnUnrelatedEffectContract()
    {
        var fixture = CreateFixture(
            CompilerEffectReplayEventKind.ManagedObjectAllocation);
        fixture.Evidence.ContractKind =
            WorkerEffectContractKind.DoesNotThrow;
        AssertAllocationReplayUnknown(fixture);
    }

    [TestCase(
        WorkerEffectContractKind.EnforcePure,
        WorkerEffectSet.None,
        WorkerClaimOutcome.Unknown)]
    [TestCase(
        WorkerEffectContractKind.ZeroAllocations,
        WorkerEffectSet.None,
        WorkerClaimOutcome.Refuted)]
    [TestCase(
        WorkerEffectContractKind.EffectContract,
        WorkerEffectSet.None,
        WorkerClaimOutcome.Refuted)]
    [TestCase(
        WorkerEffectContractKind.EffectContract,
        WorkerEffectSet.Allocates,
        WorkerClaimOutcome.Unknown)]
    public void AllocationReplayRespectsTheSelectedContract(
        WorkerEffectContractKind contractKind,
        WorkerEffectSet allowedEffects,
        WorkerClaimOutcome expected)
    {
        var fixture = CreateFixture(
            CompilerEffectReplayEventKind.ManagedObjectAllocation);
        fixture.Evidence.ContractKind = contractKind;
        fixture.Evidence.Constraint.AllowedEffects =
            allowedEffects;
        CompilerEffectClaimArtifactCodec.Seal(fixture.Evidence);

        var result = EffectClaimResultAssembler.Assemble(
            fixture.Target,
            fixture.Evidence,
            CallableEntryFeasibility.Feasible,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(expected));
            Assert.That(
                result.Reason,
                Is.EqualTo(
                    expected == WorkerClaimOutcome.Refuted
                        ? WorkerClaimReason.None
                        : WorkerClaimReason
                            .CounterexampleReplayFailed));
            Assert.That(
                result.EffectWitness != null,
                Is.EqualTo(
                    expected == WorkerClaimOutcome.Refuted));
        }
    }

    private static void AssertAllocationReplayUnknown(
        ReplayFixture fixture)
    {
        CompilerEffectClaimArtifactCodec.Seal(fixture.Evidence);

        var result = EffectClaimResultAssembler.Assemble(
            fixture.Target,
            fixture.Evidence,
            CallableEntryFeasibility.Feasible,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                result.Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleReplayFailed));
            Assert.That(result.EffectWitness, Is.Null);
        }
    }

    [Test]
    public void CapabilityReplayRefutesACombinedContractWhenItsEffectIsAllowed()
    {
        foreach (var kind in new[]
                 {
                     CompilerEffectReplayEventKind.MonitorCall,
                     CompilerEffectReplayEventKind.EmptyLock
                 })
        {
            var fixture = CreateFixture(kind);
            fixture.Evidence.ContractKind =
                WorkerEffectContractKind.EffectContract;
            fixture.Evidence.Constraint.AllowedEffects =
                WorkerEffectSet.Synchronizes;
            fixture.Evidence.Constraint.AllowedCapabilities =
                WorkerEffectCapabilitySet.None;
            CompilerEffectClaimArtifactCodec.Seal(fixture.Evidence);

            var result = EffectClaimResultAssembler.Assemble(
                fixture.Target,
                fixture.Evidence,
                CallableEntryFeasibility.Feasible,
                CancellationToken.None);

            AssertRefuted(fixture.Evidence.Witness!, result);
        }
    }

    [Test]
    public void ExceptionReplayRefutesACombinedContractWhenThrowingIsAllowed()
    {
        var fixture = CreateFixture(
            CompilerEffectReplayEventKind.ExplicitThrow);
        fixture.Evidence.ContractKind =
            WorkerEffectContractKind.EffectContract;
        fixture.Evidence.Constraint.AllowedEffects =
            WorkerEffectSet.Throws;
        fixture.Evidence.Constraint.AllowedExceptionTypes =
            [ArgumentExceptionIdentity];
        CompilerEffectClaimArtifactCodec.Seal(fixture.Evidence);

        var result = EffectClaimResultAssembler.Assemble(
            fixture.Target,
            fixture.Evidence,
            CallableEntryFeasibility.Feasible,
            CancellationToken.None);

        AssertRefuted(fixture.Evidence.Witness!, result);
    }



    [Test]
    public void CanceledReplayDoesNotPoisonTheNextReplay()
    {
        foreach (var kind in s_allocationEventKinds)
        {
            var fixture = CreateFixture(kind);
            using var cancellation =
                new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>((Action)(() =>
                EffectClaimResultAssembler.Assemble(
                    fixture.Target,
                    fixture.Evidence,
                    CallableEntryFeasibility.Feasible,
                    cancellation.Token)));

            var recovered = EffectClaimResultAssembler.Assemble(
                fixture.Target,
                fixture.Evidence,
                CallableEntryFeasibility.Feasible,
                CancellationToken.None);
            Assert.That(
                recovered.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted),
                kind.ToString());
        }
    }

    [TestCase("proven")]
    [TestCase("unknown")]
    [TestCase("unsupported-contract")]
    [TestCase("entry-failure")]
    public void CanceledNonRefutedEffectAssemblyCannotPublish(
        string scenario)
    {
        var fixture = CreateFixture(
            CompilerEffectReplayEventKind.ManagedObjectAllocation);
        var entryFeasibility = CallableEntryFeasibility.Feasible;
        switch (scenario)
        {
            case "proven":
                fixture.Evidence.Outcome = WorkerClaimOutcome.Proven;
                fixture.Evidence.Reason = WorkerClaimReason.None;
                fixture.Evidence.Certainty = WorkerEffectEvidenceCertainty
                    .CompleteMayEffectSummary;
                break;
            case "unknown":
                fixture.Evidence.Outcome = WorkerClaimOutcome.Unknown;
                fixture.Evidence.Reason =
                    WorkerClaimReason.EffectSummaryIncomplete;
                fixture.Evidence.Certainty = WorkerEffectEvidenceCertainty
                    .IncompleteMayEffectSummary;
                break;
            case "unsupported-contract":
                fixture.Evidence.Outcome = WorkerClaimOutcome.Unknown;
                fixture.Evidence.Reason =
                    WorkerClaimReason.UnsupportedContract;
                fixture.Evidence.Certainty =
                    WorkerEffectEvidenceCertainty.Unavailable;
                break;
            case "entry-failure":
                entryFeasibility = CallableEntryFeasibility.Unknown(
                    WorkerClaimReason.ResourceLimit);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>((Action)(() =>
            EffectClaimResultAssembler.Assemble(
                fixture.Target,
                fixture.Evidence,
                entryFeasibility,
                cancellation.Token)));
    }

    [Test]
    public async Task ConcurrentObjectAndArrayReplaysRemainIndependent()
    {
        var kinds = Enumerable.Range(0, 32)
            .Select(static index =>
                index % 2 == 0
                    ? CompilerEffectReplayEventKind
                        .ManagedObjectAllocation
                    : CompilerEffectReplayEventKind
                        .ManagedArrayAllocation)
            .ToArray();
        var results = await Task.WhenAll(kinds.Select(static kind =>
            Task.Run(() =>
            {
                var fixture = CreateFixture(kind);
                return EffectClaimResultAssembler.Assemble(
                    fixture.Target,
                    fixture.Evidence,
                    CallableEntryFeasibility.Feasible,
                    CancellationToken.None);
            })));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                results.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                results.Count(static result =>
                    result.EffectWitness?.Kind ==
                    AllocationWitnessKinds.Managed[0]),
                Is.EqualTo(16));
            Assert.That(
                results.Count(static result =>
                    result.EffectWitness?.Kind ==
                    AllocationWitnessKinds.Managed[1]),
                Is.EqualTo(16));
        }
    }

    [Test]
    public void CompleteReplayedEffectRefutationsAreCacheable()
    {
        var fixture = CreateFixture(
            CompilerEffectReplayEventKind.ManagedObjectAllocation);
        var manifest = new WorkerClaimManifest
        {
            Callables = [fixture.Target.Entry],
            Claims = [
                new WorkerClaimManifestEntry
                {
                    ClaimId = ClaimId,
                    CallableId = CallableId,
                    Kind = WorkerClaimKind.Effect,
                    Evidence = WorkerClaimEvidence.Attribute,
                    EffectContractKind =
                        WorkerEffectContractKind.ZeroAllocations,
                    Location = Location()
                }
            ]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var result = EffectClaimResultAssembler.Assemble(
            fixture.Target,
            fixture.Evidence,
            CallableEntryFeasibility.Feasible,
            CancellationToken.None);
        var response = WorkerResultAssembler.Create(
            new string('a', 64),
            manifest,
            WorkerRunStatus.Complete,
            WorkerRunFailureReason.None,
            [
                new WorkerCallableResult
                {
                    CallableId = CallableId,
                    Coverage = WorkerCallableCoverage.Complete,
                    Reason = WorkerCallableCoverageReason.None
                }
            ],
            [result],
            new WorkerBudgets(),
            WorkerCacheStatus.Miss,
            0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                WorkerProtocolJson.Validate(
                    response,
                    response.InputHash,
                    manifest).IsValid,
                Is.True);
            Assert.That(
                VerificationCache.IsCacheable(
                    response,
                    response.InputHash,
                    manifest),
                Is.True);
        }
    }

    private static ReplayFixture CreateFixture(
        CompilerEffectReplayEventKind kind)
    {
        var location = Location();
        var isObject =
            kind ==
            CompilerEffectReplayEventKind.ManagedObjectAllocation;
        var isArray =
            kind ==
            CompilerEffectReplayEventKind.ManagedArrayAllocation;
        var isThrow =
            kind == CompilerEffectReplayEventKind.ExplicitThrow;
        var isLock =
            kind == CompilerEffectReplayEventKind.EmptyLock;
        var isMonitor =
            kind == CompilerEffectReplayEventKind.MonitorCall;
        if (!isObject && !isArray && !isThrow && !isLock && !isMonitor)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var snapshot = new CompilerSyntaxTreeSnapshot
        {
            Path = "Subject.cs",
            Sha256 = TreeSha256,
            TextLength = 100,
            LineMap = [
                new CompilerSourceLineMapEntry
                {
                    SourceStart = 0,
                    SourceLength = 9,
                    MappedPath = "MappedSubject.cs",
                    MappedLine = 0,
                    MappedColumn = 0
                },
                new CompilerSourceLineMapEntry
                {
                    SourceStart = 10,
                    SourceLength = 90,
                    MappedPath = "MappedSubject.cs",
                    MappedLine = 6,
                    MappedColumn = 4
                }
            ]
        };
        snapshot.LineMapSha256 = CompilerReportingIdentity.ComputeLineMapSha256(
            snapshot.LineMap);
        var effectEvent = new CompilerEffectReplayEventArtifact
        {
            Ordinal = 0,
            Kind = kind,
            SyntaxTreeOrdinal = 0,
            SyntaxTreeSha256 = TreeSha256,
            SyntaxTreeSnapshotSha256 =
                CompilerReportingIdentity.ComputeSyntaxTreeSnapshotSha256(
                    snapshot),
            SyntaxTreeLineMapSha256 = snapshot.LineMapSha256,
            SyntaxStart = location.Start,
            SyntaxLength = location.Length,
            MemberIdentity = isObject
                ? "Assembly::M:Subject.#ctor"
                : isThrow
                    ? "Assembly::M:System.InvalidOperationException.#ctor"
                    : isMonitor
                        ? "Assembly::M:System.Threading.Monitor.Enter"
                        : string.Empty,
            MemberDocumentationId = isObject
                ? "M:Subject.#ctor"
                : isThrow
                    ? "M:System.InvalidOperationException.#ctor"
                    : isMonitor
                        ? "M:System.Threading.Monitor.Enter(System.Object)"
                        : null,
            TypeIdentity = isObject
                ? "Assembly::Subject"
                : isArray
                    ? "Assembly::System.Object[]"
                    : isThrow
                        ? InvalidOperationExceptionIdentity
                        : "Assembly::T:System.Threading.Monitor",
            TypeDocumentationId = isObject
                ? "T:Subject"
                : isArray
                    ? "T:System.Object[]"
                    : isThrow
                        ? "T:System.InvalidOperationException"
                        : "T:System.Threading.Monitor",
            ScalarOperands = [],
            ExactExceptionTypeHierarchy = isThrow
                ? [ExceptionIdentity, InvalidOperationExceptionIdentity]
                : [],
            Location = location,
            SourceTreeOrdinal = 0,
            SourceTreePath = snapshot.Path,
            SourceTreeSha256 = snapshot.Sha256,
            SourceLineMapSha256 = snapshot.LineMapSha256
        };
        var detail = isObject || isMonitor
            ? effectEvent.MemberDocumentationId!
            : effectEvent.TypeDocumentationId!;
        var evidence = new CompilerEffectClaimArtifact
        {
            ClaimId = ClaimId,
            ContractKind =
                WorkerEffectContractKind.ZeroAllocations,
            Outcome = WorkerClaimOutcome.Refuted,
            Reason = WorkerClaimReason.None,
            Certainty =
                WorkerEffectEvidenceCertainty.DefiniteViolation,
            Constraint = new CompilerEffectConstraintArtifact(),
            Witness = new WorkerEffectViolationWitness
            {
                Kind = isObject
                    ? AllocationWitnessKinds.Managed[0]
                    : isArray
                        ? AllocationWitnessKinds.Managed[1]
                        : isThrow
                            ? "explicit-throw"
                            : isMonitor
                                ? "synchronization-call"
                                : "synchronization-lock",
                Detail = detail,
                Effects = isObject || isArray
                    ? WorkerEffectSet.Allocates
                    : isThrow
                        ? WorkerEffectSet.Throws
                        : WorkerEffectSet.Synchronizes,
                Capabilities = isLock || isMonitor
                    ? WorkerEffectCapabilitySet.Synchronization
                    : WorkerEffectCapabilitySet.None,
                ExactExceptionTypeHierarchy =
                    [.. effectEvent.ExactExceptionTypeHierarchy],
                Location = Copy(location)
            },
            Replay = new CompilerEffectReplayArtifact
            {
                PathKind =
                    CompilerEffectReplayPathKind.Unconditional,
                Events = [effectEvent]
            },
            Evidence = "test-allocation-replay"
        };
        CompilerEffectClaimArtifactCodec.Seal(evidence);

        var entry = new WorkerCallableManifestEntry
        {
            CallableId = CallableId,
            SelectedFeatures = [WorkerSelectedFeature.Effects],
            SelectionReasons = [
                WorkerSelectionReason.ExplicitAnnotation
            ],
            Location = Location(),
            ClaimIds = [ClaimId]
        };
        var target = new CompilerCallablePreparation(
            new IrFactory(),
            entry,
            [],
            [],
            WorkerClaimReason.None,
            CompilerPreparedBody.Trivial())
        {
            EffectClaims = [evidence],
            Compilation = new CompilerCompilationSnapshot
            {
                SyntaxTrees = [
                    snapshot
                ]
            }
        };
        return new ReplayFixture(target, evidence, effectEvent);
    }

    private static void AssertRefuted(
        WorkerEffectViolationWitness expected,
        WorkerClaimResult actual,
        bool requireDistinctWitness = false)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual.Outcome, Is.EqualTo(
                WorkerClaimOutcome.Refuted));
            Assert.That(actual.Reason, Is.EqualTo(
                WorkerClaimReason.None));
            Assert.That(actual.EffectCertainty, Is.EqualTo(
                WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(actual.EffectWitness, Is.Not.Null);
            if (requireDistinctWitness)
            {
                Assert.That(actual.EffectWitness, Is.Not.SameAs(expected));
            }
            AssertWitnessesEqual(expected, actual.EffectWitness!);
        }
    }

    private static void AssertWitnessesEqual(
        WorkerEffectViolationWitness expected,
        WorkerEffectViolationWitness actual)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
            Assert.That(actual.Detail, Is.EqualTo(expected.Detail));
            Assert.That(actual.Effects, Is.EqualTo(expected.Effects));
            Assert.That(
                actual.Capabilities,
                Is.EqualTo(expected.Capabilities));
            Assert.That(
                actual.ExactExceptionTypeHierarchy,
                Is.EqualTo(
                    expected.ExactExceptionTypeHierarchy));
            Assert.That(actual.Location.Path, Is.EqualTo(
                expected.Location.Path));
            Assert.That(actual.Location.Start, Is.EqualTo(
                expected.Location.Start));
            Assert.That(actual.Location.Length, Is.EqualTo(
                expected.Location.Length));
            Assert.That(actual.Location.Line, Is.EqualTo(
                expected.Location.Line));
            Assert.That(actual.Location.Column, Is.EqualTo(
                expected.Location.Column));
        }
    }

    private static WorkerSourceLocation Location()
    {
        return new WorkerSourceLocation
        {
            Path = "MappedSubject.cs",
            Start = 10,
            Length = 12,
            Line = 7,
            Column = 5
        };
    }

    private static WorkerSourceLocation Copy(
        WorkerSourceLocation source)
    {
        return new WorkerSourceLocation
        {
            Path = source.Path,
            Start = source.Start,
            Length = source.Length,
            Line = source.Line,
            Column = source.Column
        };
    }

    private const string CallableId = "M:Subject.Allocate";
    private const string ClaimId = "spc1:allocation";
    private static readonly string TreeSha256 =
        new('1', 64);
    private const string ArgumentExceptionIdentity =
        "Assembly::T:System.ArgumentException";
    private const string ExceptionIdentity =
        "Assembly::T:System.Exception";
    private const string InvalidOperationExceptionIdentity =
        "Assembly::T:System.InvalidOperationException";

    private sealed record ReplayFixture(
        CompilerCallablePreparation Target,
        CompilerEffectClaimArtifact Evidence,
        CompilerEffectReplayEventArtifact Event);
}
