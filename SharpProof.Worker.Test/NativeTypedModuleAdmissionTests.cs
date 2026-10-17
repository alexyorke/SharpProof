using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeTypedModuleAdmissionTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task LegalSequenceTypesRespectTheNativeModuleAdmissionDomain(bool nested, bool resultBinding)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flat = factory.GetOrCreateSequenceType(factory.IntegerType);
        var type = nested ? factory.GetOrCreateSequenceType(flat) : flat;
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var site = factory.CreateOperation("typed-module-return");
        var binding = new PassiveParameterBinding(factory.CreateVariable("entry", type),
            factory.CreateVariable("current", type), factory.CreateVariable("old", type));
        var returned = resultBinding ? factory.CreateVariable("result", type) : (IrVarId?)null;
        builder.Return(block, site, resultBinding ? factory.Null(type) : null);
        var candidate = new PassiveCallableCandidate("typed-module", builder.Build(),
            resultBinding ? [] : [binding], returned, [], []);
        Assert.That(factory.GetTypeInfo(type).ElementType, Is.EqualTo(nested ? flat : factory.IntegerType));
        var built = PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(built, Is.EqualTo(!nested));
            Assert.That(failure, Is.EqualTo(nested ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
            Assert.That(plan == null, Is.EqualTo(nested));
        }
        var result = await NativeEffectSiteVerifier.VerifyCapabilityProjectionAsync(candidate,
            WorkerEffectCapabilitySet.None, new WorkerBudgets(), cancellationToken: CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, nested ? Is.Null : Is.TypeOf<ProvenOutcome>());
            Assert.That(result.Reason, Is.EqualTo(nested ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
            Assert.That(result.LockWitness, Is.Null);
            Assert.That(result.WriteWitness, Is.Null);
            Assert.That(result.AllocationWitness, Is.Null);
        }
    }

}
