using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeModuleExceptionEvidenceTests
{
    [TestCase("explicit", "arithmetic", "Unreplayable")]
    [TestCase("explicit", "none", "Refuted")]
    [TestCase("overflow", "arithmetic", "Proven")]
    [TestCase("overflow", "argument", "Refuted")]
    public async Task GenericExplicitEvidenceCannotAssertANarrowRuntimeType(string thrown, string allowed, string expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var exit = builder.CreateBlock();
        // An ordinary owned operation is legal IR without compiler type metadata.
        var site = factory.CreateOperation("module-throw");
        var kind = thrown == "explicit" ? IrExceptionKind.Explicit : IrExceptionKind.Overflow;
        builder.Throw(block, site, kind, exit);
        builder.ExceptionalExit(exit, site);
        var candidate = new PassiveCallableCandidate("exception-evidence-module", builder.Build(), [], null, [], []);
        var execution = new IrProgramInterpreter(factory).Execute(candidate.Program);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(kind));
        Assert.That(execution.Exception.Site, Is.EqualTo(site));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.Unsupported, Is.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var admission, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True, admission.ToString());
        var constraint = new CompilerEffectConstraintArtifact
        {
            AllowedEffects = WorkerEffectSet.Allocates | (allowed == "none" ? WorkerEffectSet.None : WorkerEffectSet.Throws),
            AllowedExceptionTypes = allowed == "none" ? [] :
                [allowed == "arithmetic" ? "System.ArithmeticException" : "System.ArgumentException"]
        };
        Assert.That(WorkerProtocolJson.HasKnownEffects(constraint.AllowedEffects, constraint.AllowedCapabilities), Is.True);
        IrExceptionKind[] kinds = allowed == "arithmetic" ? [IrExceptionKind.DivideByZero, IrExceptionKind.Overflow] :
            allowed == "argument" ? [IrExceptionKind.Argument] : [];
        var result = await NativeEffectSiteVerifier.VerifyEffectContractProjectionAsync(candidate, constraint, [.. kinds], new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, expected == "Proven" ? Is.TypeOf<ProvenOutcome>() :
            expected == "Refuted" ? Is.TypeOf<RefutedOutcome>() : Is.Null);
        Assert.That(result.Reason, Is.EqualTo(expected == "Unreplayable"
            ? WorkerClaimReason.CounterexampleNotReplayable : WorkerClaimReason.None));
        Assert.That(result.ExceptionWitness == null, Is.EqualTo(expected != "Refuted"));
        if (expected == "Refuted")
        {
            Assert.That(result.ExceptionWitness!.Kind, Is.EqualTo(kind));
            Assert.That(result.ExceptionWitness.Site, Is.EqualTo(site));
            var replay = plan!.ReplayException(result.EntryModel, TestContext.CurrentContext.CancellationToken);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.Exception!.Kind, Is.EqualTo(kind));
            Assert.That(replay.Exception.Site, Is.EqualTo(site));
        }
        Assert.That(result.AllocationWitness, Is.Null);
        Assert.That(result.WriteWitness, Is.Null);
        Assert.That(result.LockWitness, Is.Null);
    }
}
