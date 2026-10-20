using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeCandidateSemanticsAdmissionTests
{
    [TestCase(IrExecutionSemantics.Legacy)]
    [TestCase(IrExecutionSemantics.Total)]
    public async Task PassiveCandidateAdmitsOnlyTheTotalModuleDomain(IrExecutionSemantics semantics)
    {
        var factory = new IrFactory(semantics);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("candidate-semantics-admission");
        var result = factory.CreateVariable("result", factory.IntegerType);
        builder.Return(block, site, factory.Integer(1));
        var program = builder.Build();
        var execution = new IrProgramInterpreter(factory).Execute(program);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue?.Integer, Is.EqualTo(1));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.Unsupported, Is.Null);
            Assert.That(execution.Exception, Is.Null);
        }
        PassiveCallableCandidate CreateCandidate()
        {
            return new("candidate-semantics-admission", program, [], result, [],
                [new(factory.Binary(IrBinaryOperator.Equal, factory.Variable(result), factory.Integer(1)), factory.Boolean(true), site)]);
        }
        if (semantics == IrExecutionSemantics.Legacy)
        {
            var failure = Assert.Throws<ArgumentException>(new Action(() => CreateCandidate()));
            Assert.That(failure!.ParamName, Is.EqualTo("program"));
            Assert.That(failure.Message, Does.Contain("requires Total IR"));
            return;
        }
        var candidate = CreateCandidate();
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.None));
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync(cancellationToken: TestContext.CurrentContext.CancellationToken)).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proof = await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(proof.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(proof.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
    }
}
