using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Dataflow;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerTcbEdgeCaseTests {
    [TestCase(
        BackendFailureReason.Timeout,
        WorkerClaimReason.MethodTimeout)]
    [TestCase(
        BackendFailureReason.UnsupportedEncoding,
        WorkerClaimReason.UnsupportedExpression)]
    public async Task BackendAbstentionsMapToAccountableClaimReasons(
        BackendFailureReason backendReason,
        WorkerClaimReason expectedReason) {
        var verifier = new CallableVerifier(
            new FixedBackend(BackendCheckResult.Unknown(backendReason)),
            WorkerBudgets.DefaultMaximumExpressionDepth);

        var results = await verifier.VerifyAsync(
            CreateTrivialTarget(),
            CreateResourceBudget(),
            CancellationToken.None);

        using (Assert.EnterMultipleScope()) {
            Assert.That(results, Has.Length.EqualTo(1));
            Assert.That(
                results[0].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(results[0].Reason, Is.EqualTo(expectedReason));
        }
    }

    [TestCase(MalformedBodyKind.MissingAssignmentSource)]
    [TestCase(MalformedBodyKind.UnboundCall)]
    [TestCase(MalformedBodyKind.MissingBranchCondition)]
    [TestCase(MalformedBodyKind.MissingReturnValue)]
    [TestCase(MalformedBodyKind.UnsupportedInstruction)]
    public async Task MalformedProgramBodiesFailClosedBeforeBackendInvocation(
        MalformedBodyKind kind) {
        var backend = new UnexpectedBackend();
        var verifier = new CallableVerifier(
            backend,
            WorkerBudgets.DefaultMaximumExpressionDepth);

        var results = await verifier.VerifyAsync(
            CreateMalformedProgramTarget(kind),
            CreateResourceBudget(),
            CancellationToken.None);

        using (Assert.EnterMultipleScope()) {
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(results, Has.Length.EqualTo(1));
            Assert.That(
                results[0].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                results[0].Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    [Test]
    public void TrivialReplayRejectsAResultVariableWithoutAProgram() {
        var factory = new IrFactory();
        var result = factory.CreateVariable(
            "result",
            factory.IntegerType);
        var target = CreateTarget(
            factory,
            factory.Boolean(false),
            [new CompilerCanonicalVariable(
                CompilerVariableRole.Result,
                -1,
                result,
                null,
                null,
                "result")],
            CompilerPreparedBody.Trivial());

        var reason = CallableCounterexampleReplayer.Replay(
            target,
            0,
            ImmutableDictionary<IrVarId, IrValue>.Empty);

        Assert.That(
            reason,
            Is.EqualTo(WorkerClaimReason.CounterexampleReplayFailed));
    }

    [Test]
    public void NullResultFacetProjectsToNegativeNonNullEvidence() {
        var factory = new IrFactory();
        var result = factory.CreateVariable(
            "result",
            factory.ObjectType);

        var succeeded = SpecResultDomainProjection.TryCreate(
            factory,
            CreateTemplate(
                SpecValueType.Reference,
                SpecNullness.Null,
                SpecCardinality.NotApplicable),
            result,
            out var projection,
            out var evidence);

        using (Assert.EnterMultipleScope()) {
            Assert.That(succeeded, Is.True);
            Assert.That(projection.NonNullVariable, Is.Not.Null);
            Assert.That(projection.LengthVariable, Is.Null);
            Assert.That(evidence, Has.Length.EqualTo(1));
            Assert.That(evidence[0], Is.TypeOf<IrUnaryTerm>());
        }
    }

    [Test]
    public void NonEmptySequenceFacetProjectsToPositiveLengthEvidence() {
        var factory = new IrFactory();
        var result = factory.CreateVariable(
            "result",
            factory.GetOrCreateSequenceType(factory.IntegerType));

        var succeeded = SpecResultDomainProjection.TryCreate(
            factory,
            CreateTemplate(
                SpecValueType.Sequence,
                SpecNullness.NonNull,
                SpecCardinality.NonEmpty),
            result,
            out var projection,
            out var evidence);

        using (Assert.EnterMultipleScope()) {
            Assert.That(succeeded, Is.True);
            Assert.That(projection.NonNullVariable, Is.Not.Null);
            Assert.That(projection.LengthVariable, Is.Not.Null);
            Assert.That(evidence, Has.Length.EqualTo(2));
            Assert.That(evidence[1], Is.TypeOf<IrBinaryTerm>());
        }
    }

    [Test]
    public async Task CacheRejectsAHashedPayloadWithNullCallableResults() {
        var directory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "worker-cache-edge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var manifest = new WorkerClaimManifest();
            WorkerProtocolJson.SealManifest(manifest);
            var inputHash = new string('a', 64);
            var payload = JsonSerializer.Serialize(
                new {
                    ManifestHash = manifest.Hash,
                    CallableResults = (WorkerCallableResult[]?)null,
                    ClaimResults = Array.Empty<WorkerClaimResult>()
                },
                WorkerProtocolJson.Options);
            var payloadHash = string.Concat(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload))
                    .Select(static value => value.ToString(
                        "x2",
                        CultureInfo.InvariantCulture)));
            var envelope = JsonSerializer.Serialize(
                new {
                    SchemaVersion = WorkerCacheVersions.Current,
                    InputHash = inputHash,
                    PayloadHash = payloadHash,
                    Payload = payload
                },
                WorkerProtocolJson.Options);
            await File.WriteAllTextAsync(
                Path.Combine(directory, inputHash + ".json"),
                envelope);
            var cache = new VerificationCache(directory, 1024 * 1024);

            var response = await cache.TryReadAsync(
                inputHash,
                manifest,
                new WorkerBudgets(),
                CancellationToken.None);

            Assert.That(response, Is.Null);
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CompilerCallablePreparation CreateTrivialTarget() {
        var factory = new IrFactory();
        return CreateTarget(
            factory,
            factory.Boolean(true),
            [],
            CompilerPreparedBody.Trivial());
    }

    private static CompilerCallablePreparation CreateMalformedProgramTarget(
        MalformedBodyKind kind) {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        switch (kind) {
            case MalformedBodyKind.MissingAssignmentSource:
                var source = factory.CreateVariable(
                    "unbound-source",
                    factory.IntegerType);
                var assignmentTarget = factory.CreateVariable(
                    "assignment-target",
                    factory.IntegerType);
                builder.Assign(
                    entry,
                    factory.CreateOperation(),
                    assignmentTarget,
                    factory.Variable(source));
                builder.Return(
                    entry,
                    factory.CreateOperation(),
                    factory.Variable(assignmentTarget));
                break;
            case MalformedBodyKind.UnboundCall:
                var callTarget = factory.CreateVariable(
                    "call-target",
                    factory.IntegerType);
                var member = factory.GetOrCreateMember(
                    factory.CreateIdentity(),
                    factory.ObjectType,
                    "Opaque",
                    factory.IntegerType,
                    isStatic: true);
                builder.Call(
                    entry,
                    factory.CreateOperation(),
                    callTarget,
                    member,
                    null);
                builder.Return(
                    entry,
                    factory.CreateOperation(),
                    factory.Variable(callTarget));
                break;
            case MalformedBodyKind.MissingBranchCondition:
                var condition = factory.CreateVariable(
                    "unbound-condition",
                    factory.BooleanType);
                var whenTrue = builder.CreateBlock("when-true");
                var whenFalse = builder.CreateBlock("when-false");
                builder.Branch(
                    entry,
                    factory.CreateOperation(),
                    factory.Variable(condition),
                    whenTrue,
                    whenFalse);
                builder.Return(
                    whenTrue,
                    factory.CreateOperation(),
                    factory.Integer(1));
                builder.Return(
                    whenFalse,
                    factory.CreateOperation(),
                    factory.Integer(0));
                break;
            case MalformedBodyKind.MissingReturnValue:
                builder.Return(entry, factory.CreateOperation());
                break;
            case MalformedBodyKind.UnsupportedInstruction:
                builder.Assume(
                    entry,
                    factory.CreateOperation(),
                    factory.Boolean(true));
                builder.Return(
                    entry,
                    factory.CreateOperation(),
                    factory.Integer(0));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return CreateTarget(
            factory,
            factory.Boolean(true),
            [],
            CompilerPreparedBody.ProgramBody(
                builder.Build(),
                ImmutableDictionary<IrVarId, IrVarId>.Empty,
                ImmutableDictionary<
                    IrInstructionId,
                    CompilerPreparedSpecCall>.Empty));
    }

    private static CompilerCallablePreparation CreateTarget(
        IrFactory factory,
        IrTerm postcondition,
        ImmutableArray<CompilerCanonicalVariable> variables,
        CompilerPreparedBody body) =>
        new(
            factory,
            new WorkerCallableManifestEntry {
                CallableId = "M:Test.Subject.Verify",
                ClaimIds = ["claim"]
            },
            [new CompilerPreparedClause(
                CompilerContractKind.Ensures,
                postcondition,
                CompilerContractEvidence.CompilerBoundInvocation,
                "claim",
                null)],
            variables,
            WorkerClaimReason.None,
            body);

    private static MethodResourceBudget CreateResourceBudget() =>
        new(
            null,
            WorkerBudgets.DefaultQueryRlimit,
            WorkerBudgets.DefaultMethodRlimit);

    private static ApiSpecTemplate CreateTemplate(
        SpecValueType resultType,
        SpecNullness nullness,
        SpecCardinality cardinality) {
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Documented,
            "worker-tcb-edge-test");
        return ApiSpecTable.Create([
            new ApiSpecDeclaration(
                new ApiSpecTarget(
                    "test.tcb.result",
                    "M:Test.Tcb.Result",
                    "Test.Tcb",
                    SpecTargetMemberKind.Method,
                    "Result",
                    true,
                    0,
                    null,
                    [],
                    resultType),
                new ApiSpecFacets(
                    new SpecEffectFacet(SpecEffect.None, evidence),
                    new SpecAllocationFacet(
                        SpecAllocationBehavior.Unknown,
                        evidence),
                    new SpecThrowFacet(
                        SpecThrowBehavior.DoesNotThrow,
                        [],
                        evidence),
                    new SpecNullnessFacet(nullness, evidence),
                    new SpecCardinalityFacet(
                        cardinality,
                        null,
                        evidence)),
                [])
        ]).Templates.Single();
    }

    private sealed class FixedBackend(BackendCheckResult result)
        : ISmtBackend {
        private readonly BackendCheckResult _result = result;

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }

    private sealed class UnexpectedBackend : ISmtBackend {
        private int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken) {
            Interlocked.Increment(ref _callCount);
            throw new AssertionException(
                "Malformed input reached the backend.");
        }
    }

    public enum MalformedBodyKind {
        MissingAssignmentSource,
        UnboundCall,
        MissingBranchCondition,
        MissingReturnValue,
        UnsupportedInstruction
    }
}
