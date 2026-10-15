using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// This verifies the maintained Total IR module API, not compiler artifacts or
// published C# claims. All terms, events and members belong to one factory.
[TestFixture]
public sealed class NativeOpaqueCapabilityModuleTests
{
    [TestCase("synchronization", false, true)]
    [TestCase("synchronization", true, true)]
    [TestCase("synchronization", true, false)]
    [TestCase("native", false, true)]
    [TestCase("native", true, true)]
    [TestCase("native", true, false)]
    [TestCase("reflection", false, true)]
    [TestCase("reflection", true, true)]
    [TestCase("reflection", true, false)]
    [TestCase("randomness", false, true)]
    [TestCase("randomness", true, true)]
    [TestCase("randomness", true, false)]
    [TestCase("combined", false, true)]
    [TestCase("combined", true, true)]
    [TestCase("combined", true, false)]
    [TestCase("native-reflection", false, true)]
    [TestCase("native-reflection", true, true)]
    [TestCase("native-reflection", true, false)]
    [TestCase("io-randomness", false, true)]
    [TestCase("io-randomness", true, true)]
    [TestCase("io-randomness", true, false)]
    public async Task LegalMayFacetsPreserveBoundsAndConcreteLockReplay(
        string facet, bool locked, bool synchronizationAllowed)
    {
        var (effects, capabilities) = Facets(facet);
        var (candidate, callSite, lockSite) = CreateModule(effects, locked);
        var decoded = IrOpaqueCallSite.Effects(candidate.Factory, callSite);
        Assert.That(decoded, Is.EqualTo(effects));
        Assert.That(IrOpaqueCallSite.Capabilities(decoded), Is.Null);
        Assert.That(NativeEffectSiteVerifier.Capabilities(decoded), Is.EqualTo(capabilities));
        var allowed = synchronizationAllowed ? capabilities | WorkerEffectCapabilitySet.Synchronization
            : capabilities & ~WorkerEffectCapabilitySet.Synchronization;
        var result = await NativeEffectSiteVerifier.VerifyCapabilityProjectionAsync(candidate, allowed, new WorkerBudgets());
        var refuted = locked && !synchronizationAllowed;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, refuted ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>(),
                result.Reason.ToString());
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(result.LockWitness, refuted ? Is.EqualTo(lockSite) : Is.Null);
            Assert.That(result.AllocationWitness, Is.Null);
            Assert.That(result.WriteWitness, Is.Null);
        }
    }

    [TestCase("io")]
    [TestCase("synchronization")]
    [TestCase("native")]
    [TestCase("reflection")]
    [TestCase("randomness")]
    [TestCase("combined")]
    [TestCase("native-reflection")]
    [TestCase("io-randomness")]
    public async Task MayCallFacetsCannotManufactureAConcreteRefutation(string facet)
    {
        var (effects, _) = Facets(facet);
        var (candidate, _, _) = CreateModule(effects, locked: false);
        var synchronizationFacet = (effects & IrOpaqueCallEffects.Synchronizes) != 0;
        // Permit synchronization only when it is not the facet being rejected.
        // The maintained solver deliberately reports uncertainty for may calls.
        var allowed = synchronizationFacet ? WorkerEffectCapabilitySet.None : WorkerEffectCapabilitySet.Synchronization;
        var result = await NativeEffectSiteVerifier.VerifyCapabilityProjectionAsync(candidate, allowed, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(synchronizationFacet
                ? WorkerClaimReason.CounterexampleNotReplayable : WorkerClaimReason.UnsupportedBody));
            Assert.That(result.LockWitness, Is.Null);
            Assert.That(result.AllocationWitness, Is.Null);
            Assert.That(result.WriteWitness, Is.Null);
            Assert.That(result.EntryModel, Is.Empty);
        }
    }

    private static (IrOpaqueCallEffects Effects, WorkerEffectCapabilitySet Capabilities) Facets(string facet)
    {
        return facet switch
        {
            "io" => (IrOpaqueCallEffects.InputOutput, WorkerEffectCapabilitySet.IO),
            "synchronization" => (IrOpaqueCallEffects.Synchronizes, WorkerEffectCapabilitySet.Synchronization),
            "native" => (IrOpaqueCallEffects.NativeCode, WorkerEffectCapabilitySet.NativeInterop),
            "reflection" => (IrOpaqueCallEffects.Reflection, WorkerEffectCapabilitySet.Reflection),
            "randomness" => (IrOpaqueCallEffects.Nondeterminism, WorkerEffectCapabilitySet.Randomness),
            "native-reflection" => (IrOpaqueCallEffects.NativeCode | IrOpaqueCallEffects.Reflection,
                WorkerEffectCapabilitySet.NativeInterop | WorkerEffectCapabilitySet.Reflection),
            "io-randomness" => (IrOpaqueCallEffects.InputOutput | IrOpaqueCallEffects.Nondeterminism,
                WorkerEffectCapabilitySet.IO | WorkerEffectCapabilitySet.Randomness),
            "combined" => (IrOpaqueCallEffects.InputOutput | IrOpaqueCallEffects.Synchronizes |
                IrOpaqueCallEffects.NativeCode | IrOpaqueCallEffects.Reflection | IrOpaqueCallEffects.Nondeterminism,
                WorkerEffectCapabilitySet.IO | WorkerEffectCapabilitySet.Synchronization | WorkerEffectCapabilitySet.NativeInterop |
                WorkerEffectCapabilitySet.Reflection | WorkerEffectCapabilitySet.Randomness),
            _ => throw new ArgumentException("Unknown module test facet.", nameof(facet))
        };
    }

    private static (PassiveCallableCandidate Candidate, OperationId CallSite, OperationId LockSite) CreateModule(
        IrOpaqueCallEffects effects, bool locked)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var lockSite = factory.CreateOperation("module-lock");
        if (locked)
        {
            var receiver = factory.CreateVariable("monitor", factory.ObjectType);
            builder.Allocate(block, factory.CreateOperation("module-monitor-allocation"), factory.ObjectType, receiver);
            builder.Lock(block, lockSite, factory.Variable(receiver));
        }
        var callSite = factory.CreateOperation(IrOpaqueCallSite.Describe(effects, "ModuleBoundary"));
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "ModuleBoundary", factory.IntegerType, true, []);
        builder.Call(block, callSite, null, member, null);
        builder.Return(block, factory.CreateOperation("module-return"));
        var program = builder.Build();
        var candidate = new PassiveCallableCandidate("opaque-capability-module", program, [], null, [], []);
        Assert.That(ReferenceEquals(program.Factory, factory), Is.True);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var failure), Is.True, failure.ToString());
        return (candidate, callSite, lockSite);
    }

    [TestCase(false, true, "Proven")]
    [TestCase(true, true, "Proven")]
    [TestCase(true, false, "Refuted")]
    public async Task InferredIoMayFacetAndConcreteLockHaveIndependentNativeBounds(
        bool locked, bool synchronizationAllowed, string expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var lockSite = factory.CreateOperation("module-lock");
        if (locked)
        {
            var receiver = factory.CreateVariable("monitor", factory.ObjectType);
            builder.Allocate(block, factory.CreateOperation("module-monitor-allocation"), factory.ObjectType, receiver);
            builder.Lock(block, lockSite, factory.Variable(receiver));
        }
        var callSite = factory.CreateOperation(IrOpaqueCallSite.Describe(IrOpaqueCallEffects.InputOutput, "ModuleBoundary"));
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "ModuleBoundary", factory.IntegerType, true, []);
        builder.Call(block, callSite, null, member, null);
        builder.Return(block, factory.CreateOperation("module-return"));
        var program = builder.Build();
        var candidate = new PassiveCallableCandidate("opaque-capability-module", program, [], null, [], []);
        var decodedEffects = IrOpaqueCallSite.Effects(factory, callSite);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReferenceEquals(program.Factory, factory), Is.True);
            Assert.That(decodedEffects, Is.EqualTo(IrOpaqueCallEffects.InputOutput));
            Assert.That(IrOpaqueCallSite.Capabilities(decodedEffects), Is.Null);
            Assert.That(NativeEffectSiteVerifier.Capabilities(decodedEffects), Is.EqualTo(WorkerEffectCapabilitySet.IO));
            Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var failure), Is.True, failure.ToString());
        }
        var allowed = WorkerEffectCapabilitySet.IO | (synchronizationAllowed
            ? WorkerEffectCapabilitySet.Synchronization : WorkerEffectCapabilitySet.None);
        var result = await NativeEffectSiteVerifier.VerifyCapabilityProjectionAsync(candidate, allowed, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, expected == "Proven" ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(),
                result.Reason.ToString());
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(result.LockWitness, expected == "Refuted" ? Is.EqualTo(lockSite) : Is.Null);
            Assert.That(result.AllocationWitness, Is.Null);
            Assert.That(result.WriteWitness, Is.Null);
        }
    }
}
