using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class PassiveExceptionLoopTests
{
    [TestCase("call", true)]
    [TestCase("memory", true)]
    [TestCase("variables-memory", true)]
    [TestCase("variables", false)]
    [TestCase("scalar", false)]
    [TestCase("parameter", true)]
    [TestCase("unknown", true)]
    public void RouterHeapForgettingTracksSkippedEffects(string effect, bool forgetsHeap)
    {
        var subject = new PassiveCallableVcTests.ScalarSubject();
        var factory = subject.Factory;
        var builder = subject.Builder;
        var entry = builder.CreateBlock();
        var header = builder.CreateBlock();
        var body = builder.CreateBlock();
        var exit = builder.CreateBlock();
        var routerSite = factory.CreateOperation("external-entry");
        var bodySite = factory.CreateOperation("body");
        var local = factory.CreateVariable("unused", factory.IntegerType);
        builder.Goto(entry, routerSite, header);
        builder.Branch(header, bodySite, factory.Boolean(true), exit, body);
        switch (effect)
        {
            case "call":
                var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Opaque", factory.IntegerType, true);
                builder.Call(body, bodySite, null, member, null);
                break;
            case "memory":
                builder.Havoc(body, bodySite, IrHavocKind.Memory, IrHavocOrigin.Approximation);
                break;
            case "variables-memory":
            case "variables":
                builder.Havoc(body, bodySite, effect == "variables-memory" ? IrHavocKind.VariablesAndMemory : IrHavocKind.Variables,
                    IrHavocOrigin.Approximation, local);
                break;
            case "parameter":
            case "unknown":
                builder.Write(body, bodySite, effect == "parameter" ? IrWriteRegion.Parameter : IrWriteRegion.Unknown);
                break;
            default:
                builder.Assign(body, bodySite, local, factory.Integer(1));
                break;
        }
        builder.Throw(body, bodySite, IrExceptionKind.Overflow, header);
        builder.Return(exit, bodySite, factory.Integer(0));
        Assert.That(PassiveLoopCutter.TryCreate(subject.Candidate(factory.Boolean(true)), out var proof, out var search,
            out var reason, CancellationToken.None), Is.True, reason.ToString());
        var routerHavoc = proof!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrHavocInstruction>()
            .Single(havoc => havoc.Operation == routerSite);
        Assert.That(routerHavoc.HavocKind, Is.EqualTo(forgetsHeap ? IrHavocKind.VariablesAndMemory : IrHavocKind.Variables));
        Assert.That(routerHavoc.Origin, Is.EqualTo(IrHavocOrigin.Approximation));
        Assert.That(search!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrHavocInstruction>()
            .Any(havoc => havoc.Operation == routerSite), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MultipleEntryExceptionComponentKeepsItsFiniteNormalPath(bool feasibility)
    {
        var candidate = MultipleEntryCandidate();
        var actual = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program);
        Assert.That(actual.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(actual.ReturnValue!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.One));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        if (feasibility)
        {
            Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        }
        else
        {
            Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<RefutedOutcome>());
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RoutersKeepOriginalOwnershipAndCanonicalProjection(bool insideEntry)
    {
        var candidate = MultipleEntryCandidate(insideEntry, unusedInput: true);
        var originalInstructions = candidate.Program.Blocks.Sum(block => block.Instructions.Length);
        Assert.That(PassiveLoopCutter.TryCreate(candidate, out var proof, out var search, out var reason, CancellationToken.None),
            Is.True, reason.ToString());
        Assert.That(candidate.Program.Blocks.Sum(block => block.Instructions.Length), Is.EqualTo(originalInstructions));
        Assert.That(search!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrHavocInstruction>(), Is.Empty);
        var havocs = proof!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrHavocInstruction>().ToArray();
        Assert.That(havocs, Has.Length.EqualTo(insideEntry ? 1 : 2));
        Assert.That(havocs.Select(havoc => havoc.Origin), Is.All.EqualTo(IrHavocOrigin.Approximation));
        Assert.That(havocs.SelectMany(havoc => havoc.Variables).Intersect(candidate.Parameters.SelectMany(parameter => new[] { parameter.Entry, parameter.Old })), Is.Empty);
        Assert.That(proof.Program.Blocks.SelectMany(block => block.Instructions).Select(instruction => instruction.Operation),
            Is.All.EqualTo(candidate.Ensures[0].Operation));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out reason), Is.True, reason.ToString());
        var query = plan!.EnsuresQuery(0);
        Assert.That(query.Assumptions.Select(assumption => assumption.Justification), Is.All.TypeOf<LoweredJustification>());
        Assert.That(query.Assumptions.Select(assumption => assumption.Justification).Distinct().Count(), Is.EqualTo(query.Assumptions.Length));
        using var solver = new PassiveCallableSolver(plan);
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.EntryModel.Keys, Is.EqualTo(new[] { candidate.Parameters[0].Entry }));
        Assert.That(result.EntryModel.Values.Single().IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(3)));
        Assert.That(result.BodyAssumptions, Is.Empty);
    }

    [TestCase("old")]
    [TestCase("unbound")]
    [TestCase("naked-exit")]
    public void RouterDoesNotRepairMissingImmutableStateOrPendingThrow(string kind)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("entry", factory.IntegerType),
            factory.CreateVariable("current", factory.IntegerType), factory.CreateVariable("old", factory.IntegerType));
        var unbound = factory.CreateVariable("unbound", factory.IntegerType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var site = factory.CreateOperation("malformed-original");
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var first = builder.CreateBlock();
        var second = builder.CreateBlock();
        var end = builder.CreateBlock();
        builder.Goto(entry, site, first);
        builder.Branch(first, site, factory.Boolean(true), end, second);
        builder.Throw(second, site, IrExceptionKind.Overflow, first);
        if (kind == "naked-exit")
        { builder.ExceptionalExit(end, site); }
        else
        { builder.Return(end, site, factory.Variable(kind == "old" ? parameter.Old : unbound)); }
        var candidate = new PassiveCallableCandidate("malformed", builder.Build(), [parameter], result, [], [new(factory.Boolean(false), factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var reason), Is.False);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [TestCase(-1)]
    [TestCase(0)]
    public async Task ExceptionComponentKeepsBothNormalExitsAndChangedStorage(int input)
    {
        var subject = new PassiveCallableVcTests.ScalarSubject();
        var factory = subject.Factory;
        var builder = subject.Builder;
        var entry = builder.CreateBlock();
        var first = builder.CreateBlock();
        var second = builder.CreateBlock();
        var thrown = builder.CreateBlock();
        var zero = builder.CreateBlock();
        var one = builder.CreateBlock();
        builder.Assign(entry, subject.Site, subject.Parameter.Old, factory.Variable(subject.Parameter.Entry));
        builder.Branch(entry, subject.Site, factory.Boolean(true), first, second);
        builder.Assign(first, subject.Site, subject.Parameter.Current,
            factory.Binary(IrBinaryOperator.Add, factory.Variable(subject.Parameter.Current), factory.Integer(1)));
        builder.Branch(first, subject.Site, factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Parameter.Current), factory.Integer(0)), zero, second);
        builder.Branch(second, subject.Site, factory.Binary(IrBinaryOperator.LessThan, factory.Variable(subject.Parameter.Current), factory.Integer(2)), one, thrown);
        builder.Assign(thrown, subject.Site, subject.Parameter.Current, factory.Integer(0));
        builder.Throw(thrown, subject.Site, IrExceptionKind.Overflow, first);
        builder.Return(zero, subject.Site, factory.Variable(subject.Parameter.Current));
        builder.Return(one, subject.Site, factory.Variable(subject.Parameter.Current));
        var candidate = subject.Candidate(factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Result), factory.Variable(subject.Parameter.Old)),
            factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Parameter.Entry), factory.Integer(input)));
        var value = factory.CreateIntegerValue(factory.IntegerType, input);
        var actual = new IrProgramInterpreter(factory).Execute(candidate.Program,
            new Dictionary<IrVarId, IrValue> { [subject.Parameter.Entry] = value, [subject.Parameter.Current] = value });
        Assert.That(actual.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(actual.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(input + 1)));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<RefutedOutcome>());
    }

    [TestCase("assume")]
    [TestCase("entry")]
    [TestCase("old")]
    public void ExceptionComponentKeepsCyclicFiltersAndImmutableWritesClosed(string kind)
    {
        var subject = new PassiveCallableVcTests.ScalarSubject();
        var block = subject.Builder.CreateBlock();
        if (kind == "assume")
        { subject.Builder.Assume(block, subject.Site, subject.Factory.Boolean(true)); }
        else
        { subject.Builder.Assign(block, subject.Site, kind == "entry" ? subject.Parameter.Entry : subject.Parameter.Old, subject.Factory.Integer(0)); }
        subject.Builder.Throw(block, subject.Site, IrExceptionKind.Overflow, block);
        Assert.That(PassiveCallableVcBuilder.TryBuild(subject.Candidate(subject.Factory.Boolean(false)), out _, out var reason), Is.False);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [Test]
    public void RepeatedExternalRoutersStopBeforeUnboundedGraphGrowth()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var site = factory.CreateOperation("router-bound");
        var entries = Enumerable.Range(0, 1100).Select(_ => builder.CreateBlock()).ToArray();
        var first = builder.CreateBlock();
        var second = builder.CreateBlock();
        var end = builder.CreateBlock();
        for (var ordinal = 0; ordinal < entries.Length - 1; ordinal++)
        { builder.Branch(entries[ordinal], site, factory.Boolean(false), first, entries[ordinal + 1]); }
        builder.Goto(entries[^1], site, first);
        builder.Branch(first, site, factory.Boolean(true), end, second);
        builder.Throw(second, site, IrExceptionKind.Overflow, first);
        builder.Return(end, site);
        var candidate = new PassiveCallableCandidate("bounded", builder.Build(), [], null, [], [new(factory.Boolean(false), factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var reason), Is.False);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => PassiveCallableVcBuilder.TryBuild(candidate, out _, out _, cancellation.Token)));
    }

    internal static PassiveCallableCandidate MultipleEntryCandidate(bool insideEntry = false, bool unusedInput = false)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var site = factory.CreateOperation("multi-entry-original");
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("E");
        var first = builder.CreateBlock("A");
        var second = builder.CreateBlock("B");
        var thrown = builder.CreateBlock("T");
        var returned = builder.CreateBlock("R");
        if (insideEntry)
        { builder.SetEntry(first); }
        builder.Branch(entry, site, factory.Boolean(true), first, second);
        builder.Goto(first, site, second);
        builder.Branch(second, site, factory.Boolean(true), returned, thrown);
        builder.Throw(thrown, site, IrExceptionKind.DivideByZero, first);
        builder.Return(returned, site, factory.Integer(1));
        if (unusedInput)
        {
            var parameter = new PassiveParameterBinding(factory.CreateVariable("unused-entry", factory.IntegerType),
                factory.CreateVariable("unused-current", factory.IntegerType), factory.CreateVariable("unused-old", factory.IntegerType));
            return new("multi-entry", builder.Build(), [parameter], result,
                [new(factory.Binary(IrBinaryOperator.Equal, factory.Variable(parameter.Entry), factory.Integer(3)), factory.Boolean(true), site)],
                [new(factory.Boolean(false), factory.Boolean(true), site)]);
        }
        return new("multi-entry", builder.Build(), [], result, [], [new(factory.Boolean(false), factory.Boolean(true), site)]);
    }
}
