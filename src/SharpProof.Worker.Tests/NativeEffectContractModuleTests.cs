using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeEffectContractModuleTests
{
    private const WorkerEffectSet Reads = WorkerEffectSet.ReadsReceiverState | WorkerEffectSet.ReadsArgumentState |
        WorkerEffectSet.ReadsCapturedState | WorkerEffectSet.ReadsStaticState | WorkerEffectSet.ReadsAmbientState;
    private const WorkerEffectSet Writes = WorkerEffectSet.WritesReceiverState | WorkerEffectSet.WritesArgumentState |
        WorkerEffectSet.WritesCapturedState | WorkerEffectSet.WritesStaticState | WorkerEffectSet.WritesAmbientState;

    [TestCase("io", true, false, "Proven")]
    [TestCase("io", false, false, "Unreplayable")]
    [TestCase("all", true, false, "Proven")]
    [TestCase("all", false, true, "Refuted")]
    public async Task DeclaredMayEffectsAndConcreteLocksRemainDistinct(string facet, bool allowed, bool locked, string expected)
    {
        System.ArgumentNullException.ThrowIfNull(expected);
        System.ArgumentNullException.ThrowIfNull(facet);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var lockSite = factory.CreateOperation("module-lock");
        if (locked)
        {
            var receiver = factory.CreateVariable("monitor", factory.ObjectType);
            builder.Allocate(block, factory.CreateOperation("module-monitor"), factory.ObjectType, receiver);
            builder.Lock(block, lockSite, factory.Variable(receiver));
        }
        var effects = facet == "all" ? IrOpaqueCallEffects.All : IrOpaqueCallEffects.InputOutput;
        var site = factory.CreateOperation(IrOpaqueCallSite.Describe(effects, "EffectModuleBoundary"));
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "EffectModuleBoundary", factory.IntegerType, true, []);
        builder.Call(block, site, null, member, null);
        builder.Return(block, factory.CreateOperation("module-return"));
        var candidate = new PassiveCallableCandidate("effect-contract-module", builder.Build(), [], null, [], []);
        Assert.That(IrOpaqueCallSite.Effects(factory, site), Is.EqualTo(effects));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var admission, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True, admission.ToString());
        // These ordinary module bounds carry no compiler claim or preparation.
        // A forbidden may-call has no invented concrete witness; a real lock does.
        var constraint = new CompilerEffectConstraintArtifact
        {
            AllowedEffects = facet == "all" ? WorkerEffectSet.AllKnown :
                WorkerEffectSet.ReadsAmbientState | WorkerEffectSet.WritesAmbientState,
            AllowedCapabilities = facet == "all" ? WorkerEffectCapabilitySet.AllKnown : WorkerEffectCapabilitySet.IO
        };
        if (!allowed)
        {
            { constraint = constraint with { AllowedCapabilities = constraint.AllowedCapabilities & ~(facet == "all" ? WorkerEffectCapabilitySet.Synchronization : WorkerEffectCapabilitySet.IO) }; }
        }
        Assert.That(WorkerProtocolJson.HasKnownEffects(constraint.AllowedEffects, constraint.AllowedCapabilities), Is.True);
        Assert.That(constraint.AllowedExceptionTypes, Is.Empty);
        var result = await NativeEffectSiteVerifier.VerifyEffectContractProjectionAsync(candidate, constraint, [], new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Reason, Is.EqualTo(expected == "Unreplayable"
            ? WorkerClaimReason.CounterexampleNotReplayable : WorkerClaimReason.None));
        Assert.That(result.Outcome, expected == "Proven" ? Is.TypeOf<ProvenOutcome>() :
            expected == "Refuted" ? Is.TypeOf<RefutedOutcome>() : Is.Null);
        Assert.That(result.LockWitness, expected == "Refuted" ? Is.EqualTo(lockSite) : Is.Null);
        Assert.That(result.WriteWitness, Is.Null);
        Assert.That(result.AllocationWitness, Is.Null);
        Assert.That(result.ExceptionWitness, Is.Null);
        if (expected == "Refuted")
        {
            Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out _, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
            OperationId? replayed = null;
            plan!.ReplayEffects(result.EntryModel, TestContext.CurrentContext.CancellationToken,
                lockPrefixObserver: (instruction, approximation) => { System.ArgumentNullException.ThrowIfNull(instruction, nameof(instruction)); if (!approximation) { replayed ??= instruction.Operation; } });
            Assert.That(replayed, Is.EqualTo(lockSite));
        }
    }

    [TestCase(IrOpaqueCallEffects.Reads, Reads, WorkerEffectCapabilitySet.None, true)]
    [TestCase(IrOpaqueCallEffects.Reads, Reads, WorkerEffectCapabilitySet.None, false)]
    [TestCase(IrOpaqueCallEffects.Writes, Writes, WorkerEffectCapabilitySet.None, true)]
    [TestCase(IrOpaqueCallEffects.Writes, Writes, WorkerEffectCapabilitySet.None, false)]
    [TestCase(IrOpaqueCallEffects.Allocates, WorkerEffectSet.Allocates, WorkerEffectCapabilitySet.None, true)]
    [TestCase(IrOpaqueCallEffects.Allocates, WorkerEffectSet.Allocates, WorkerEffectCapabilitySet.None, false)]
    [TestCase(IrOpaqueCallEffects.Throws, WorkerEffectSet.Throws, WorkerEffectCapabilitySet.None, true)]
    [TestCase(IrOpaqueCallEffects.Throws, WorkerEffectSet.Throws, WorkerEffectCapabilitySet.None, false)]
    [TestCase(IrOpaqueCallEffects.Synchronizes, WorkerEffectSet.Synchronizes, WorkerEffectCapabilitySet.Synchronization, true)]
    [TestCase(IrOpaqueCallEffects.Synchronizes, WorkerEffectSet.Synchronizes, WorkerEffectCapabilitySet.Synchronization, false)]
    [TestCase(IrOpaqueCallEffects.NativeCode, WorkerEffectSet.UsesNativeCode, WorkerEffectCapabilitySet.NativeInterop, true)]
    [TestCase(IrOpaqueCallEffects.NativeCode, WorkerEffectSet.UsesNativeCode, WorkerEffectCapabilitySet.NativeInterop, false)]
    [TestCase(IrOpaqueCallEffects.Reflection, WorkerEffectSet.UsesReflection, WorkerEffectCapabilitySet.Reflection, true)]
    [TestCase(IrOpaqueCallEffects.Reflection, WorkerEffectSet.UsesReflection, WorkerEffectCapabilitySet.Reflection, false)]
    [TestCase(IrOpaqueCallEffects.Nondeterminism, WorkerEffectSet.UsesNondeterminism, WorkerEffectCapabilitySet.Randomness, true)]
    [TestCase(IrOpaqueCallEffects.Nondeterminism, WorkerEffectSet.UsesNondeterminism, WorkerEffectCapabilitySet.Randomness, false)]
    [TestCase(IrOpaqueCallEffects.InputOutput | IrOpaqueCallEffects.Nondeterminism,
        WorkerEffectSet.ReadsAmbientState | WorkerEffectSet.WritesAmbientState | WorkerEffectSet.UsesNondeterminism,
        WorkerEffectCapabilitySet.IO | WorkerEffectCapabilitySet.Randomness, true)]
    [TestCase(IrOpaqueCallEffects.InputOutput | IrOpaqueCallEffects.Nondeterminism,
        WorkerEffectSet.ReadsAmbientState | WorkerEffectSet.WritesAmbientState | WorkerEffectSet.UsesNondeterminism,
        WorkerEffectCapabilitySet.IO | WorkerEffectCapabilitySet.Randomness, false)]
    public async Task EveryDeclaredMayFacetRequiresItsApplicableEffectBounds(
        int facetValue, WorkerEffectSet bound, WorkerEffectCapabilitySet capabilities, bool allowed)
    {
        var facet = (IrOpaqueCallEffects)facetValue;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation(IrOpaqueCallSite.Describe(facet, "EffectModuleBoundary"));
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "EffectModuleBoundary", factory.IntegerType, true, []);
        builder.Call(block, site, null, member, null);
        builder.Return(block, factory.CreateOperation("module-return"));
        var candidate = new PassiveCallableCandidate("effect-contract-module", builder.Build(), [], null, [], []);
        Assert.That(IrOpaqueCallSite.Effects(factory, site), Is.EqualTo(facet));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var admission, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True, admission.ToString());
        var constraint = new CompilerEffectConstraintArtifact
        {
            AllowedEffects = allowed ? bound : WorkerEffectSet.None,
            AllowedCapabilities = capabilities
        };
        Assert.That(WorkerProtocolJson.HasKnownEffects(constraint.AllowedEffects, constraint.AllowedCapabilities), Is.True);
        var result = await NativeEffectSiteVerifier.VerifyEffectContractProjectionAsync(candidate, constraint, [], new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, allowed ? Is.TypeOf<ProvenOutcome>() : Is.Null);
        Assert.That(result.Reason, Is.EqualTo(allowed ? WorkerClaimReason.None : WorkerClaimReason.CounterexampleNotReplayable));
        Assert.That(result.LockWitness, Is.Null);
        Assert.That(result.AllocationWitness, Is.Null);
        Assert.That(result.WriteWitness, Is.Null);
        Assert.That(result.ExceptionWitness, Is.Null);
        Assert.That(result.EntryModel, Is.Empty);
    }
}
