using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeAcyclicLoopAdmissionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task AcyclicOwnedProgramsNeedNoLoopEncodingAndRetainNativeProof(bool diamond)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var input = new PassiveParameterBinding(factory.CreateVariable("input:entry", factory.BooleanType),
            factory.CreateVariable("input:current", factory.BooleanType), factory.CreateVariable("input:old", factory.BooleanType));
        var result = factory.CreateVariable("result", factory.BooleanType);
        var site = factory.CreateOperation("acyclic-loop-admission");
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Assign(entry, site, input.Current, factory.Variable(input.Entry));
        if (diamond)
        {
            var left = builder.CreateBlock("left");
            var right = builder.CreateBlock("right");
            var exit = builder.CreateBlock("exit");
            builder.Branch(entry, site, factory.Variable(input.Current), left, right);
            builder.Goto(left, site, exit);
            builder.Goto(right, site, exit);
            builder.Return(exit, site, factory.Variable(input.Current));
        }
        else
        { builder.Return(entry, site, factory.Variable(input.Current)); }
        var ensures = new PassiveContractClause(factory.Binary(IrBinaryOperator.Equal,
            factory.Variable(result), factory.Variable(input.Old)), factory.Boolean(true), site);
        var candidate = new PassiveCallableCandidate("acyclic-loop-admission", builder.Build(), [input], result, [], [ensures]);

        Assert.That(PassiveLoopCutter.Loops(candidate, CancellationToken.None), Is.Empty);
        var cut = PassiveLoopCutter.TryCreate(candidate, out var proof, out var search, out var reason, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut, Is.False);
            Assert.That(proof, Is.Null);
            Assert.That(search, Is.Null);
            Assert.That(reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(PassiveCallableVcBuilder.TryBuildWithInvariants(candidate, [], CancellationToken.None), Is.Null);
        }

        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: CancellationToken.None), Is.True);
        Assert.That(failure, Is.EqualTo(WorkerClaimReason.None));
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync(cancellationToken: CancellationToken.None)).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var verified = await solver.VerifyEnsuresAsync(0, cancellationToken: CancellationToken.None);
        Assert.That(verified.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(verified.Reason, Is.EqualTo(WorkerClaimReason.None));
        foreach (var value in new[] { false, true })
        {
            var inputValue = factory.CreateBooleanValue(value);
            var execution = new IrProgramInterpreter(factory).Execute(candidate.Program,
                new Dictionary<IrVarId, IrValue>
                {
                    [input.Entry] = inputValue,
                    [input.Current] = inputValue,
                    [input.Old] = inputValue
                });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(execution.ReturnValue?.Boolean, Is.EqualTo(value));
                Assert.That(execution.ConsumedApproximation, Is.False);
                Assert.That(execution.Unsupported, Is.Null);
                Assert.That(execution.Exception, Is.Null);
            }
        }
    }
}
