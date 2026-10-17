using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeLoopCallObligationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task NaturalLoopObligationsKeepOriginalCallWitnesses(bool predicate)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("entry", factory.IntegerType),
            factory.CreateVariable("current", factory.IntegerType), factory.CreateVariable("old", factory.IntegerType));
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var header = builder.CreateBlock("header");
        var body = builder.CreateBlock("body");
        var exit = builder.CreateBlock("exit");
        var structural = factory.CreateOperation("loop-call-control");
        var site = factory.CreateOperation("owned-loop-call-obligation");
        var target = factory.CreateVariable("call-marker", factory.BooleanType);
        var value = factory.Boolean(predicate);
        var safe = factory.Boolean(true);
        builder.Assign(entry, structural, parameter.Current, factory.Variable(parameter.Entry));
        builder.Goto(entry, structural, header);
        builder.Branch(header, structural,
            factory.Binary(IrBinaryOperator.GreaterThan, factory.Variable(parameter.Current), factory.Integer(0)), body, exit);
        var marker = builder.Assign(body, site, target, factory.Binary(IrBinaryOperator.AndAlso, safe, value));
        builder.Assign(body, structural, parameter.Current,
            factory.Binary(IrBinaryOperator.Subtract, factory.Variable(parameter.Current), factory.Integer(1)));
        builder.Goto(body, structural, header);
        builder.Return(exit, structural);
        var bounds = factory.Binary(IrBinaryOperator.AndAlso,
            factory.Binary(IrBinaryOperator.GreaterThanOrEqual, factory.Variable(parameter.Entry), factory.Integer(0)),
            factory.Binary(IrBinaryOperator.LessThanOrEqual, factory.Variable(parameter.Entry), factory.Integer(3)));
        // Entry bounds are a module precondition; the natural SCC contains
        // only assignments and control flow, with an actual owned call marker.
        var candidate = new PassiveCallableCandidate("owned-loop-call-obligation", builder.Build(), [parameter], null,
            [new(bounds, safe, structural)], [], callPreconditions: [new(marker.Id, value, safe)]);
        for (var input = 0; input <= 3; input++)
        {
            var integer = factory.CreateIntegerValue(factory.IntegerType, input);
            var execution = new IrProgramInterpreter(factory).Execute(candidate.Program,
                new Dictionary<IrVarId, IrValue> { [parameter.Entry] = integer, [parameter.Current] = integer, [parameter.Old] = integer });
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.Unsupported, Is.Null);
            Assert.That(execution.Exception, Is.Null);
            Assert.That(execution.GetCurrentValue(parameter.Current)!.Integer, Is.Zero);
            Assert.That(execution.GetCurrentValue(target) == null, Is.EqualTo(input == 0));
            if (input != 0)
            { Assert.That(execution.GetCurrentValue(target)!.Boolean, Is.EqualTo(predicate)); }
        }
        Assert.That(PassiveLoopCutter.Loops(candidate, CancellationToken.None), Has.Length.EqualTo(1));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(plan!.LoopSearch, Is.Not.Null);
        using var solver = new PassiveCallableSolver(plan);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proof = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(proof.Outcome, predicate ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>());
        Assert.That(proof.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(proof.CallPreconditionWitness, predicate ? Is.Null : Is.EqualTo(site));
        if (!predicate)
        {
            Assert.That(proof.EntryModel[parameter.Entry].Integer, Is.InRange(1, 3));
            Assert.That(plan.ReplayCallPrecondition(0, proof.EntryModel, CancellationToken.None), Is.EqualTo(site));
        }
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
    }

    [TestCase("before-positive", true)]
    [TestCase("before-nonpositive", false)]
    [TestCase("after-nonnegative", true)]
    [TestCase("after-negative", false)]
    public async Task LoopObligationsReadTheActualMarkerPrefixState(string kind, bool proven)
    {
        System.ArgumentNullException.ThrowIfNull(kind);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("entry", factory.IntegerType),
            factory.CreateVariable("current", factory.IntegerType), factory.CreateVariable("old", factory.IntegerType));
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var header = builder.CreateBlock();
        var body = builder.CreateBlock();
        var exit = builder.CreateBlock();
        var structural = factory.CreateOperation("loop-prefix-control");
        var site = factory.CreateOperation("owned-loop-prefix-obligation");
        var target = factory.CreateVariable("call-marker", factory.BooleanType);
        var current = factory.Variable(parameter.Current);
        var zero = factory.Integer(0);
        var comparison = kind switch
        {
            "before-positive" => IrBinaryOperator.GreaterThan,
            "before-nonpositive" => IrBinaryOperator.LessThanOrEqual,
            "after-nonnegative" => IrBinaryOperator.GreaterThanOrEqual,
            _ => IrBinaryOperator.LessThan
        };
        var value = factory.Binary(comparison, current, zero);
        var safe = factory.Boolean(true);
        builder.Assign(entry, structural, parameter.Current, factory.Variable(parameter.Entry));
        builder.Goto(entry, structural, header);
        builder.Branch(header, structural, factory.Binary(IrBinaryOperator.GreaterThan, current, zero), body, exit);
        var afterDecrement = kind.StartsWith("after", StringComparison.Ordinal);
        if (afterDecrement)
        { builder.Assign(body, structural, parameter.Current, factory.Binary(IrBinaryOperator.Subtract, current, factory.Integer(1))); }
        var marker = builder.Assign(body, site, target, factory.Binary(IrBinaryOperator.AndAlso, safe, value));
        if (!afterDecrement)
        { builder.Assign(body, structural, parameter.Current, factory.Binary(IrBinaryOperator.Subtract, current, factory.Integer(1))); }
        builder.Goto(body, structural, header);
        builder.Return(exit, structural);
        var bounds = factory.Binary(IrBinaryOperator.AndAlso,
            factory.Binary(IrBinaryOperator.GreaterThanOrEqual, factory.Variable(parameter.Entry), zero),
            factory.Binary(IrBinaryOperator.LessThanOrEqual, factory.Variable(parameter.Entry), factory.Integer(3)));
        var candidate = new PassiveCallableCandidate("owned-loop-prefix-obligation", builder.Build(), [parameter], null,
            [new(bounds, safe, structural)], [], callPreconditions: [new(marker.Id, value, safe)]);
        for (var input = 0; input <= 3; input++)
        {
            var integer = factory.CreateIntegerValue(factory.IntegerType, input);
            var execution = new IrProgramInterpreter(factory).Execute(candidate.Program,
                new Dictionary<IrVarId, IrValue> { [parameter.Entry] = integer, [parameter.Current] = integer, [parameter.Old] = integer });
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.Unsupported, Is.Null);
            Assert.That(execution.Exception, Is.Null);
            Assert.That(execution.GetCurrentValue(parameter.Current)!.Integer, Is.Zero);
            Assert.That(execution.GetCurrentValue(target) == null, Is.EqualTo(input == 0));
            if (input != 0)
            { Assert.That(execution.GetCurrentValue(target)!.Boolean, Is.EqualTo(proven)); }
        }
        Assert.That(PassiveLoopCutter.Loops(candidate, CancellationToken.None), Has.Length.EqualTo(1));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(plan!.LoopSearch, Is.Not.Null);
        using var solver = new PassiveCallableSolver(plan);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proof = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(proof.Outcome, proven ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>());
        Assert.That(proof.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(proof.CallPreconditionWitness, proven ? Is.Null : Is.EqualTo(site));
        if (!proven)
        {
            Assert.That(proof.EntryModel[parameter.Entry].Integer, Is.InRange(1, 3));
            Assert.That(plan.ReplayCallPrecondition(0, proof.EntryModel, CancellationToken.None), Is.EqualTo(site));
        }
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
    }
}
