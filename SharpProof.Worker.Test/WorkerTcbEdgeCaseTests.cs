using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Dataflow;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerTcbEdgeCaseTests
{
    private const string CacheFileSuffix = VerificationCache.CacheFileSuffix;

    [TestCase(
        BackendFailureReason.Timeout,
        WorkerClaimReason.MethodTimeout)]
    [TestCase(
        BackendFailureReason.UnsupportedEncoding,
        WorkerClaimReason.UnsupportedExpression)]
    public async Task BackendAbstentionsMapToAccountableClaimReasons(
        BackendFailureReason backendReason,
        WorkerClaimReason expectedReason)
    {
        using var project = new ShadowTestProject(NativeTrivialSource);
        var results = await VerifyNativeClaimsAsync(project.Snapshot.Callables.Single(),
            new FixedBackend(BackendCheckResult.Unknown(backendReason)), new WorkerBudgets());
        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0].Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(results[0].Reason, Is.EqualTo(expectedReason));
    }
    [TestCase(MalformedBodyKind.MissingAssignmentSource)]
    [TestCase(MalformedBodyKind.UnboundCall)]
    [TestCase(MalformedBodyKind.MissingBranchCondition)]
    [TestCase(MalformedBodyKind.MissingReturnValue)]
    [TestCase(MalformedBodyKind.UnsupportedInstruction)]
    public void MalformedProgramBodiesFailClosedBeforeBackendInvocation(MalformedBodyKind kind)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    if (value > 0) return value;
                    return value;
                }
            }
            """);
        var instructions = artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block.Instructions).ToArray();
        switch (kind)
        {
            case MalformedBodyKind.MissingAssignmentSource:
                instructions.First(instruction => instruction.Kind == IrInstructionKind.Assign).B = -1;
                break;
            case MalformedBodyKind.UnboundCall:
                var call = instructions.First(instruction => instruction.Kind == IrInstructionKind.Return);
                call.Kind = IrInstructionKind.Call;
                call.B = int.MaxValue;
                break;
            case MalformedBodyKind.MissingBranchCondition:
                instructions.First(instruction => instruction.Kind == IrInstructionKind.Branch).A = -1;
                break;
            case MalformedBodyKind.MissingReturnValue:
                instructions.First(instruction => instruction.Kind == IrInstructionKind.Return).A = -1;
                break;
            case MalformedBodyKind.UnsupportedInstruction:
                instructions[0].Kind = (IrInstructionKind)int.MaxValue;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        RejectMalformedArtifact(artifact);
    }
    [Test]
    public void ContractClaimOrderMismatchFailsClosedBeforeBackendInvocation()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(NativeTrivialSource);
        artifact.Callables.Single().Total!.Clauses.Single().ClaimId = "foreign-claim";
        RejectMalformedArtifact(artifact);
    }
    [Test]
    public void MoreEnsuresClausesThanClaimIdsFailsClosedWithoutIndexing()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(NativeTrivialSource);
        var total = artifact.Callables.Single().Total!;
        total.Clauses = [total.Clauses.Single(), total.Clauses.Single()];
        RejectMalformedArtifact(artifact);
    }
    [Test]
    public async Task MissingPreparedBodyFailsClosedBeforeBackendInvocation()
    {
        using var project = new ShadowTestProject(NativeTrivialSource);
        var target = project.Snapshot.Callables.Single() with { Total = null };
        var backend = new ThrowingBackend("Missing typed body reached the backend.");
        var results = await VerifyNativeClaimsAsync(target, backend, new WorkerBudgets());
        Assert.That(backend.CallCount, Is.Zero);
        Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }
    [Test]
    public async Task DeepPreconditionFailsClosedBeforeBackendInvocation()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures(true);
                    return value;
                }
            }
            """);
        var target = project.Snapshot.Callables.Single();
        var backend = new ThrowingBackend("Deep typed precondition reached the backend.");
        var budgets = new WorkerBudgets { MaximumExpressionDepth = 1 };
        var results = await VerifyNativeClaimsAsync(target, backend, budgets);
        var entry = await TotalCallableVerifier.VerifyEntryAsync(target, budgets, CancellationToken.None,
            backend, CreateResourceBudget());
        Assert.That(backend.CallCount, Is.Zero);
        Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
        Assert.That(entry.IsUnknown, Is.True);
        Assert.That(entry.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
    }
    [Test]
    public void EmptySourceIntervalFailsClosedBeforeBackendInvocation()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) { Contract.Ensures(true); return value; }
            }
            """);
        var parameter = artifact.Callables.Single().Variables.Single(variable => variable.Role == CompilerVariableRole.Parameter);
        parameter.Minimum = 1;
        parameter.Maximum = 0;
        RejectMalformedArtifact(artifact);
    }
    [Test]
    public async Task ResourceCounterCrossingMethodLimitDiscardsBackendOutcome()
    {
        using var project = new ShadowTestProject(NativeTrivialSource);
        long consumed = 0;
        var backend = new ResourceConsumingBackend(() => consumed = 11,
            BackendCheckResult.Unsatisfiable([]));
        var results = await VerifyNativeClaimsAsync(project.Snapshot.Callables.Single(), backend,
            new WorkerBudgets { QueryRlimit = 10, MethodRlimit = 10 }, () => Volatile.Read(ref consumed));
        Assert.That(backend.CallCount, Is.EqualTo(1));
        Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task SemanticPreconditionContradictionIsExplicitVacuityEvidence(bool fullCallable)
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value > 0);
                    Contract.Requires(value < 0);
                    Contract.Ensures(false);
                    return value;
                }
            }
            """);
        if (fullCallable)
        {
            var result = await VerifyNativeSourceAsync(project);
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.ContradictoryPreconditions));
            Assert.That(result.ProofCore, Is.Not.Empty);
        }
        else
        {
            var result = await TotalCallableVerifier.VerifyEntryAsync(project.Snapshot.Callables.Single(),
                project.Request.Budgets, CancellationToken.None);
            Assert.That(result.IsContradictory, Is.True);
        }
    }

    [Test]
    public async Task ResultSourceDomainCannotCreatePreconditionVacuity()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static byte Target() { Contract.Ensures(false); return byte.MaxValue; }
            }
            """);
        var result = await VerifyNativeSourceAsync(project);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UserAssumeCreatesNormalCompletionVacuity(bool hasPrecondition)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    {{(hasPrecondition ? "Contract.Requires(value > 0);" : "")}}
                    Contract.Assume({{(hasPrecondition ? "value < 0" : "false")}});
                    Contract.Ensures(false);
                    return value;
                }
            }
            """);
        var result = await VerifyNativeSourceAsync(project);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.NoModeledNormalReturn));
        Assert.That(result.Assumptions.Single(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume).Used, Is.True);
    }

    [TestCase(true, WorkerClaimOutcome.Proven)]
    [TestCase(false, WorkerClaimOutcome.Refuted)]
    public async Task SatisfiablePreconditionPreservesPostconditionVerdict(bool postcondition, WorkerClaimOutcome expectedOutcome)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures({{(postcondition ? "true" : "false")}});
                    return value;
                }
            }
            """);
        var result = await VerifyNativeSourceAsync(project);
        Assert.That(result.Outcome, Is.EqualTo(expectedOutcome));
        Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
    }

    [Test]
    public async Task UnknownPreconditionSatisfiabilityCannotBecomeVacuousProof()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures(true);
                    return value;
                }
            }
            """);
        var backend = new ScriptedBackend(BackendCheckResult.Unknown(BackendFailureReason.InfrastructureFailure));
        var results = await VerifyNativeClaimsAsync(project.Snapshot.Callables.Single(), backend, project.Request.Budgets);
        Assert.That(backend.CallCount, Is.EqualTo(1));
        Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.InfrastructureFailure));
        Assert.That(results.Single().Vacuity, Is.EqualTo(WorkerVacuityKind.None));
    }

    [TestCase(false, false, WorkerVacuityKind.NoModeledNormalReturn)]
    [TestCase(true, false, WorkerVacuityKind.None)]
    [TestCase(false, true, WorkerVacuityKind.NoModeledNormalReturn)]
    public async Task NormalCompletionVacuityMatchesModeledPath(bool nonzero, bool assumeCompletion, WorkerVacuityKind expectedVacuity)
    {
        using var project = new ShadowTestProject(DivisionSource(nonzero, assumeCompletion));
        var result = await VerifyNativeSourceAsync(project);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(result.Vacuity, Is.EqualTo(expectedVacuity));
    }

    [Test]
    public async Task UnknownNormalCompletionSatisfiabilityCannotBecomeProof()
    {
        using var project = new ShadowTestProject(DivisionSource(nonzero: true, assumeCompletion: false));
        using var backend = new UnknownNormalCompletionBackend();
        var results = await VerifyNativeClaimsAsync(project.Snapshot.Callables.Single(), backend, project.Request.Budgets);
        Assert.That(backend.CallCount, Is.EqualTo(2));
        Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.InfrastructureFailure));
        Assert.That(results.Single().Vacuity, Is.EqualTo(WorkerVacuityKind.None));
    }

    private static string DivisionSource(bool nonzero, bool assumeCompletion)
    {
        return $$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value {{(nonzero ? "!=" : "==")}} 0);
                    {{(assumeCompletion ? "Contract.Assume(1 / value == 1 / value);" : "")}}
                    Contract.Ensures({{(nonzero ? "true" : "false")}});
                    return 1 / value;
                }
            }
            """;
    }

    private static async Task<WorkerClaimResult> VerifyNativeSourceAsync(ShadowTestProject project)
    {
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        return response.ClaimResults.Single();
    }
    [Test]
    public void UnknownCompilerClauseKindIsRejectedExhaustively()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(NativeTrivialSource);
        artifact.Callables.Single().Total!.Clauses.Single().Kind = (CompilerContractKind)int.MaxValue;
        RejectMalformedArtifact(artifact);
    }

    private static void RejectMalformedArtifact(CompilerManifestArtifact artifact)
    {
        Assert.Throws<JsonException>(new Action(() =>
        {
            var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
            CompilerManifestArtifactJson.DeserializePrepared(json, out _);
        }));
    }
    [Test]
    public void MalformedBackendOutcomeBecomesTypedUnknown()
    {
        var result = CallableClaimResultAssembler.FromOutcome(
            CreateTrivialTarget(),
            0,
            outcome: null!,
            new Dictionary<ProofJustification, string>(),
            new Dictionary<ProofJustification, string>(),
            WorkerVacuityKind.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.MalformedBackendResult));
        }
    }

    [Test]
    public void ClaimAssumptionsDoNotAliasManifestOrSiblingEvidence()
    {
        var target = CreateTrivialTarget();
        target.Entry.Assumptions =
        [
            new WorkerAssumptionEvidence
            {
                Id = "assumption",
                Kind = WorkerAssumptionKind.UserAssume
            }
        ];
        var first = CallableClaimResultAssembler.Unknown(
            target,
            0,
            WorkerClaimReason.UnsupportedExpression);
        var sibling = CallableClaimResultAssembler.Unknown(
            target,
            0,
            WorkerClaimReason.UnsupportedExpression);

        first.Assumptions[0].Id = "mutated";
        first.Assumptions[0].Used = true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.Assumptions[0],
                Is.Not.SameAs(target.Entry.Assumptions[0]));
            Assert.That(first.Assumptions[0], Is.Not.SameAs(sibling.Assumptions[0]));
            Assert.That(target.Entry.Assumptions[0].Id, Is.EqualTo("assumption"));
            Assert.That(target.Entry.Assumptions[0].Used, Is.False);
            Assert.That(sibling.Assumptions[0].Id, Is.EqualTo("assumption"));
            Assert.That(sibling.Assumptions[0].Used, Is.False);
        }
    }

    [Test]
    public void ProvenOutcomeWithUnmappedEvidenceFailsClosed()
    {
        var factory = new IrFactory();
        var outcome = CreateProvenOutcome([
            new LoweredJustification(factory.CreateOperation("unmapped"))
        ]);

        var result = CallableClaimResultAssembler.FromOutcome(
            CreateTrivialTarget(),
            0,
            outcome,
            new Dictionary<ProofJustification, string>(),
            new Dictionary<ProofJustification, string>(),
            WorkerVacuityKind.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.MalformedBackendResult));
            Assert.That(result.ProofCore, Is.Empty);
        }
    }

    [Test]
    public void ProvenOutcomeWithEmptyEvidenceCoreRemainsValid()
    {
        var result = CallableClaimResultAssembler.FromOutcome(
            CreateTrivialTarget(),
            0,
            CreateProvenOutcome([]),
            new Dictionary<ProofJustification, string>(),
            new Dictionary<ProofJustification, string>(),
            WorkerVacuityKind.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(result.ProofCore, Is.Empty);
        }
    }

    [Test]
    public void CounterexampleModelFormattingCoversEveryIrValueKind()
    {
        var factory = new IrFactory();
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var values = new[] {
            (Variable: factory.CreateVariable("boolean", factory.BooleanType),
                Label: "boolean", Value: factory.CreateBooleanValue(true)),
            (Variable: factory.CreateVariable("integer", factory.IntegerType),
                Label: "integer", Value: factory.CreateIntegerValue(-1)),
            (Variable: factory.CreateVariable("string", factory.StringType),
                Label: "string", Value: factory.CreateStringValue("text")),
            (Variable: factory.CreateVariable("null", factory.StringType),
                Label: "null", Value: factory.CreateNullValue(factory.StringType)),
            (Variable: factory.CreateVariable("reference", factory.ObjectType),
                Label: "reference", Value: factory.CreateReferenceValue(factory.ObjectType, new object())),
            (Variable: factory.CreateVariable("sequence", sequenceType),
                Label: "sequence", Value: factory.CreateSequenceValue(
                    sequenceType,
                    [factory.CreateIntegerValue(1)]))
        };
        var variables = values.Select((item, index) =>
            new CompilerCanonicalVariable(
                CompilerVariableRole.Parameter,
                index,
                item.Variable,
                null,
                null,
                item.Label)).ToImmutableArray();
        var assignments = values.ToImmutableDictionary(
            static item => item.Variable,
            static item => item.Value);
        var validatedModel = (ValidatedModel)typeof(ValidatedModel)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single()
            .Invoke([assignments]);
        var refuted = (RefutedOutcome)typeof(RefutedOutcome)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single()
            .Invoke([validatedModel]);
        var target = CreateTarget(
            factory,
            factory.Boolean(false),
            variables,
            CompilerPreparedBody.Trivial());

        var result = CallableClaimResultAssembler.FromOutcome(
            target,
            0,
            refuted,
            new Dictionary<ProofJustification, string>(),
            new Dictionary<ProofJustification, string>(),
            WorkerVacuityKind.None);

        (string Variable, string Kind, string Value)[] expected = [
            ("boolean", "Boolean", "true"),
            ("integer", "Integer", "-1"),
            ("null", "Null", "null"),
            ("reference", "Reference", "<opaque>"),
            ("sequence", "Sequence", "<opaque>"),
            ("string", "String", "text")
        ];
        Assert.That(
            result.Model.Select(static value => (value.Variable, value.Kind, value.Value)),
            Is.EqualTo(expected));
    }

    [Test]
    public void TrivialReplayRejectsAResultVariableWithoutAProgram()
    {
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

        var reason = CallableReplayTestHarness.Replay(
            target,
            0,
            ImmutableDictionary<IrVarId, IrValue>.Empty,
            target.Clauses);

        Assert.That(
            reason,
            Is.EqualTo(WorkerClaimReason.CounterexampleReplayFailed));
    }

    [TestCase(
        false,
        TestName = "CacheRejectsAHashedPayloadWithNullCallableResults")]
    [TestCase(
        true,
        TestName = "CacheRejectsPayloadSealedForADifferentManifest")]
    public async Task CacheRejectsMalformedPayload(bool differentManifest)
    {
        using var temporaryDirectory = new TempDirectory("worker-cache-edge-");
        var directory = temporaryDirectory.FullName;
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        var inputHash = new string(differentManifest ? 'b' : 'a', 64);
        await WriteCacheEnvelopeAsync(
            directory,
            inputHash,
            differentManifest ? new string('c', 64) : manifest.Hash,
            differentManifest ? [] : null,
            []);
        var cache = new VerificationCache(directory, 1024 * 1024);

        var response = await cache.TryReadAsync(
            inputHash,
            manifest,
            new WorkerBudgets(),
            CancellationToken.None);

        Assert.That(response, Is.Null);
    }

    [Test]
    public async Task CacheRejectsOversizedJsonBeforeDeserialization()
    {
        using var temporaryDirectory = new TempDirectory("worker-cache-size-");
        var directory = temporaryDirectory.FullName;
        var inputHash = new string('d', 64);
        var path = Path.Combine(
            directory,
            inputHash + CacheFileSuffix);
        await File.WriteAllBytesAsync(
            path, new byte[WorkerProtocolJson.MaximumJsonBytes + 1]);
        var cache = new VerificationCache(
            directory, WorkerProtocolJson.MaximumJsonBytes * 2L);
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);

        var response = await cache.TryReadAsync(
            inputHash,
            manifest,
            new WorkerBudgets(),
            CancellationToken.None);

        Assert.That(response, Is.Null);
    }

    [Test]
    public async Task CacheWriteLimitsAreRejectedBeforePublication()
    {
        var inputHash = new string('e', 64);
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            ClaimResults = [new WorkerClaimResult
            {
                ProofCore = [new string('x', WorkerProtocolJson.MaximumJsonBytes)]
            }]
        };
        foreach (var maximumBytes in new[]
        {
            1L,
            (long)WorkerProtocolJson.MaximumJsonBytes * 2
        })
        {
            using var temporaryDirectory = new TempDirectory(
                "worker-cache-write-size-");
            var directory = temporaryDirectory.FullName;
            var cache = new VerificationCache(directory, maximumBytes);
            Assert.That(
                await cache.TryWriteAsync(
                    response,
                    inputHash,
                    manifest,
                    CancellationToken.None),
                Is.False,
                maximumBytes.ToString(CultureInfo.InvariantCulture));
            Assert.That(
                Directory.GetFiles(directory, "*" + CacheFileSuffix),
                Is.Empty,
                maximumBytes.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static Task WriteCacheEnvelopeAsync(
        string directory,
        string inputHash,
        string manifestHash,
        WorkerCallableResult[]? callableResults,
        WorkerClaimResult[] claimResults)
    {
        var entry = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = WorkerCacheVersions.Current,
                InputHash = inputHash,
                ManifestHash = manifestHash,
                CallableResults = callableResults,
                ClaimResults = claimResults
            },
            WorkerProtocolJson.Options);
        return File.WriteAllTextAsync(
            Path.Combine(
                directory,
                inputHash + CacheFileSuffix),
            entry);
    }

    private const string NativeTrivialSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static void Target() { Contract.Ensures(true); }
        }
        """;

    private static async Task<ImmutableArray<WorkerClaimResult>> VerifyNativeClaimsAsync(
        CompilerCallablePreparation target, ISmtBackend backend, WorkerBudgets budgets,
        Func<long>? readConsumedResources = null)
    {
        using var projectBoundary = new CancellationTokenSource();
        var result = await CallableVerificationPolicy.VerifyNativeTargetAsync(backend, target, budgets,
            readConsumedResources, WorkerBudgets.DefaultMethodWallTimeMilliseconds,
            projectBoundary, CancellationToken.None);
        return result.Claims;
    }
    private static CompilerCallablePreparation CreateTrivialTarget()
    {
        var factory = new IrFactory();
        return CreateTarget(
            factory,
            factory.Boolean(true),
            [],
            CompilerPreparedBody.Trivial());
    }

    private static ProvenOutcome CreateProvenOutcome(
        ImmutableArray<ProofJustification> core)
    {
        return (ProvenOutcome)typeof(ProvenOutcome)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single()
            .Invoke([core]);
    }

    private static CompilerCallablePreparation CreateTarget(
        IrFactory factory,
        IrTerm postcondition,
        ImmutableArray<CompilerCanonicalVariable> variables,
        CompilerPreparedBody? body)
    {
        return CreateTarget(
            factory,
            [new CompilerPreparedClause(
                CompilerContractKind.Ensures,
                postcondition,
                CompilerContractEvidence.CompilerBoundInvocation,
                "claim",
                null)],
            variables,
            body);
    }

    private static CompilerCallablePreparation CreateTarget(
        IrFactory factory,
        ImmutableArray<CompilerPreparedClause> clauses,
        ImmutableArray<CompilerCanonicalVariable> variables,
        CompilerPreparedBody? body)
    {
        return new(
            factory,
            new WorkerCallableManifestEntry
            {
                CallableId = "M:Test.Subject.Verify",
                ClaimIds = ["claim"]
            },
            clauses,
            variables,
            WorkerClaimReason.None,
            body);
    }

    private static MethodResourceBudget CreateResourceBudget()
    {
        return new(
            null,
            WorkerBudgets.DefaultQueryRlimit,
            WorkerBudgets.DefaultMethodRlimit);
    }

    private sealed class FixedBackend(BackendCheckResult result)
        : ISmtBackend
    {
        private readonly BackendCheckResult _result = result;

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }

    private sealed class ResourceConsumingBackend(
        Action consume,
        BackendCheckResult result) : ISmtBackend
    {
        private readonly Action _consume = consume;
        private readonly BackendCheckResult _result = result;
        private int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            _consume();
            return Task.FromResult(_result);
        }
    }

    private sealed class ScriptedBackend(
        params BackendCheckResult[] results) : ISmtBackend
    {
        private readonly Queue<BackendCheckResult> _results = new(results);
        private int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class UnknownNormalCompletionBackend : ISmtBackend, IDisposable
    {
        private readonly NativeCallableBackend _backend;
        internal int CallCount { get; private set; }

        internal UnknownNormalCompletionBackend()
        {
            ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
            _backend = new NativeCallableBackend(new IrSmtBackendOptions(WorkerBudgets.DefaultQueryRlimit));
        }

        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return CallCount switch
            {
                1 => _backend.CheckAsync(query, cancellationToken),
                2 => Task.FromResult(BackendCheckResult.Unknown(BackendFailureReason.InfrastructureFailure)),
                _ => throw new AssertionException("A postcondition query followed unknown normal completion.")
            };
        }

        public void Dispose() { _backend.Dispose(); }
    }
    public enum MalformedBodyKind
    {
        MissingAssignmentSource,
        UnboundCall,
        MissingBranchCondition,
        MissingReturnValue,
        UnsupportedInstruction
    }
}
