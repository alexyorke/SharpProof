using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class PassiveLoopCutterTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ExceptionLoopRouterCannotRetainEntryHeapAfterSkippedStores(bool array)
    {
        var (candidate, inputs) = ExceptionLoopHeapCandidate(array);
        var execution = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, inputs, maximumSteps: 128);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(2)));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(PassiveLoopCutter.TryCreate(candidate, out var proof, out _, out var cutReason, CancellationToken.None), Is.True, cutReason.ToString());
        Assert.That(proof!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrHavocInstruction>()
            .Any(havoc => havoc.HavocKind == IrHavocKind.VariablesAndMemory), Is.True);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<RefutedOutcome>());
    }

    internal static (PassiveCallableCandidate Candidate, Dictionary<IrVarId, IrValue> Inputs) ExceptionLoopHeapCandidate(bool array)
    {
        var subject = new PassiveCallableVcTests.ScalarSubject();
        var factory = subject.Factory;
        var builder = subject.Builder;
        var ownerType = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.ObjectType;
        var owner = new PassiveParameterBinding(factory.CreateVariable("owner:entry", ownerType),
            factory.CreateVariable("owner:current", ownerType), factory.CreateVariable("owner:old", ownerType));
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
        var entry = builder.CreateBlock();
        var header = builder.CreateBlock();
        var body = builder.CreateBlock();
        var exit = builder.CreateBlock();
        void Store(IrBlockId block, int value)
        {
            if (array)
            { builder.ElementStore(block, subject.Site, factory.Variable(owner.Current), factory.Integer(0), factory.Integer(value)); }
            else
            { builder.FieldStore(block, subject.Site, IrWriteRegion.Field, factory.Variable(owner.Current), field, factory.Integer(value)); }
        }
        foreach (var parameter in new[] { owner, subject.Parameter })
        {
            builder.Assign(entry, subject.Site, parameter.Current, factory.Variable(parameter.Entry));
            builder.Assign(entry, subject.Site, parameter.Old, factory.Variable(parameter.Entry));
        }
        Store(entry, 0);
        builder.Goto(entry, subject.Site, header);
        builder.Branch(header, subject.Site, factory.Binary(IrBinaryOperator.GreaterThan,
            factory.Variable(subject.Parameter.Current), factory.Integer(0)), body, exit);
        Store(body, 2);
        builder.Assign(body, subject.Site, subject.Parameter.Current, factory.Binary(IrBinaryOperator.Subtract,
            factory.Variable(subject.Parameter.Current), factory.Integer(1)));
        builder.Throw(body, subject.Site, IrExceptionKind.Overflow, header);
        builder.Return(exit, subject.Site, array ? factory.SequenceAccess(factory.Variable(owner.Current), factory.Integer(0))
            : factory.PureOpaque(field, factory.Variable(owner.Current)));
        var requires = new List<PassiveContractClause>
        {
            new(factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(owner.Entry), factory.Null(ownerType)), factory.Boolean(true), subject.Site),
            new(factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Parameter.Entry), factory.Integer(2)), factory.Boolean(true), subject.Site)
        };
        if (array)
        { requires.Add(new(factory.Binary(IrBinaryOperator.Equal, factory.Length(factory.Variable(owner.Entry)), factory.Integer(1)), factory.Boolean(true), subject.Site)); }
        var candidate = new PassiveCallableCandidate("exception-loop-heap", builder.Build(), [owner, subject.Parameter], subject.Result,
            [.. requires],
            [new(factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Result), factory.Integer(0)), factory.Boolean(true), subject.Site)]);
        return (candidate, new Dictionary<IrVarId, IrValue>
        {
            [owner.Entry] = array ? factory.CreateSequenceValue(ownerType, [factory.CreateIntegerValue(0)])
                : factory.CreateReferenceValue(ownerType, new IrObjectState()),
            [subject.Parameter.Entry] = factory.CreateIntegerValue(2)
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LoopCannotModifyCanonicalImmutableRoles(bool old)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var binding = new PassiveParameterBinding(factory.CreateVariable("entry", factory.IntegerType), factory.CreateVariable("current", factory.IntegerType), factory.CreateVariable("old", factory.IntegerType));
        var result = factory.CreateVariable("result", factory.IntegerType);
        var site = factory.CreateOperation("loop");
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var loop = builder.CreateBlock();
        builder.Assign(entry, site, binding.Old, factory.Variable(binding.Entry));
        builder.Goto(entry, site, loop);
        builder.Assign(loop, site, old ? binding.Old : binding.Entry, factory.Integer(factory.IntegerType, 0));
        builder.Goto(loop, site, loop);
        var candidate = new PassiveCallableCandidate("owned", builder.Build(), [binding], result, [], [new(factory.Boolean(false), factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var reason), Is.False);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [Test]
    public void IrreducibleMultipleEntryLoopRemainsClosed()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var site = factory.CreateOperation("irreducible");
        var entry = builder.CreateBlock();
        var first = builder.CreateBlock();
        var second = builder.CreateBlock();
        builder.Branch(entry, site, factory.Boolean(true), first, second);
        builder.Goto(first, site, second);
        builder.Goto(second, site, first);
        var candidate = new PassiveCallableCandidate("owned", builder.Build(), [], null, [], [new(factory.Boolean(false), factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var reason), Is.False);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [Test]
    public void UnsupportedLoopInstructionStaysUnenrolledRatherThanBudgetLimited()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var site = factory.CreateOperation("unsupported-loop");
        var entry = builder.CreateBlock();
        builder.Assert(entry, site, factory.Boolean(true));
        builder.Goto(entry, site, entry);
        var candidate = new PassiveCallableCandidate("owned", builder.Build(), [], null, [], [new(factory.Boolean(false), factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var reason), Is.False);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [Test]
    public void ExpansionStopsAtTheExistingConstructionBoundAndHonorsCancellation()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var site = factory.CreateOperation("large-loop");
        var blocks = Enumerable.Range(0, 1000).Select(_ => builder.CreateBlock()).ToArray();
        for (var ordinal = 0; ordinal < blocks.Length; ordinal++)
        { builder.Goto(blocks[ordinal], site, blocks[(ordinal + 1) % blocks.Length]); }
        var candidate = new PassiveCallableCandidate("owned", builder.Build(), [], null, [], [new(factory.Boolean(false), factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var reason), Is.False);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => PassiveCallableVcBuilder.TryBuild(candidate, out _, out _, cancellation.Token)));
    }

    [Test]
    public async Task NestedSharedStorageNeverProvesTheEntryValueIsUnchanged()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Requires(x == 0); Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
                for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) x++; } return x;
            } }
            """);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(project.Snapshot.Callables.Single())!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.SolverIncomplete));
    }

    [Test]
    public async Task LegalPrologueFilterOutsideLoopKeepsUsedConditionalProvenance()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x, int y) {
                Contract.Assume(y > 0); Contract.Ensures(Contract.Result<int>() > 0);
                while (x < 3) x++; return y;
            } }
            """);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(project.Snapshot.Callables.Single())!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(result.BodyAssumptions, Has.Length.EqualTo(1));
        Assert.That(plan!.EntryQuery().Assumptions.Select(assumption => assumption.Justification), Is.All.TypeOf<LoweredJustification>());
    }

    [TestCase("input")]
    [TestCase("approximation-read")]
    [TestCase("approximation-overwritten")]
    public async Task OriginalLoopHavocRetainsEntryIdentityAndApproximationReadRules(string kind)
    {
        var subject = new PassiveCallableVcTests.ScalarSubject();
        var factory = subject.Factory;
        var counter = factory.CreateVariable("counter", factory.IntegerType);
        var entry = subject.Builder.CreateBlock();
        var header = subject.Builder.CreateBlock();
        var body = subject.Builder.CreateBlock();
        var end = subject.Builder.CreateBlock();
        subject.Builder.Assign(entry, subject.Site, subject.Parameter.Old, factory.Variable(subject.Parameter.Entry));
        subject.Builder.Assign(entry, subject.Site, counter, factory.Integer(0));
        subject.Builder.Goto(entry, subject.Site, header);
        subject.Builder.Branch(header, subject.Site, factory.Binary(IrBinaryOperator.LessThan, factory.Variable(counter), factory.Integer(2)), body, end);
        subject.Builder.Havoc(body, subject.Site, IrHavocKind.Variables,
            kind == "input" ? IrHavocOrigin.Input : IrHavocOrigin.Approximation, subject.Parameter.Current);
        if (kind == "approximation-overwritten")
        { subject.Builder.Assign(body, subject.Site, subject.Parameter.Current, factory.Integer(1)); }
        subject.Builder.Assign(body, subject.Site, counter, factory.Binary(IrBinaryOperator.Add, factory.Variable(counter), factory.Integer(1)));
        subject.Builder.Goto(body, subject.Site, header);
        subject.Builder.Return(end, subject.Site, factory.Variable(subject.Parameter.Current));
        var candidate = subject.Candidate(factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Result), factory.Integer(0)),
            factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Parameter.Entry), factory.Integer(1)));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var outcome = await solver.VerifyEnsuresAsync(0);
        if (kind == "approximation-read")
        {
            Assert.That(outcome.Outcome, Is.TypeOf<UnknownOutcome>());
            Assert.That(outcome.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        }
        else
        {
            Assert.That(outcome.Outcome, Is.TypeOf<RefutedOutcome>());
            Assert.That(outcome.EntryModel.Single().Value.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.One));
        }
    }
}
