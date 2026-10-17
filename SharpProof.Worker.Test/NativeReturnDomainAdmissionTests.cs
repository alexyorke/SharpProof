using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeReturnDomainAdmissionTests
{
    [TestCase("Boolean", "Boolean", true)]
    [TestCase("Boolean", "Integer", false)]
    [TestCase("Void", "Void", true)]
    public Task RepresentativeReturnDomainsPreserveModuleAdmission(string actual, string declared, bool admitted)
    { return VerifyAsync(actual, declared, admitted); }

    [TestCase("Integer", "Integer", true)]
    [TestCase("Integer", "Boolean", false)]
    [TestCase("Integer", "Void", false)]
    [TestCase("Boolean", "Void", false)]
    [TestCase("Void", "Integer", false)]
    [TestCase("Void", "Boolean", false)]
    public Task RemainingReturnDomainsPreserveModuleAdmission(string actual, string declared, bool admitted)
    { return VerifyAsync(actual, declared, admitted); }

    private static async Task VerifyAsync(string actual, string declared, bool admitted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("return-domain-admission");
        IrTerm? returned = actual switch
        {
            "Boolean" => factory.Boolean(true),
            "Integer" => factory.Integer(1),
            _ => null
        };
        builder.Return(block, site, returned);
        var program = builder.Build();
        var execution = new IrProgramInterpreter(factory).Execute(program);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.Unsupported, Is.Null);
        Assert.That(execution.Exception, Is.Null);
        Assert.That(execution.ReturnValue == null, Is.EqualTo(actual == "Void"));
        if (actual == "Boolean")
        { Assert.That(execution.ReturnValue!.Boolean, Is.True); }
        else if (actual == "Integer")
        { Assert.That(execution.ReturnValue!.Integer, Is.EqualTo(1)); }
        IrVarId? result = declared == "Void" ? null : factory.CreateVariable("result",
            declared == "Boolean" ? factory.BooleanType : factory.IntegerType);
        var goal = result is { } variable
            ? factory.Binary(IrBinaryOperator.Equal, factory.Variable(variable),
                declared == "Boolean" ? factory.Boolean(true) : factory.Integer(1))
            : factory.Boolean(true);
        // The IR program is typed and owned. Mismatched callable return/result
        // coupling must fail this internal module admission boundary; compiler
        // artifact decoding rejects such coupling before worker enrollment.
        var candidate = new PassiveCallableCandidate("return-domain-admission", program, [], result, [],
            [new(goal, factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason, cancellationToken: CancellationToken.None), Is.EqualTo(admitted));
        Assert.That(reason, Is.EqualTo(admitted ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody));
        Assert.That(plan == null, Is.EqualTo(!admitted));
        if (!admitted)
        { return; }
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync(cancellationToken: CancellationToken.None)).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proof = await solver.VerifyEnsuresAsync(0, cancellationToken: CancellationToken.None);
        Assert.That(proof.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(proof.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
    }
}
