using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeLoopAdmissionContractTests
{
    // Admission policy: assumptions within a cyclic component cannot be
    // treated as independently justified loop invariant premises.
    [TestCase(false)]
    [TestCase(true)]
    public void CyclicAssumptionIsRejectedBeforeInvariantCandidates(bool containsAssumption)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var header = builder.CreateBlock("header");
        var body = builder.CreateBlock("body");
        var exit = builder.CreateBlock("exit");
        var site = factory.CreateOperation("loop-admission");
        var input = new PassiveParameterBinding(factory.CreateVariable("entry", factory.BooleanType),
            factory.CreateVariable("current", factory.BooleanType), factory.CreateVariable("old", factory.BooleanType));
        builder.Assign(entry, site, input.Current, factory.Variable(input.Entry));
        builder.Goto(entry, site, header);
        builder.Branch(header, site, factory.Variable(input.Current), body, exit);
        if (containsAssumption)
        { builder.Assume(body, site, factory.Boolean(true)); }
        builder.Assign(body, site, input.Current, factory.Boolean(false));
        builder.Goto(body, site, header);
        builder.Return(exit, site);
        var candidate = new PassiveCallableCandidate("cyclic-assumption-admission", builder.Build(), [input], null, [], []);
        var loops = PassiveLoopCutter.Loops(candidate, TestContext.CurrentContext.CancellationToken);
        Assert.That(loops, Has.Length.EqualTo(containsAssumption ? 0 : 1));
        if (!containsAssumption)
        {
            Assert.That(loops.Single().Blocks, Does.Contain(body));
            Assert.That(loops.Single().Blocks, Does.Not.Contain(exit));
        }
        var accepted = PassiveLoopCutter.TryCreate(candidate, out var proof, out _, out var reason, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accepted, Is.EqualTo(!containsAssumption));
            Assert.That(proof == null, Is.EqualTo(containsAssumption));
            Assert.That(reason, Is.EqualTo(containsAssumption ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
        }
    }
}
