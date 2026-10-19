using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeEffectModuleAdmissionTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task RequiresValueAndSafetyRespectTheUnchangedDefaultDepth(bool safety, bool beyond)
    {
        var budgets = new WorkerBudgets();
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(WorkerBudgets.DefaultMaximumExpressionDepth));
        var expectedDepth = budgets.MaximumExpressionDepth + (beyond ? 1 : 0);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("entry", factory.IntegerType),
            factory.CreateVariable("current", factory.IntegerType), factory.CreateVariable("old", factory.IntegerType));
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("module-requires-depth");
        builder.Return(block, site);
        IrTerm predicate = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, factory.Variable(parameter.Entry), factory.Integer(0));
        var bound = 1;
        while (IrTermAnalysis.GetDepth(predicate) < expectedDepth)
        {
            predicate = factory.Binary(IrBinaryOperator.AndAlso, predicate,
                factory.Binary(IrBinaryOperator.GreaterThanOrEqual, factory.Variable(parameter.Entry), factory.Integer(bound++)));
        }
        Assert.That(IrTermAnalysis.GetDepth(predicate), Is.EqualTo(expectedDepth));
        var value = safety ? factory.Boolean(true) : predicate;
        var safe = safety ? predicate : factory.Boolean(true);
        var candidate = new PassiveCallableCandidate("effect-module-depth", builder.Build(), [parameter], null,
            [new(value, safe, site)], []);
        // Both clauses use legal factory-owned entry terms. This module limit
        // test makes no claim that compiler lowering admits the over-depth case.
        var result = await NativeEffectSiteVerifier.VerifyEffectContractProjectionAsync(candidate,
            new CompilerEffectConstraintArtifact(), [], budgets, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, beyond ? Is.Null : Is.TypeOf<ProvenOutcome>());
        Assert.That(result.Reason, Is.EqualTo(beyond ? WorkerClaimReason.UnsupportedExpression : WorkerClaimReason.None));
        Assert.That(result.AllocationWitness, Is.Null);
        Assert.That(result.WriteWitness, Is.Null);
        Assert.That(result.LockWitness, Is.Null);
        Assert.That(result.ExceptionWitness, Is.Null);
        if (!beyond)
        {
            Assert.That(result.EntryModel.Keys, Is.EqualTo(ImmutableArray.Create(parameter.Entry)));
            var execution = new IrProgramInterpreter(factory).Execute(candidate.Program, result.EntryModel);
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.Unsupported, Is.Null);
            Assert.That(execution.Exception, Is.Null);
        }
        else
        { Assert.That(result.EntryModel, Is.Empty); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EffectModuleAdmissionRetainsActualReturnCoupling(bool matched)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("effect-module-return-coupling");
        builder.Return(block, site, factory.Boolean(true));
        var result = factory.CreateVariable("result", matched ? factory.BooleanType : factory.IntegerType);
        var candidate = new PassiveCallableCandidate("effect-module-return-coupling", builder.Build(), [], result, [], []);
        var execution = new IrProgramInterpreter(factory).Execute(candidate.Program);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Boolean, Is.True);
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.Unsupported, Is.Null);
        Assert.That(execution.Exception, Is.Null);
        // Typed owned IR is accepted by the builder; the mismatched callable
        // result declaration is rejected by the existing VC admission policy.
        var checkedEffect = await NativeEffectSiteVerifier.VerifyEffectContractProjectionAsync(candidate,
            new CompilerEffectConstraintArtifact(), [], new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(checkedEffect.Outcome, matched ? Is.TypeOf<ProvenOutcome>() : Is.Null);
        Assert.That(checkedEffect.Reason, Is.EqualTo(matched ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody));
        Assert.That(checkedEffect.AllocationWitness, Is.Null);
        Assert.That(checkedEffect.WriteWitness, Is.Null);
        Assert.That(checkedEffect.LockWitness, Is.Null);
        Assert.That(checkedEffect.ExceptionWitness, Is.Null);
    }
}
