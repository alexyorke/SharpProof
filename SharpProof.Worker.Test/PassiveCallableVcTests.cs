using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class PassiveCallableVcTests
{
    [TestCase(false, false, PassiveCallableFeasibilityKind.Feasible)]
    [TestCase(true, false, PassiveCallableFeasibilityKind.NoModeledNormalReturn)]
    [TestCase(false, true, PassiveCallableFeasibilityKind.ContradictoryEntry)]
    public async Task OwnedNormalFeasibilitySeparatesEntryAndActualReturns(bool thrown, bool impossible, int expectedKind)
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        if (thrown)
        {
            var exit = subject.Builder.CreateBlock();
            subject.Builder.Throw(block, subject.Site, IrExceptionKind.Overflow, exit);
            subject.Builder.ExceptionalExit(exit, subject.Site);
        }
        else
        { subject.Builder.Return(block, subject.Site, subject.Factory.Variable(subject.Parameter.Current)); }
        var plan = Build(subject.Candidate(subject.Factory.Boolean(false), requires: subject.Factory.Boolean(!impossible)));
        using var solver = new PassiveCallableSolver(plan);
        var result = await solver.VerifyFeasibilityAsync();
        Assert.That((int)result.Kind, Is.EqualTo(expectedKind));
        Assert.That(result.EntryEvidence.Outcome,
            impossible ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>());
        if (result.Kind == PassiveCallableFeasibilityKind.Feasible)
        {
            Assert.That(result.Evidence.Outcome, Is.TypeOf<RefutedOutcome>());
            Assert.That(result.Evidence.EntryModel.Keys, Is.EqualTo(new[] { subject.Parameter.Entry }));
        }
    }

    [Test]
    public async Task NormalWitnessRejectsActualApproximationReadAndKeepsQueryIsolation()
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        subject.Builder.Havoc(block, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Approximation, subject.Parameter.Current);
        subject.Builder.Return(block, subject.Site, subject.Factory.Variable(subject.Parameter.Current));
        using var solver = new PassiveCallableSolver(Build(subject.Candidate(subject.Factory.Boolean(false))));
        var normal = await solver.VerifyFeasibilityAsync();
        Assert.That(normal.Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Unknown));
        Assert.That(normal.Evidence.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        Assert.That(normal.EntryEvidence.Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That((await solver.VerifyEntryAsync()).Outcome, Is.TypeOf<RefutedOutcome>());
    }
    [Test]
    public async Task SourceDiamondReplaysMutationOldAndEveryUnusedCanonicalInput()
    {
        var subject = PassiveSourceSubject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x, bool choose, int unused) {
                    Contract.Ensures(Contract.Result<int>() == (choose ? unchecked(Contract.Old(x) + 1) : unchecked(Contract.Old(x) - 1)) && x == Contract.Result<int>());
                    Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
                    if (choose) x = unchecked(x + 1); else x = unchecked(x - 1);
                    return x;
                }
            }
            """);
        var candidate = subject.Enroll()!;
        var plan = Build(candidate);
        using var solver = new PassiveCallableSolver(plan);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(candidate.Parameters.Select(parameter => parameter.Entry)));
        Assert.That(plan.EnsuresQuery(1).ModelVariables, Does.Contain(candidate.Parameters[2].Entry));
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [TestCase("int", "2147483647", "-2147483648")]
    [TestCase("ulong", "18446744073709551615UL", "0UL")]
    public async Task SourceWrapUsesTheOriginalTypedBody(string type, string maximum, string wrapped)
    {
        var subject = PassiveSourceSubject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static {{type}} Target({{type}} x) {
                    Contract.Requires(x == {{maximum}});
                    Contract.Ensures(Contract.Result<{{type}}>() == {{wrapped}});
                    x = unchecked(x + 1);
                    return x;
                }
            }
            """);
        using var solver = new PassiveCallableSolver(Build(subject.Enroll()!));
        Assert.That((await solver.VerifyEntryAsync()).Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task UnsafeSourceEnsuresAbstainsAfterOriginalGuardReplay()
    {
        var subject = PassiveSourceSubject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int d) { Contract.Ensures(10 / d == 10 / d); return 0; }
            }
            """);
        using var solver = new PassiveCallableSolver(Build(subject.Enroll()!));
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.PostconditionMayBeUndefined));
        Assert.That(result.EntryModel, Is.Empty);
    }

    [Test]
    public async Task PointAssumeIsConditionalEvidenceAndNeverAnEntryPremise()
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        subject.Builder.Assume(block, subject.Site, subject.Factory.Boolean(false));
        subject.Builder.Return(block, subject.Site, subject.Factory.Integer(0));
        var plan = Build(subject.Candidate(subject.Factory.Boolean(false)));
        Assert.That(plan.EntryQuery().Assumptions, Is.Empty);
        using var solver = new PassiveCallableSolver(plan);
        Assert.That((await solver.VerifyEntryAsync()).Outcome, Is.TypeOf<RefutedOutcome>());
        var proof = await solver.VerifyEnsuresAsync(0);
        Assert.That(proof.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(proof.BodyAssumptions, Is.EqualTo(new[] { subject.Site }));
        Assert.That(proof.Core.Any(label => label.StartsWith("assume:", StringComparison.Ordinal)), Is.True);
        var normal = await solver.VerifyFeasibilityAsync();
        Assert.That(normal.Kind, Is.EqualTo(PassiveCallableFeasibilityKind.NoModeledNormalReturn));
        Assert.That(normal.Evidence.BodyAssumptions, Is.EqualTo(new[] { subject.Site }));
        Assert.That((await solver.VerifyEntryAsync()).Outcome, Is.TypeOf<RefutedOutcome>());
    }

    [Test]
    public async Task ContradictoryRequiresUsesOnlyEntryEvidence()
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        subject.Builder.Assume(block, subject.Site, subject.Factory.Boolean(true));
        subject.Builder.Return(block, subject.Site, subject.Factory.Integer(0));
        using var solver = new PassiveCallableSolver(Build(subject.Candidate(subject.Factory.Boolean(false), requires: subject.Factory.Boolean(false))));
        var entry = await solver.VerifyEntryAsync();
        Assert.That(entry.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(entry.Core, Has.Length.EqualTo(1));
        Assert.That(entry.Core.Single(), Is.EqualTo("requires:0"));
        Assert.That(entry.BodyAssumptions, Is.Empty);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExplicitThrowHasNoNormalObligationUnlessItsTargetReturns(bool caught)
    {
        var subject = new ScalarSubject();
        var start = subject.Builder.CreateBlock();
        var target = subject.Builder.CreateBlock();
        subject.Builder.Throw(start, subject.Site, IrExceptionKind.Overflow, target);
        if (caught)
        { subject.Builder.Return(target, subject.Site, subject.Factory.Integer(0)); }
        else
        { subject.Builder.ExceptionalExit(target, subject.Site); }
        using var solver = new PassiveCallableSolver(Build(subject.Candidate(subject.Factory.Boolean(false))));
        Assert.That((await solver.VerifyEntryAsync()).Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind,
            Is.EqualTo(caught ? PassiveCallableFeasibilityKind.Feasible : PassiveCallableFeasibilityKind.NoModeledNormalReturn));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, caught ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public void ReachableNakedExceptionalExitClosesButUnusedExitDoesNot()
    {
        var malformed = new ScalarSubject();
        malformed.Builder.ExceptionalExit(malformed.Builder.CreateBlock(), malformed.Site);
        AssertClosed(malformed.Candidate(malformed.Factory.Boolean(false)));
        var unused = new ScalarSubject();
        unused.Builder.Return(unused.Builder.CreateBlock(), unused.Site, unused.Factory.Integer(0));
        unused.Builder.ExceptionalExit(unused.Builder.CreateBlock(), unused.Site);
        Build(unused.Candidate(unused.Factory.Boolean(false)));
    }

    [Test]
    public async Task RepeatedBoundInputUsesTheSameOriginalEntryIdentity()
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        subject.Builder.Havoc(block, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Input, subject.Parameter.Current);
        subject.Builder.Assign(block, subject.Site, subject.Parameter.Current, subject.Factory.Integer(42));
        subject.Builder.Havoc(block, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Input, subject.Parameter.Current);
        subject.Builder.Return(block, subject.Site, subject.Factory.Variable(subject.Parameter.Current));
        var equalOld = subject.Factory.Binary(IrBinaryOperator.Equal, subject.Factory.Variable(subject.Result), subject.Factory.Variable(subject.Parameter.Old));
        using var solver = new PassiveCallableSolver(Build(subject.Candidate(equalOld, additionalEnsures: subject.Factory.Unary(IrUnaryOperator.Not, equalOld))));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That((await solver.VerifyEnsuresAsync(1)).Outcome, Is.TypeOf<RefutedOutcome>());
    }

    [TestCase(IrHavocOrigin.Input)]
    [TestCase(IrHavocOrigin.SpecResult)]
    public void UnboundInputAndSpecificationResultClose(IrHavocOrigin origin)
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        var local = subject.Factory.CreateVariable("local", subject.Factory.IntegerType);
        subject.Builder.Havoc(block, subject.Site, IrHavocKind.Variables, origin, local);
        subject.Builder.Return(block, subject.Site, subject.Factory.Variable(local));
        AssertClosed(subject.Candidate(subject.Factory.Boolean(false)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ApproximationCanRefuteOnlyWhenNeverRead(bool readBeforeOverwrite)
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        var local = subject.Factory.CreateVariable("local", subject.Factory.IntegerType);
        subject.Builder.Havoc(block, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Approximation, local);
        if (readBeforeOverwrite)
        {
            var observed = subject.Factory.CreateVariable("observed", subject.Factory.IntegerType);
            subject.Builder.Assign(block, subject.Site, observed, subject.Factory.Variable(local));
        }
        subject.Builder.Assign(block, subject.Site, local, subject.Factory.Integer(0));
        subject.Builder.Return(block, subject.Site, subject.Factory.Variable(local));
        using var solver = new PassiveCallableSolver(Build(subject.Candidate(subject.Factory.Boolean(false))));
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, readBeforeOverwrite ? Is.TypeOf<UnknownOutcome>() : Is.TypeOf<RefutedOutcome>());
        Assert.That(result.Reason, Is.EqualTo(readBeforeOverwrite ? WorkerClaimReason.CounterexampleNotReplayable : WorkerClaimReason.None));
    }

    [TestCase("Contract.Assume(System.Math.Abs(x) > 0); return x;")]
    [TestCase("return Helper(x);")]
    public void UnsupportedSourceAssumeAndNonExactBodyCannotEnroll(string body)
    {
        var subject = PassiveSourceSubject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x) { Contract.Ensures(true); {{body}} }
                private static int Helper(int x) => x;
            }
            """);
        Assert.That(subject.Enroll(), Is.Null);
    }

    [Test]
    public void SourceContextsCannotSwapBodiesOrClauses()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(Contract.Result<int>() == x); return x; } }
            """;
        var first = PassiveSourceSubject.Create(source);
        var second = PassiveSourceSubject.Create(source);
        Assert.Throws<ArgumentException>(new Action(() => PassiveSourceSubject.Enroll(first.Context, second.Binding, first.Lowering)));
        Assert.Throws<ArgumentException>(new Action(() => PassiveSourceSubject.Enroll(first.Context, first.Binding, second.Lowering)));
        var shared = new IrFactory(IrExecutionSemantics.Total);
        const string related = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x) { Contract.Ensures(Contract.Result<int>() == x); return x; }
                public static int Other(int x) { Contract.Ensures(Contract.Result<int>() == -x); return -x; }
            }
            """;
        var target = PassiveSourceSubject.Create(related, shared);
        var other = PassiveSourceSubject.Create(related, shared, "Other");
        Assert.Throws<ArgumentException>(new Action(() => PassiveSourceSubject.Enroll(target.Context, other.Binding, target.Lowering)));
        Assert.Throws<ArgumentException>(new Action(() => PassiveSourceSubject.Enroll(target.Context, target.Binding, other.Lowering)));
    }

    [TestCase("old-current")]
    [TestCase("old-entry")]
    [TestCase("cross-input")]
    [TestCase("result")]
    public void CanonicalRoleAliasesCannotEnroll(string alias)
    {
        var subject = new ScalarSubject();
        subject.Builder.Return(subject.Builder.CreateBlock(), subject.Site, subject.Factory.Integer(0));
        var program = subject.Builder.Build();
        var original = subject.Parameter;
        var first = alias switch
        {
            "old-current" => original with { Old = original.Current },
            "old-entry" => original with { Old = original.Entry },
            _ => original
        };
        ImmutableArray<PassiveParameterBinding> parameters = [first];
        if (alias == "cross-input")
        {
            parameters = parameters.Add(new(original.Current,
                subject.Factory.CreateVariable("other-current", subject.Factory.IntegerType),
                subject.Factory.CreateVariable("other-old", subject.Factory.IntegerType)));
        }
        Assert.Throws<ArgumentException>(new Action(() => new PassiveCallableCandidate("aliases", program, parameters,
            alias == "result" ? original.Current : subject.Result, [], [])));
    }

    [Test]
    public async Task BodyOldRequiresAnOriginalInitialization()
    {
        var subject = new ScalarSubject();
        subject.Builder.Return(subject.Builder.CreateBlock(), subject.Site, subject.Factory.Variable(subject.Parameter.Old));
        var ensures = subject.Factory.Binary(IrBinaryOperator.Equal, subject.Factory.Variable(subject.Result), subject.Factory.Variable(subject.Parameter.Entry));
        var candidate = subject.Candidate(ensures);
        if (PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out _))
        {
            using var solver = new PassiveCallableSolver(plan!);
            var result = await solver.VerifyEnsuresAsync(0);
            Assert.Fail("Uninitialized body Old was enrolled and received " + result.Outcome!.GetType().Name + ".");
        }
        AssertClosed(candidate);
    }

    [Test]
    public async Task SameSiteFactsHaveDistinctJustificationsAndStableReorderedCoreLabels()
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        subject.Builder.Assume(block, subject.Site, subject.Factory.Boolean(false));
        subject.Builder.Return(block, subject.Site, subject.Factory.Integer(0));
        var plan = Build(subject.Candidate(subject.Factory.Boolean(false)));
        var query = plan.EnsuresQuery(0);
        Assert.That(query.Assumptions.Select(assumption => assumption.Justification).Distinct().Count(), Is.EqualTo(query.Assumptions.Length));
        Assert.That(query.Assumptions.Select(assumption => assumption.Justification).OfType<LoweredJustification>()
            .Select(justification => justification.Operation).Distinct(),
            Is.EqualTo(new[] { subject.Site }));
        Assert.That(query.Assumptions.Select(assumption => assumption.Justification).OfType<UserAssumedJustification>()
            .Single().Location, Is.EqualTo(new SourceLocationId(subject.Site.Value)));
        var kernel = new ProofKernel(new ReorderedCoreBackend());
        var proof = (ProvenOutcome)await kernel.VerifyAsync(query);
        Assert.That(plan.CoreLabels(proof), Has.Length.EqualTo(query.Assumptions.Length));
        Assert.That(plan.UsedBodyAssumptions(proof), Is.EqualTo(new[] { subject.Site }));
        var other = new ScalarSubject();
        var otherBlock = other.Builder.CreateBlock();
        other.Builder.Return(otherBlock, other.Site, other.Factory.Integer(0));
        var otherPlan = Build(other.Candidate(other.Factory.Boolean(false)));
        Assert.Throws<ArgumentException>(new Action(() => otherPlan.CoreLabels(proof)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => plan.EnsuresQuery(1)));
    }

    [TestCase(8, true)]
    [TestCase(8, false)]
    [TestCase(16, true)]
    [TestCase(16, false)]
    [TestCase(32, true)]
    [TestCase(32, false)]
    [TestCase(64, true)]
    [TestCase(64, false)]
    [TestCase(0, false)]
    public async Task ScalarIdentitySupportsEveryDeclaredType(int width, bool hasSign)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = width == 0 ? factory.BooleanType : factory.GetOrCreateIntegerType(width, hasSign);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("entry", type), factory.CreateVariable("current", type), factory.CreateVariable("old", type));
        var result = factory.CreateVariable("result", type);
        var site = factory.CreateOperation();
        var builder = new IrProgramBuilder(factory);
        builder.Return(builder.CreateBlock(), site, factory.Variable(parameter.Current));
        var candidate = new PassiveCallableCandidate("identity", builder.Build(), [parameter], result, [],
            [new(factory.Binary(IrBinaryOperator.Equal, factory.Variable(result), factory.Variable(parameter.Old)), factory.Boolean(true), site)]);
        using var solver = new PassiveCallableSolver(Build(candidate));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [TestCase("assert")]
    [TestCase("call")]
    public void UnsupportedEffectsClose(string scenario)
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        switch (scenario)
        {
            case "assert":
                subject.Builder.Assert(block, subject.Site, subject.Factory.Boolean(true));
                break;
            case "call":
                var member = subject.Factory.GetOrCreateMember(subject.Factory.CreateIdentity(), subject.Factory.ObjectType,
                    "Call", subject.Factory.IntegerType, true);
                subject.Builder.Call(block, subject.Site, subject.Result, member, null);
                break;
        }
        subject.Builder.Return(block, subject.Site, subject.Factory.Integer(0));
        AssertClosed(subject.Candidate(subject.Factory.Boolean(false)));
    }

    [Test]
    public async Task PureScalarCycleHasSoundAbstractNormalVacuity()
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        subject.Builder.Goto(block, subject.Site, block);
        using var solver = new PassiveCallableSolver(Build(subject.Candidate(subject.Factory.Boolean(false))));
        var feasibility = await solver.VerifyFeasibilityAsync();
        Assert.That(feasibility.Kind, Is.EqualTo(PassiveCallableFeasibilityKind.NoModeledNormalReturn));
        Assert.That(feasibility.Evidence.Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public void AJoinCannotHideAnIncomingExitWithoutPendingThrow()
    {
        var subject = new ScalarSubject();
        var entry = subject.Builder.CreateBlock();
        var thrown = subject.Builder.CreateBlock();
        var plain = subject.Builder.CreateBlock();
        var exit = subject.Builder.CreateBlock();
        subject.Builder.Branch(entry, subject.Site, subject.Factory.Binary(IrBinaryOperator.Equal,
            subject.Factory.Variable(subject.Parameter.Current), subject.Factory.Integer(0)), thrown, plain);
        subject.Builder.Throw(thrown, subject.Site, IrExceptionKind.Overflow, exit);
        subject.Builder.Goto(plain, subject.Site, exit);
        subject.Builder.ExceptionalExit(exit, subject.Site);
        AssertClosed(subject.Candidate(subject.Factory.Boolean(false)));
    }

    [Test]
    public void StraightLineStateForwardsVersionsWithoutQuadraticFacts()
    {
        const int blocks = 80;
        const int parameters = 32;
        var plan = Build(StraightLineCandidate(blocks, parameters));
        var query = plan.EnsuresQuery(0);
        Assert.That(query.Assumptions.Length, Is.LessThan(4 * (blocks + parameters)));
        Assert.That(query.ModelVariables.Length, Is.LessThan(4 * (blocks + parameters)));
    }

    [Test]
    public void ConstructionClosesBeforeOversizedGraphTraversalAndHonorsCancellation()
    {
        var oversized = StraightLineCandidate(PassiveCallableVcBuilder.MaximumSteps + 1, 0);
        Assert.That(PassiveCallableVcBuilder.TryBuild(oversized, out var plan, out var failure), Is.False);
        Assert.That(plan, Is.Null);
        Assert.That(failure, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() =>
            PassiveCallableVcBuilder.TryBuild(StraightLineCandidate(2, 1), out _, out _, cancellation.Token)));
    }

    [Test]
    public void LargeConstantWritesStayBoundedAndStateHeavyConstructionCloses()
    {
        const int parameterCount = 1000;
        const int writes = 2000;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameters = Enumerable.Range(0, parameterCount).Select(index => new PassiveParameterBinding(
            factory.CreateVariable("entry:" + index, factory.IntegerType), factory.CreateVariable("current:" + index, factory.IntegerType),
            factory.CreateVariable("old:" + index, factory.IntegerType))).ToImmutableArray();
        var site = factory.CreateOperation();
        var result = factory.CreateVariable("result", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        for (var index = 0; index < writes; index++)
        { builder.Assign(block, site, parameters[0].Current, factory.Integer(0)); }
        builder.Return(block, site, factory.Integer(0));
        var candidate = new PassiveCallableCandidate("constant-writes", builder.Build(), parameters, result, [],
            [new(factory.Boolean(false), factory.Boolean(true), site)]);
        var query = Build(candidate).EnsuresQuery(0);
        Assert.That(query.Assumptions.Length, Is.LessThan(4 * (parameterCount + writes)));
        Assert.That(query.ModelVariables.Length, Is.LessThan(4 * (parameterCount + writes)));
        var heavy = StraightLineCandidate(300, parameterCount);
        Assert.That(PassiveCallableVcBuilder.TryBuild(heavy, out var plan, out var failure), Is.False);
        Assert.That(plan, Is.Null);
        Assert.That(failure, Is.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [Test]
    public async Task MethodMeterReadsActualConsumptionAndCancellationDoesNotSpend()
    {
        var subject = new ScalarSubject();
        subject.Builder.Return(subject.Builder.CreateBlock(), subject.Site, subject.Factory.Integer(0));
        using var solver = new PassiveCallableSolver(Build(subject.Candidate(subject.Factory.Boolean(false))), queryRlimit: 1, methodRlimit: 1);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () => { await solver.VerifyEntryAsync(cancellation.Token); }));
        Assert.That(solver.ConsumedResourceCount, Is.Zero);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
        Assert.That((await solver.VerifyEntryAsync()).Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [Test]
    public async Task KernelRejectsMissingSsaAssignmentsBeforeOriginalReplay()
    {
        var subject = new ScalarSubject();
        var block = subject.Builder.CreateBlock();
        subject.Builder.Assign(block, subject.Site, subject.Parameter.Current, subject.Factory.Integer(0));
        subject.Builder.Return(block, subject.Site, subject.Factory.Variable(subject.Parameter.Current));
        var plan = Build(subject.Candidate(subject.Factory.Boolean(false)));
        var query = plan.EnsuresQuery(0);
        using var session = new CallableSolverSession(subject.Factory, new IrSmtBackendOptions());
        var actual = await session.CheckAsync(query, CancellationToken.None);
        Assert.That(actual.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var missing = query.ModelVariables.First(variable => variable != subject.Parameter.Entry && variable != subject.Parameter.Current);
        var kernel = new ProofKernel(new FixedModelBackend(new BackendModel(actual.Model!.Assignments.Remove(missing))));
        var outcome = await kernel.VerifyCallableAsync(query, plan.Replay(0));
        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CallViolationSurvivesLaterAssumptionOrThrow(bool thrown)
    {
        var subject = new ScalarSubject();
        var factory = subject.Factory;
        var block = subject.Builder.CreateBlock();
        var value = factory.Boolean(false);
        var safe = factory.Boolean(true);
        var target = factory.CreateVariable("call-marker", factory.BooleanType);
        var marker = subject.Builder.Assign(block, subject.Site, target,
            factory.Binary(IrBinaryOperator.AndAlso, safe, value));
        if (thrown)
        {
            var exit = subject.Builder.CreateBlock();
            subject.Builder.Throw(block, subject.Site, IrExceptionKind.Overflow, exit);
            subject.Builder.ExceptionalExit(exit, subject.Site);
        }
        else
        {
            subject.Builder.Assume(block, subject.Site, factory.Boolean(false));
            subject.Builder.Return(block, subject.Site, factory.Integer(0));
        }
        var candidate = new PassiveCallableCandidate("call-prefix", subject.Builder.Build(),
            [subject.Parameter], subject.Result, [], [], callPreconditions: [new(marker.Id, value, safe)]);
        using var solver = new PassiveCallableSolver(Build(candidate));
        var result = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(result.CallPreconditionWitness, Is.EqualTo(subject.Site));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CallReplayChecksApproximationAtMarker(bool readBefore)
    {
        var subject = new ScalarSubject();
        var factory = subject.Factory;
        var block = subject.Builder.CreateBlock();
        var local = factory.CreateVariable("approximate", factory.IntegerType);
        var observed = factory.CreateVariable("observed", factory.IntegerType);
        subject.Builder.Havoc(block, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Approximation, local);
        if (readBefore)
        { subject.Builder.Assign(block, subject.Site, observed, factory.Variable(local)); }
        var value = factory.Boolean(false);
        var safe = factory.Boolean(true);
        var target = factory.CreateVariable("call-marker", factory.BooleanType);
        var marker = subject.Builder.Assign(block, subject.Site, target,
            factory.Binary(IrBinaryOperator.AndAlso, safe, value));
        if (!readBefore)
        { subject.Builder.Assign(block, subject.Site, observed, factory.Variable(local)); }
        subject.Builder.Return(block, subject.Site, factory.Integer(0));
        var candidate = new PassiveCallableCandidate("call-taint", subject.Builder.Build(),
            [subject.Parameter], subject.Result, [], [], callPreconditions: [new(marker.Id, value, safe)]);
        using var solver = new PassiveCallableSolver(Build(candidate));
        var result = await solver.VerifyCallPreconditionAsync(0);
        if (readBefore)
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(result.CallPreconditionWitness, Is.Null);
        }
        else
        {
            Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>());
            Assert.That(result.CallPreconditionWitness, Is.EqualTo(subject.Site));
        }
    }

    [TestCase("allocation", true, false)]
    [TestCase("write", true, false)]
    [TestCase("allocation", false, false)]
    [TestCase("write", false, false)]
    [TestCase("allocation", false, true)]
    [TestCase("write", false, true)]
    [TestCase("allocation", null, false)]
    [TestCase("write", null, false)]
    public async Task EffectReplayChecksApproximationAtSite(string kind, bool? readBefore, bool throwAfter)
    {
        var subject = new ScalarSubject();
        var factory = subject.Factory;
        var block = subject.Builder.CreateBlock();
        var local = factory.CreateVariable("approximate", factory.IntegerType);
        var observed = factory.CreateVariable("observed", factory.IntegerType);
        subject.Builder.Havoc(block, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Approximation, local);
        if (readBefore == true)
        { subject.Builder.Assign(block, subject.Site, observed, factory.Variable(local)); }
        var effectSite = factory.CreateOperation("effect-site");
        if (kind == "allocation")
        { subject.Builder.Allocate(block, effectSite, factory.ObjectType); }
        else
        { subject.Builder.Write(block, effectSite, IrWriteRegion.Static); }
        if (readBefore == false)
        { subject.Builder.Assign(block, subject.Site, observed, factory.Variable(local)); }
        if (throwAfter)
        {
            var exit = subject.Builder.CreateBlock();
            subject.Builder.Throw(block, subject.Site, IrExceptionKind.Overflow, exit);
            subject.Builder.ExceptionalExit(exit, subject.Site);
        }
        else
        { subject.Builder.Return(block, subject.Site, factory.Integer(0)); }
        using var solver = new PassiveCallableSolver(Build(subject.Candidate(factory.Boolean(true))));
        var result = kind == "allocation" ? await solver.VerifyAllocationsAsync() : await solver.VerifyPurityAsync();
        if (readBefore == true)
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(result.AllocationWitness, Is.Null);
            Assert.That(result.WriteWitness, Is.Null);
        }
        else
        {
            Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>());
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(kind == "allocation" ? result.AllocationWitness : result.WriteWitness,
                Is.EqualTo(effectSite));
        }
    }

    [TestCase("read")]
    [TestCase("rewrite")]
    [TestCase("canonical")]
    [TestCase("orphan")]
    public void CallMarkersRequireFreshUnreadOwnedTargets(string mutation)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation();
        var predicate = factory.Boolean(true);
        var target = factory.CreateVariable(mutation == "orphan" ? "$sharpproof.requires:v1:unowned" : "call-marker", factory.BooleanType);
        var marker = builder.Assign(block, site, target, factory.Binary(IrBinaryOperator.AndAlso, predicate, predicate));
        if (mutation == "rewrite")
        { builder.Assign(block, site, target, factory.Boolean(false)); }
        builder.Return(block, site, mutation == "read" ? factory.Variable(target) : predicate);
        Assert.Throws<ArgumentException>(new Action(() => new PassiveCallableCandidate("call-owner", builder.Build(), [],
            mutation == "canonical" ? target : null, [], [],
            callPreconditions: mutation == "orphan" ? [] : [new(marker.Id, predicate, predicate)])));
    }

    [Test]
    public async Task CallReplayPreservesInputHavocValue()
    {
        var subject = new ScalarSubject();
        var factory = subject.Factory;
        var block = subject.Builder.CreateBlock();
        subject.Builder.Havoc(block, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Input, subject.Parameter.Current);
        subject.Builder.Assign(block, subject.Site, subject.Parameter.Current, factory.Integer(42));
        subject.Builder.Havoc(block, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Input, subject.Parameter.Current);
        var value = factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Parameter.Current), factory.Integer(0));
        var safe = factory.Boolean(true);
        var target = factory.CreateVariable("call-marker", factory.BooleanType);
        var marker = subject.Builder.Assign(block, subject.Site, target, factory.Binary(IrBinaryOperator.AndAlso, safe, value));
        subject.Builder.Return(block, subject.Site, factory.Integer(0));
        var requires = factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Parameter.Entry), factory.Integer(1));
        var candidate = new PassiveCallableCandidate("call-input", subject.Builder.Build(), [subject.Parameter], subject.Result,
            [new(requires, safe, subject.Site)], [], callPreconditions: [new(marker.Id, value, safe)]);
        using var solver = new PassiveCallableSolver(Build(candidate));
        var result = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(result.CallPreconditionWitness, Is.EqualTo(subject.Site));
    }

    [Test]
    public async Task BoundedCallQueriesCannotRecoverAnUnknownProof()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation();
        var predicate = factory.Boolean(true);
        var marker = builder.Assign(block, site, factory.CreateVariable("call-marker", factory.BooleanType),
            factory.Binary(IrBinaryOperator.AndAlso, predicate, predicate));
        builder.Goto(block, site, block);
        var plan = Build(new("call-loop", builder.Build(), [], null, [], [],
            callPreconditions: [new(marker.Id, predicate, predicate)]));
        Assert.That(plan.LoopSearch, Is.Not.Null);
        var backend = new CutUnknownSearchUnsatBackend();
        using var solver = new PassiveCallableSolver(plan, backend, new MethodResourceBudget(null, 1, 100));
        var result = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(backend.Calls, Is.EqualTo(plan.CallPreconditionQueries(0).Length + plan.LoopSearch!.CallPreconditionQueries(0).Length));
        Assert.That(result.Outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.SolverIncomplete));
        Assert.That(result.CallPreconditionWitness, Is.Null);
    }

    private sealed class CutUnknownSearchUnsatBackend : ISmtBackend
    {
        internal int Calls { get; private set; }
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(++Calls == 1 ? BackendCheckResult.Unknown(BackendFailureReason.Incomplete)
                : BackendCheckResult.Unsatisfiable(Enumerable.Range(0, query.Assumptions.Length)));
        }
    }

    internal static PassiveCallableCandidate StraightLineCandidate(int blockCount, int parameterCount)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameters = Enumerable.Range(0, parameterCount).Select(index => new PassiveParameterBinding(
            factory.CreateVariable("entry:" + index, factory.IntegerType), factory.CreateVariable("current:" + index, factory.IntegerType),
            factory.CreateVariable("old:" + index, factory.IntegerType))).ToImmutableArray();
        var site = factory.CreateOperation();
        var result = factory.CreateVariable("result", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var blocks = Enumerable.Range(0, blockCount).Select(_ => builder.CreateBlock()).ToArray();
        for (var index = 0; index < blocks.Length - 1; index++)
        { builder.Goto(blocks[index], site, blocks[index + 1]); }
        builder.Return(blocks[^1], site, factory.Integer(0));
        return new("straight", builder.Build(), parameters, result, [], [new(factory.Boolean(false), factory.Boolean(true), site)]);
    }

    private sealed class FixedModelBackend(BackendModel model) : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BackendCheckResult.Satisfiable(model));
        }
    }

    private static PassiveCallableVcPlan Build(PassiveCallableCandidate candidate)
    {
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.True, failure.ToString());
        return plan!;
    }

    private static void AssertClosed(PassiveCallableCandidate candidate)
    {
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.False);
        Assert.That(plan, Is.Null);
        Assert.That(failure, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    private sealed class ReorderedCoreBackend : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BackendCheckResult.Unsatisfiable(Enumerable.Range(0, query.Assumptions.Length).Reverse()));
        }
    }

    internal sealed class ScalarSubject
    {
        internal IrFactory Factory { get; } = new(IrExecutionSemantics.Total);
        internal PassiveParameterBinding Parameter { get; }
        internal IrVarId Result { get; }
        internal OperationId Site { get; }
        internal IrProgramBuilder Builder { get; }

        internal ScalarSubject()
        {
            Parameter = new(Factory.CreateVariable("entry", Factory.IntegerType), Factory.CreateVariable("current", Factory.IntegerType),
                Factory.CreateVariable("old", Factory.IntegerType));
            Result = Factory.CreateVariable("result", Factory.IntegerType);
            Site = Factory.CreateOperation("original-site");
            Builder = new(Factory);
        }

        internal PassiveCallableCandidate Candidate(IrTerm ensures, IrTerm? requires = null, IrTerm? additionalEnsures = null)
        {
            var clauses = ImmutableArray.CreateBuilder<PassiveContractClause>();
            clauses.Add(new(ensures, Factory.Boolean(true), Site));
            if (additionalEnsures != null)
            { clauses.Add(new(additionalEnsures, Factory.Boolean(true), Site)); }
            return new("scalar", Builder.Build(), [Parameter], Result,
                requires == null ? [] : [new(requires, Factory.Boolean(true), Site)], clauses.ToImmutable());
        }
    }
}
