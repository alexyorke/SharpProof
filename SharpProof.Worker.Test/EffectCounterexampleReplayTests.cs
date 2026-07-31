using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EffectCounterexampleReplayTests
{
    [Test]
    public void UnconditionalObjectAndArrayAllocationsAreIndependentlyConfirmed()
    {
        foreach (var kind in new[]
                 {
                     CompilerEffectReplayEventKind.ManagedObjectAllocation,
                     CompilerEffectReplayEventKind.ManagedArrayAllocation
                 })
        {
            var fixture = CreateFixture(kind);
            var result = EffectClaimResultAssembler.Assemble(
                fixture.Target,
                fixture.Evidence);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Outcome,
                    Is.EqualTo(WorkerClaimOutcome.Refuted),
                    kind.ToString());
                Assert.That(
                    result.Reason,
                    Is.EqualTo(WorkerClaimReason.None),
                    kind.ToString());
                Assert.That(
                    result.EffectCertainty,
                    Is.EqualTo(
                        WorkerEffectEvidenceCertainty.DefiniteViolation),
                    kind.ToString());
                Assert.That(result.EffectWitness, Is.Not.Null);
                Assert.That(
                    result.EffectWitness,
                    Is.Not.SameAs(fixture.Evidence.Witness));
                AssertWitnessesEqual(
                    fixture.Evidence.Witness!,
                    result.EffectWitness!);
            }
        }
    }

    [TestCase("constraint-hash")]
    [TestCase("event-order")]
    [TestCase("path-kind")]
    [TestCase("tree-ordinal")]
    [TestCase("tree-identity")]
    [TestCase("tree-span")]
    [TestCase("operation-identity")]
    [TestCase("mapped-location")]
    public void StructurallyMalformedReplayEvidenceIsRejected(
        string tampering)
    {
        var fixture = CreateFixture(
            CompilerEffectReplayEventKind.ManagedObjectAllocation);
        switch (tampering)
        {
            case "constraint-hash":
                fixture.Evidence.Replay!.ConstraintSha256 =
                    new string('b', 64);
                break;
            case "event-order":
                fixture.Event.Ordinal = 1;
                CompilerEffectClaimArtifactCodec.Seal(
                    fixture.Evidence);
                break;
            case "path-kind":
                fixture.Evidence.Replay!.PathKind =
                    CompilerEffectReplayPathKind.Unspecified;
                CompilerEffectClaimArtifactCodec.Seal(
                    fixture.Evidence);
                break;
            case "tree-ordinal":
                fixture.Event.SyntaxTreeOrdinal = 1;
                CompilerEffectClaimArtifactCodec.Seal(
                    fixture.Evidence);
                break;
            case "tree-identity":
                fixture.Event.SyntaxTreeSha256 =
                    new string('c', 64);
                CompilerEffectClaimArtifactCodec.Seal(
                    fixture.Evidence);
                break;
            case "tree-span":
                fixture.Event.SyntaxStart = 95;
                fixture.Event.SyntaxLength = 10;
                fixture.Event.Location.Start = 95;
                fixture.Event.Location.Length = 10;
                CompilerEffectClaimArtifactCodec.Seal(
                    fixture.Evidence);
                break;
            case "operation-identity":
                fixture.Event.OperationIdentitySha256 =
                    new string('d', 64);
                break;
            case "mapped-location":
                fixture.Event.Location.Start++;
                CompilerEffectClaimArtifactCodec.Seal(
                    fixture.Evidence);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(tampering));
        }

        Assert.Throws<InvalidDataException>((Action)(() =>
            EffectClaimResultAssembler.Assemble(
                fixture.Target,
                fixture.Evidence)));
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
            fixture.Evidence);

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
        CompilerEffectClaimArtifactCodec.Seal(fixture.Evidence);

        var result = EffectClaimResultAssembler.Assemble(
            fixture.Target,
            fixture.Evidence);

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
            fixture.Evidence);

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

    [Test]
    public void WorkerOwnsCanonicalReplayHashing()
    {
        var fixture = CreateFixture(
            CompilerEffectReplayEventKind.ManagedObjectAllocation);
        var replay = fixture.Evidence.Replay!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                EffectCounterexampleReplayer
                    .ComputeConstraintIdentity(
                        fixture.Evidence.ContractKind,
                        fixture.Evidence.Constraint),
                Is.EqualTo(replay.ConstraintSha256));
            Assert.That(
                EffectCounterexampleReplayer
                    .ComputeOperationIdentity(fixture.Event),
                Is.EqualTo(
                    fixture.Event.OperationIdentitySha256));
        }

        var constraintIdentity = replay.ConstraintSha256;
        fixture.Evidence.Constraint.AllowedEffects =
            WorkerEffectSet.Allocates;
        Assert.That(
            EffectCounterexampleReplayer.ComputeConstraintIdentity(
                fixture.Evidence.ContractKind,
                fixture.Evidence.Constraint),
            Is.Not.EqualTo(constraintIdentity));

        var operationIdentity =
            fixture.Event.OperationIdentitySha256;
        fixture.Event.TypeIdentity += ":tampered";
        Assert.That(
            EffectCounterexampleReplayer.ComputeOperationIdentity(
                fixture.Event),
            Is.Not.EqualTo(operationIdentity));

        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SharpProof.Worker",
            "EffectCounterexampleReplayer.cs"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                source,
                Does.Not.Contain(
                    "CompilerEffectClaimArtifactCodec." +
                    "ComputeConstraintSha256"));
            Assert.That(
                source,
                Does.Not.Contain(
                    "CompilerEffectClaimArtifactCodec." +
                    "ComputeReplayOperationSha256"));
        }
    }

    [Test]
    public void CanceledReplayDoesNotPoisonTheNextReplay()
    {
        foreach (var kind in new[]
                 {
                     CompilerEffectReplayEventKind
                         .ManagedObjectAllocation,
                     CompilerEffectReplayEventKind
                         .ManagedArrayAllocation
                 })
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
                fixture.Evidence);
            Assert.That(
                recovered.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted),
                kind.ToString());
        }
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
                    fixture.Evidence);
            })));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                results.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                results.Count(static result =>
                    result.EffectWitness?.Kind ==
                    "managed-allocation"),
                Is.EqualTo(16));
            Assert.That(
                results.Count(static result =>
                    result.EffectWitness?.Kind ==
                    "managed-array-allocation"),
                Is.EqualTo(16));
        }
    }

    [Test]
    public void ReplayedEffectRefutationsRemainNoncacheable()
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
            fixture.Evidence);
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
                    manifest,
                    [fixture.Target]),
                Is.False);
        }
    }

    private static ReplayFixture CreateFixture(
        CompilerEffectReplayEventKind kind)
    {
        var location = Location();
        var isObject =
            kind ==
            CompilerEffectReplayEventKind.ManagedObjectAllocation;
        var effectEvent = new CompilerEffectReplayEventArtifact
        {
            Ordinal = 0,
            Kind = kind,
            SyntaxTreeOrdinal = 0,
            SyntaxTreeSha256 = TreeSha256,
            SyntaxStart = location.Start,
            SyntaxLength = location.Length,
            MemberIdentity = isObject
                ? "Assembly::M:Subject.#ctor"
                : string.Empty,
            MemberDocumentationId = isObject
                ? "M:Subject.#ctor"
                : null,
            TypeIdentity = isObject
                ? "Assembly::Subject"
                : "Assembly::System.Object[]",
            TypeDocumentationId = isObject
                ? "T:Subject"
                : "T:System.Object[]",
            ScalarOperands = [],
            ExactExceptionTypeHierarchy = [],
            Location = location
        };
        var detail = isObject
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
                    ? "managed-allocation"
                    : "managed-array-allocation",
                Detail = detail,
                Effects = WorkerEffectSet.Allocates,
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
                    new CompilerSyntaxTreeSnapshot
                    {
                        Path = "Subject.cs",
                        Sha256 = TreeSha256,
                        TextLength = 100
                    }
                ]
            }
        };
        return new ReplayFixture(target, evidence, effectEvent);
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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(
                 TestContext.CurrentContext.TestDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "SharpProof.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the SharpProof repository root.");
    }

    private const string CallableId = "M:Subject.Allocate";
    private const string ClaimId = "spc1:allocation";
    private static readonly string TreeSha256 =
        new('1', 64);

    private sealed record ReplayFixture(
        CompilerCallablePreparation Target,
        CompilerEffectClaimArtifact Evidence,
        CompilerEffectReplayEventArtifact Event);
}
