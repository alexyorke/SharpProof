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
using SharpProof.Specs;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerTcbEdgeCaseTests
{
    private const string CacheFileSuffix = VerificationCache.CacheFileSuffix;

    [Test]
    public async Task OrdinaryCacheMissReconcilesReducedCapacity()
    {
        using var directory = new TempDirectory(
            "sharpproof-cache-miss-capacity-");
        var oldest = Path.Combine(
            directory.FullName,
            new string('a', 64) + CacheFileSuffix);
        var newest = Path.Combine(
            directory.FullName,
            new string('b', 64) + CacheFileSuffix);
        await File.WriteAllTextAsync(oldest, new string('x', 100));
        await File.WriteAllTextAsync(newest, new string('y', 100));
        File.SetLastWriteTimeUtc(oldest, DateTime.UtcNow.AddMinutes(-1));
        File.SetLastWriteTimeUtc(newest, DateTime.UtcNow);

        var cache = new VerificationCache(directory.FullName, 150);
        var result = await cache.TryReadAsync(
            new string('c', 64),
            new WorkerClaimManifest { Claims = [] },
            [],
            new WorkerBudgets(),
            CancellationToken.None);

        Assert.That(result, Is.Null);
        Assert.That(
            Directory.GetFiles(directory.FullName, "*" + CacheFileSuffix),
            Is.EqualTo(new[] { newest }));
    }

    [Test]
    public void SymbolicLinkIsRejectedBeforeTraversal()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("The verifier host is Linux-only.");
        }

        var root = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "symlink-rejection-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);
        try
        {
            Action canonicalize = () =>
                LinuxPathIdentity.Canonicalize(
                    Path.Combine(link, "SharpProof", "cache"));
            Assert.Throws<ArgumentException>(canonicalize);
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

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
        var verifier = new CallableVerifier(
            new FixedBackend(BackendCheckResult.Unknown(backendReason)),
            WorkerBudgets.DefaultMaximumExpressionDepth);

        var results = await verifier.VerifyAsync(
            CreateTrivialTarget(),
            CreateResourceBudget(),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
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
        MalformedBodyKind kind)
    {
        var backend = new ThrowingBackend("Malformed input reached the backend.");
        var verifier = new CallableVerifier(
            backend,
            WorkerBudgets.DefaultMaximumExpressionDepth);

        var results = await verifier.VerifyAsync(
            CreateMalformedProgramTarget(kind),
            CreateResourceBudget(),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
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
    public async Task ContractClaimOrderMismatchFailsClosedBeforeBackendInvocation()
    {
        var factory = new IrFactory();
        var target = new CompilerCallablePreparation(
            factory,
            new WorkerCallableManifestEntry
            {
                CallableId = "M:Test.Subject.Verify",
                ClaimIds = ["manifest-claim"]
            },
            [new CompilerPreparedClause(
                CompilerContractKind.Ensures,
                factory.Boolean(true),
                CompilerContractEvidence.CompilerBoundInvocation,
                "lowered-claim",
                null)],
            [],
            WorkerClaimReason.None,
            CompilerPreparedBody.Trivial());
        var backend = new ThrowingBackend("Malformed input reached the backend.");

        var results = await new CallableVerifier(
            backend,
            WorkerBudgets.DefaultMaximumExpressionDepth).VerifyAsync(
                target,
                CreateResourceBudget(),
                CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(results, Has.Length.EqualTo(1));
            Assert.That(
                results[0].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                results[0].Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedContract));
        }
    }

    [Test]
    public async Task MoreEnsuresClausesThanClaimIdsFailsClosedWithoutIndexing()
    {
        var factory = new IrFactory();
        var target = new CompilerCallablePreparation(
            factory,
            new WorkerCallableManifestEntry
            {
                CallableId = "M:Test.Subject.Verify",
                ClaimIds = ["only-claim"]
            },
            [
                new CompilerPreparedClause(
                    CompilerContractKind.Ensures,
                    factory.Boolean(true),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    "only-claim",
                    null),
                new CompilerPreparedClause(
                    CompilerContractKind.Ensures,
                    factory.Boolean(true),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    "surplus-claim",
                    null)
            ],
            [],
            WorkerClaimReason.None,
            CompilerPreparedBody.Trivial());
        var backend = new ThrowingBackend("Malformed input reached the backend.");

        var results = await new CallableVerifier(
            backend,
            WorkerBudgets.DefaultMaximumExpressionDepth).VerifyAsync(
                target,
                CreateResourceBudget(),
                CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.Zero);

            // The surplus clause has no claim id to report against, so the
            // response is clamped to the declared claims. Reporting the
            // manifest defect must not itself throw and be laundered into an
            // InfrastructureFailure.
            Assert.That(results, Has.Length.EqualTo(1));
            Assert.That(
                results[0].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                results[0].Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedContract));
        }
    }

    [Test]
    public async Task MissingPreparedBodyFailsClosedBeforeBackendInvocation()
    {
        var factory = new IrFactory();
        var target = CreateTarget(
            factory,
            factory.Boolean(true),
            [],
            body: null);
        var backend = new ThrowingBackend("Malformed input reached the backend.");

        var results = await new CallableVerifier(
            backend,
            WorkerBudgets.DefaultMaximumExpressionDepth).VerifyAsync(
                target,
                CreateResourceBudget(),
                CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    [Test]
    public async Task DeepPreconditionFailsClosedBeforeBackendInvocation()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var target = CreateTarget(
            factory,
            [
                new CompilerPreparedClause(
                    CompilerContractKind.Requires,
                    factory.Binary(
                        IrBinaryOperator.Equal,
                        factory.Variable(value),
                        factory.Integer(1)),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    null,
                    null),
                new CompilerPreparedClause(
                    CompilerContractKind.Ensures,
                    factory.Boolean(true),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    "claim",
                null)
            ],
            [new CompilerCanonicalVariable(
                CompilerVariableRole.Parameter,
                0,
                value,
                null,
                null,
                "value")],
            CompilerPreparedBody.Trivial());
        var backend = new ThrowingBackend("Malformed input reached the backend.");

        var results = await new CallableVerifier(
            backend,
            maximumExpressionDepth: 1).VerifyAsync(
                target,
                CreateResourceBudget(),
                CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
        }
    }

    [Test]
    public async Task EmptySourceIntervalFailsClosedBeforeBackendInvocation()
    {
        var factory = new IrFactory();
        var parameter = factory.CreateVariable("value", factory.IntegerType);
        var target = CreateTarget(
            factory,
            factory.Boolean(true),
            [new CompilerCanonicalVariable(
                CompilerVariableRole.Parameter,
                0,
                parameter,
                null,
                new CompilerIntegerInterval(1, 0),
                "value")],
            CompilerPreparedBody.Trivial());
        var backend = new ThrowingBackend("Malformed input reached the backend.");

        var results = await new CallableVerifier(
            backend,
            WorkerBudgets.DefaultMaximumExpressionDepth).VerifyAsync(
                target,
                CreateResourceBudget(),
                CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
        }
    }

    [Test]
    public async Task ResourceCounterCrossingMethodLimitDiscardsBackendOutcome()
    {
        long consumed = 0;
        var backend = new ResourceConsumingBackend(
            () => consumed = 11,
            BackendCheckResult.Unsatisfiable([]));
        var verifier = new CallableVerifier(
            backend,
            WorkerBudgets.DefaultMaximumExpressionDepth);
        var budget = new MethodResourceBudget(
            () => Volatile.Read(ref consumed),
            queryRlimit: 10,
            methodRlimit: 10);

        var results = await verifier.VerifyAsync(
            CreateTrivialTarget(),
            budget,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(1));
            Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SemanticPreconditionContradictionIsExplicitVacuityEvidence(
        bool hasBody)
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var variable = factory.Variable(value);
        var target = CreateTarget(
            factory,
            [
                Requires(factory.Binary(
                    IrBinaryOperator.GreaterThan,
                    variable,
                    factory.Integer(0))),
                Requires(factory.Binary(
                    IrBinaryOperator.LessThan,
                    variable,
                    factory.Integer(0))),
                Ensures(factory.Boolean(false))
            ],
            [Parameter(value)],
            hasBody ? CompilerPreparedBody.Trivial() : null);

        var result = await VerifyWithSmtAsync(target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                result.Vacuity,
                Is.EqualTo(
                    WorkerVacuityKind.ContradictoryPreconditions));
            Assert.That(result.ProofCore, Is.Not.Empty);
        }
    }

    [Test]
    public async Task ResultSourceDomainCannotCreatePreconditionVacuity()
    {
        var factory = new IrFactory();
        var resultVariable = factory.CreateVariable(
            "result",
            factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(
            entry,
            factory.CreateOperation(),
            factory.Integer(-1));
        var target = CreateTarget(
            factory,
            factory.Boolean(false),
            [new CompilerCanonicalVariable(
                CompilerVariableRole.Result,
                -1,
                resultVariable,
                null,
                new CompilerIntegerInterval(0, byte.MaxValue),
                "result")],
            CompilerPreparedBody.ProgramBody(
                builder.Build(),
                ImmutableDictionary<IrVarId, IrVarId>.Empty,
                ImmutableDictionary<
                    IrInstructionId,
                    CompilerPreparedSpecCall>.Empty,
                ImmutableDictionary<
                    IrInstructionId,
                    CompilerPreparedSummaryCall>.Empty));

        var result = await VerifyWithSmtAsync(target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        }
    }

    [Test]
    public async Task UserAssumeCannotCreatePreconditionVacuity()
    {
        var factory = new IrFactory();
        var target = CreateTarget(
            factory,
            [
                new CompilerPreparedClause(
                    CompilerContractKind.Assume,
                    factory.Boolean(false),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    null,
                    "assume"),
                Ensures(factory.Boolean(false))
            ],
            [],
            CompilerPreparedBody.Trivial());

        var result = await VerifyWithSmtAsync(target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        }
    }

    [TestCase(true, WorkerClaimOutcome.Proven)]
    [TestCase(false, WorkerClaimOutcome.Refuted)]
    public async Task SatisfiablePreconditionPreservesPostconditionVerdict(
        bool postcondition,
        WorkerClaimOutcome expectedOutcome)
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var target = CreateTarget(
            factory,
            [
                Requires(factory.Binary(
                    IrBinaryOperator.GreaterThan,
                    factory.Variable(value),
                    factory.Integer(0))),
                Ensures(factory.Boolean(postcondition))
            ],
            [Parameter(value)],
            CompilerPreparedBody.Trivial());

        var result = await VerifyWithSmtAsync(target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(expectedOutcome));
            Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        }
    }

    [Test]
    public async Task UnknownPreconditionSatisfiabilityCannotBecomeVacuousProof()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var target = CreateTarget(
            factory,
            [
                Requires(factory.Binary(
                    IrBinaryOperator.GreaterThan,
                    factory.Variable(value),
                    factory.Integer(0))),
                Ensures(factory.Boolean(true))
            ],
            [Parameter(value)],
            CompilerPreparedBody.Trivial());
        var backend = new ScriptedBackend(
            BackendCheckResult.Unknown(
                BackendFailureReason.InfrastructureFailure),
            BackendCheckResult.Unsatisfiable([]));

        var results = await new CallableVerifier(
            backend,
            WorkerBudgets.DefaultMaximumExpressionDepth).VerifyAsync(
                target,
                CreateResourceBudget(),
                CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(1));
            Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                results.Single().Reason,
                Is.EqualTo(WorkerClaimReason.InfrastructureFailure));
            Assert.That(results.Single().Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        }
    }

    [TestCase(IrBinaryOperator.Equal, false, false, WorkerVacuityKind.NoModeledNormalReturn)]
    [TestCase(IrBinaryOperator.NotEqual, true, false, WorkerVacuityKind.None)]
    [TestCase(IrBinaryOperator.Equal, false, true, WorkerVacuityKind.NoModeledNormalReturn)]
    public async Task NormalCompletionVacuityMatchesModeledPath(
        IrBinaryOperator completionOperator,
        bool postcondition,
        bool assumeCompletion,
        WorkerVacuityKind expectedVacuity)
    {
        var result = await VerifyWithSmtAsync(
            CreateDivisionTarget(
                completionOperator,
                postcondition,
                assumeCompletion));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Vacuity, Is.EqualTo(expectedVacuity));
        }
    }

    [Test]
    public async Task UnknownNormalCompletionSatisfiabilityCannotBecomeProof()
    {
        var backend = new SatisfiableUnknownProofBackend();

        var results = await new CallableVerifier(
            backend,
            WorkerBudgets.DefaultMaximumExpressionDepth).VerifyAsync(
                CreateDivisionTarget(
                    IrBinaryOperator.NotEqual,
                    postcondition: true),
                CreateResourceBudget(),
                CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(3));
            Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                results.Single().Reason,
                Is.EqualTo(WorkerClaimReason.InfrastructureFailure));
            Assert.That(results.Single().Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        }
    }


    [Test]
    public void UnknownCompilerClauseKindIsRejectedExhaustively()
    {
        var factory = new IrFactory();
        var target = CreateTarget(
            factory,
            [
                new CompilerPreparedClause(
                    (CompilerContractKind)int.MaxValue,
                    factory.Boolean(true),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    null,
                    null),
                new CompilerPreparedClause(
                    CompilerContractKind.Ensures,
                    factory.Boolean(true),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    "claim",
                    null)
            ],
            [],
            CompilerPreparedBody.Trivial());

        Func<Task> action =
            () => new CallableVerifier(
                new ThrowingBackend("Malformed input reached the backend."),
                WorkerBudgets.DefaultMaximumExpressionDepth).VerifyAsync(
                    target,
                    CreateResourceBudget(),
                    CancellationToken.None);
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(action);
    }

    [Test]
    public void MalformedBackendOutcomeBecomesTypedUnknown()
    {
        var result = CallableClaimResultAssembler.FromOutcome(
            CreateTrivialTarget(),
            0,
            outcome: null!,
            [],
            new Dictionary<ProofJustification, string>(),
            new Dictionary<ProofJustification, string>(),
            WorkerClaimReason.None,
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
            [],
            new Dictionary<ProofJustification, string>(),
            new Dictionary<ProofJustification, string>(),
            WorkerClaimReason.None,
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
            [],
            new Dictionary<ProofJustification, string>(),
            new Dictionary<ProofJustification, string>(),
            WorkerClaimReason.None,
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
            variables,
            new Dictionary<ProofJustification, string>(),
            new Dictionary<ProofJustification, string>(),
            WorkerClaimReason.None,
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

        var reason = CallableCounterexampleReplayer.Replay(
            target,
            0,
            ImmutableDictionary<IrVarId, IrValue>.Empty,
            target.Clauses);

        Assert.That(
            reason,
            Is.EqualTo(WorkerClaimReason.CounterexampleReplayFailed));
    }

    [Test]
    public void NullResultFacetProjectsToNegativeNonNullEvidence()
    {
        var factory = new IrFactory();
        var result = factory.CreateVariable(
            "result",
            factory.ObjectType);

        var succeeded = SpecResultDomainProjection.TryCreate(
            factory,
            CreateTemplate(
                IrTypeKind.Reference,
                SpecNullness.Null,
                SpecCardinality.NotApplicable),
            result,
            out var projection,
            out var evidence);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(succeeded, Is.True);
            Assert.That(projection.NonNullVariable, Is.Not.Null);
            Assert.That(projection.LengthVariable, Is.Null);
            Assert.That(evidence, Has.Length.EqualTo(1));
            Assert.That(evidence[0], Is.TypeOf<IrUnaryTerm>());
        }
    }

    [Test]
    public void NonEmptySequenceFacetProjectsToPositiveLengthEvidence()
    {
        var factory = new IrFactory();
        var result = factory.CreateVariable(
            "result",
            factory.GetOrCreateSequenceType(factory.IntegerType));

        var succeeded = SpecResultDomainProjection.TryCreate(
            factory,
            CreateTemplate(
                IrTypeKind.Sequence,
                SpecNullness.NonNull,
                SpecCardinality.NonEmpty),
            result,
            out var projection,
            out var evidence);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(succeeded, Is.True);
            Assert.That(projection.NonNullVariable, Is.Not.Null);
            Assert.That(projection.LengthVariable, Is.Not.Null);
            Assert.That(evidence, Has.Length.EqualTo(2));
            Assert.That(evidence[1], Is.TypeOf<IrBinaryTerm>());
        }
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
            [],
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
            [],
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

    [Test]
    public void CacheWriteRollsBackPublicationWhenPostValidationIsCanceled()
    {
        using var temporaryDirectory = new TempDirectory("worker-cache-cancel-");
        var directory = temporaryDirectory.FullName;
        using var cancellation = new CancellationTokenSource();
        try
        {
            var inputHash = new string('f', 64);
            var manifest = new WorkerClaimManifest();
            WorkerProtocolJson.SealManifest(manifest);
            var cache = new VerificationCache(directory, 1024 * 1024);
            VerificationCache.PathValidationOverride = (_, path) =>
            {
                if (path.EndsWith(
                        CacheFileSuffix,
                        StringComparison.Ordinal) &&
                    File.Exists(path))
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
            };

            Func<Task> write = async () =>
            {
                await cache.TryWriteAsync(
                    new WorkerVerifyResponse(),
                    inputHash,
                    manifest,
                    cancellation.Token);
            };
            Assert.ThrowsAsync<OperationCanceledException>(write);
            Assert.That(
                Directory.GetFiles(directory, "*" + CacheFileSuffix),
                Is.Empty);
        }
        finally
        {
            VerificationCache.PathValidationOverride = null;
        }
    }

    [Test]
    public void CacheCapacityScanStopsAfterCancellation()
    {
        const string suffix = CacheFileSuffix;
        using var temporaryDirectory = new TempDirectory(
            "worker-cache-capacity-cancel-");
        var directory = temporaryDirectory.FullName;
        using var cancellation = new CancellationTokenSource();
        try
        {
            for (var index = 0; index < 32; index++)
            {
                File.WriteAllText(
                    Path.Combine(
                        directory,
                        index.ToString("x64", CultureInfo.InvariantCulture) + suffix),
                    "entry");
            }

            var inputHash = new string('f', 64);
            var publishedPath = Path.Combine(directory, inputHash + suffix);
            var validatedEntries = 0;
            VerificationCache.PathValidationOverride = (_, candidate) =>
            {
                if (!candidate.EndsWith(suffix, StringComparison.Ordinal) ||
                    string.Equals(
                        candidate,
                        publishedPath,
                        StringComparison.Ordinal))
                {
                    return;
                }

                if (Interlocked.Increment(ref validatedEntries) == 1)
                {
                    cancellation.Cancel();
                }
            };
            var manifest = new WorkerClaimManifest();
            WorkerProtocolJson.SealManifest(manifest);
            var cache = new VerificationCache(directory, 1024 * 1024);

            Func<Task> write = async () =>
            {
                await cache.TryWriteAsync(
                    new WorkerVerifyResponse(),
                    inputHash,
                    manifest,
                    cancellation.Token);
            };
            Assert.ThrowsAsync<OperationCanceledException>(write);
            Assert.That(validatedEntries, Is.EqualTo(1));
        }
        finally
        {
            VerificationCache.PathValidationOverride = null;
        }
    }

    [Test]
    public async Task CacheWriteRollbackRestoresPreExistingExactKeyBytes()
    {
        using var temporaryDirectory = new TempDirectory(
            "worker-cache-existing-");
        var directory = temporaryDirectory.FullName;
        try
        {
            var inputHash = new string('9', 64);
            var manifest = new WorkerClaimManifest();
            WorkerProtocolJson.SealManifest(manifest);
            var cache = new VerificationCache(directory, 1024 * 1024);
            Assert.That(
                await cache.TryWriteAsync(
                    new WorkerVerifyResponse(),
                    inputHash,
                    manifest,
                    CancellationToken.None),
                Is.True);
            var path = Directory.GetFiles(
                directory,
                ("*" + CacheFileSuffix)).Single();
            var original = await File.ReadAllBytesAsync(path);
            VerificationCache.PathValidationOverride = (_, candidate) =>
            {
                if (candidate.EndsWith(
                        CacheFileSuffix,
                        StringComparison.Ordinal) &&
                    File.Exists(candidate))
                {
                    throw new ArgumentException("synthetic post-publish failure");
                }
            };

            var written = await cache.TryWriteAsync(
                new WorkerVerifyResponse
                {
                    ClaimResults = [new WorkerClaimResult
                    {
                        ClaimId = "different"
                    }]
                },
                inputHash,
                manifest,
                CancellationToken.None);

            Assert.That(written, Is.False);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
        }
        finally
        {
            VerificationCache.PathValidationOverride = null;
        }
    }

    [Test]
    public async Task CacheRollbackRemainsLockedUntilAttemptOwnedStateIsRemoved()
    {
        using var temporaryDirectory = new TempDirectory(
            "worker-cache-locked-rollback-");
        var directory = temporaryDirectory.FullName;
        using var rollbackEntered = new ManualResetEventSlim();
        using var allowRollback = new ManualResetEventSlim();
        try
        {
            var firstHash = new string('7', 64);
            var secondHash = new string('8', 64);
            var manifest = new WorkerClaimManifest();
            WorkerProtocolJson.SealManifest(manifest);
            var cache = new VerificationCache(directory, 1024 * 1024);
            var failValidation = 0;
            VerificationCache.PathValidationOverride = (_, candidate) =>
            {
                if (candidate.EndsWith(
                        CacheFileSuffix,
                        StringComparison.Ordinal) &&
                    File.Exists(candidate) &&
                    Interlocked.CompareExchange(
                        ref failValidation,
                        1,
                        0) == 0)
                {
                    throw new ArgumentException("synthetic post-publish failure");
                }
            };
            var rollbackCalls = 0;
            VerificationCache.TransactionRollbackOverride = () =>
            {
                if (Interlocked.Increment(ref rollbackCalls) == 1)
                {
                    rollbackEntered.Set();
                    allowRollback.Wait(TimeSpan.FromSeconds(10));
                }
            };

            var first = Task.Run(() => cache.TryWriteAsync(
                new WorkerVerifyResponse(),
                firstHash,
                manifest,
                CancellationToken.None));
            Assert.That(
                rollbackEntered.Wait(TimeSpan.FromSeconds(10)),
                Is.True,
                "The failed transaction did not reach rollback.");
            var competing = await cache.TryWriteAsync(
                new WorkerVerifyResponse(),
                secondHash,
                manifest,
                CancellationToken.None);
            Assert.That(
                competing,
                Is.False,
                "A second cache transaction observed attempt-owned state.");

            allowRollback.Set();
            Assert.That(await first, Is.False);
            Assert.That(
                await cache.TryWriteAsync(
                    new WorkerVerifyResponse(),
                    secondHash,
                    manifest,
                    CancellationToken.None),
                Is.True);
            Assert.That(
                Directory.GetFiles(directory, "*" + CacheFileSuffix)
                    .Select(Path.GetFileName),
                Is.EqualTo(new[] {
                    secondHash + CacheFileSuffix
                }));
        }
        finally
        {
            allowRollback.Set();
            VerificationCache.TransactionRollbackOverride = null;
            VerificationCache.PathValidationOverride = null;
        }
    }

    [Test]
    public void CacheLockDisposesHandleWhenPostOpenValidationFails()
    {
        using var temporaryDirectory = new TempDirectory(
            "worker-cache-lock-validation-");
        var directory = temporaryDirectory.FullName;
        try
        {
            var calls = 0;
            Action<string, string> validatePath = (_, _) =>
            {
                calls++;
                if (calls == 3)
                {
                    throw new ArgumentException("synthetic validation failure");
                }
            };

            VerificationCache.PathValidationOverride = validatePath;
            var acquireLock = typeof(VerificationCache)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(method => method.Name == "AcquireLock" &&
                    method.GetParameters().Length == 1);
            Action failValidation = () => acquireLock.Invoke(
                null,
                [directory]);
            var invocation = Assert.Throws<TargetInvocationException>(
                failValidation);
            Assert.That(
                invocation!.InnerException,
                Is.TypeOf<ArgumentException>());

            using var reopened = new FileStream(
                Path.Combine(directory, ".sharp-proof-cache.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.That(calls, Is.EqualTo(3));
        }
        finally
        {
            VerificationCache.PathValidationOverride = null;
        }
    }

    private static Task WriteCacheEnvelopeAsync(
        string directory,
        string inputHash,
        string manifestHash,
        WorkerCallableResult[]? callableResults,
        WorkerClaimResult[] claimResults)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                ManifestHash = manifestHash,
                CallableResults = callableResults,
                ClaimResults = claimResults
            },
            WorkerProtocolJson.Options);
        var payloadHash = WorkerProtocolJson.ComputeSha256(
            Encoding.UTF8.GetBytes(payload));
        var envelope = JsonSerializer.Serialize(
            new
            {
                SchemaVersion = WorkerCacheVersions.Current,
                InputHash = inputHash,
                PayloadHash = payloadHash,
                Payload = payload
            },
            WorkerProtocolJson.Options);
        return File.WriteAllTextAsync(
            Path.Combine(
                directory,
                inputHash + CacheFileSuffix),
            envelope);
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

    private static CompilerCallablePreparation CreateMalformedProgramTarget(
        MalformedBodyKind kind)
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        switch (kind)
        {
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
                    CompilerPreparedSpecCall>.Empty,
                ImmutableDictionary<
                    IrInstructionId,
                    CompilerPreparedSummaryCall>.Empty));
    }

    private static CompilerCallablePreparation CreateDivisionTarget(
        IrBinaryOperator preconditionOperator,
        bool postcondition,
        bool assumeCompletion = false)
    {
        var factory = new IrFactory();
        var parameter = factory.CreateVariable(
            "parameter",
            factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var division = factory.Binary(
            IrBinaryOperator.Divide,
            factory.Integer(1),
            factory.Variable(parameter));
        builder.Return(
            entry,
            factory.CreateOperation(),
            division);
        var clauses = ImmutableArray.CreateBuilder<CompilerPreparedClause>();
        clauses.Add(
            Requires(factory.Binary(
                preconditionOperator,
                factory.Variable(parameter),
                factory.Integer(0))));
        if (assumeCompletion)
        {
            clauses.Add(
                new CompilerPreparedClause(
                    CompilerContractKind.Assume,
                    factory.Binary(
                        IrBinaryOperator.Equal,
                        division,
                        division),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    null,
                    "assume-completion"));
        }

        clauses.Add(Ensures(factory.Boolean(postcondition)));
        return CreateTarget(
            factory,
            clauses.ToImmutable(),
            [Parameter(parameter)],
            CompilerPreparedBody.ProgramBody(
                builder.Build(),
                ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(
                    parameter,
                    parameter),
                ImmutableDictionary<
                    IrInstructionId,
                    CompilerPreparedSpecCall>.Empty,
                ImmutableDictionary<
                    IrInstructionId,
                    CompilerPreparedSummaryCall>.Empty));
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

    private static CompilerPreparedClause Requires(IrTerm condition)
    {
        return new(
            CompilerContractKind.Requires,
            condition,
            CompilerContractEvidence.CompilerBoundInvocation,
            null,
            null);
    }

    private static CompilerPreparedClause Ensures(IrTerm condition)
    {
        return new(
            CompilerContractKind.Ensures,
            condition,
            CompilerContractEvidence.CompilerBoundInvocation,
            "claim",
            null);
    }

    private static CompilerCanonicalVariable Parameter(IrVarId variable)
    {
        return new(
            CompilerVariableRole.Parameter,
            0,
            variable,
            null,
            null,
            "value");
    }

    private static async Task<WorkerClaimResult> VerifyWithSmtAsync(
        CompilerCallablePreparation target)
    {
        using var backend = new SharpProof.Smt.IrSmtBackend(
            new SharpProof.Smt.IrSmtBackendOptions(
                WorkerBudgets.DefaultQueryRlimit));
        return (await new CallableVerifier(
            backend,
            WorkerBudgets.DefaultMaximumExpressionDepth).VerifyAsync(
                target,
                CreateResourceBudget(),
                CancellationToken.None)).Single();
    }

    private static MethodResourceBudget CreateResourceBudget()
    {
        return new(
            null,
            WorkerBudgets.DefaultQueryRlimit,
            WorkerBudgets.DefaultMethodRlimit);
    }

    private static ApiSpecTemplate CreateTemplate(
        IrTypeKind resultType,
        SpecNullness nullness,
        SpecCardinality cardinality)
    {
        return WorkerApiSpecTestFixtures.CreateTemplate(
            "test.tcb.result",
            "M:Test.Tcb.Result",
            "Test.Tcb",
            "worker-tcb-edge-test",
            resultType,
            nullness,
            cardinality);
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

    private sealed class SatisfiableUnknownProofBackend : ISmtBackend
    {
        private int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                Interlocked.Increment(ref _callCount) switch
                {
                    1 => BackendCheckResult.Satisfiable(
                        new BackendModel(
                            query.ModelVariables.Select(variable =>
                                KeyValuePair.Create(
                                    variable,
                                    query.Factory.CreateIntegerValue(1))))),
                    2 => BackendCheckResult.Unknown(
                        BackendFailureReason.InfrastructureFailure),
                    3 => BackendCheckResult.Unsatisfiable([]),
                    _ => throw new AssertionException(
                        "Unexpected verification query.")
                });
        }
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
