using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeConstructionWorkAdmissionTests
{
    [Test]
    public async Task DominanceWorkRespectsDefaultsBelowTheBlockLimit()
    {
        var small = DispatcherLoops(3);
        var large = DispatcherLoops(300);
        foreach (var candidate in new[] { small, large })
        {
            Assert.That(candidate.Program.Blocks.Length, Is.LessThan(PassiveCallableVcBuilder.MaximumSteps));
            AssertReturned(new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program));
        }
        Assert.That(PassiveLoopCutter.Loops(small, TestContext.CurrentContext.CancellationToken), Has.Length.EqualTo(3));
        Assert.That(PassiveLoopCutter.Loops(large, TestContext.CurrentContext.CancellationToken), Is.Empty);
        Assert.That(PassiveLoopCutter.TryCreate(large, out var proof, out var search, out var reason, TestContext.CurrentContext.CancellationToken), Is.False);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proof, Is.Null);
            Assert.That(search, Is.Null);
            Assert.That(reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(PassiveLoopCutter.TryEncodeWithInvariants(large, [], TestContext.CurrentContext.CancellationToken), Is.Null);
        }
        await AssertNativeProofAsync(small);
    }

    [Test]
    public async Task TypedHeapOverlayWorkRespectsDefaultsAfterLoopEncoding()
    {
        var small = HeapStores(1);
        var large = HeapStores(725);
        foreach (var candidate in new[] { small, large })
        {
            Assert.That(candidate.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).Count(),
                Is.LessThan(PassiveCallableVcBuilder.MaximumSteps));
            AssertReturned(new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program));
            Assert.That(PassiveLoopCutter.Loops(candidate, TestContext.CurrentContext.CancellationToken), Has.Length.EqualTo(1));
            Assert.That(PassiveLoopCutter.TryEncodeWithInvariants(candidate, [], TestContext.CurrentContext.CancellationToken), Is.Not.Null);
        }
        Assert.That(PassiveCallableVcBuilder.TryBuildWithInvariants(small, [], TestContext.CurrentContext.CancellationToken), Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuildWithInvariants(large, [], TestContext.CurrentContext.CancellationToken), Is.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(large, out var plan, out var reason, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.False);
        Assert.That(plan, Is.Null);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        await AssertNativeProofAsync(small);
    }

    private static PassiveCallableCandidate DispatcherLoops(int count)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var site = factory.CreateOperation("dominance-work-admission");
        var dispatch = Enumerable.Range(0, count + 1).Select(_ => builder.CreateBlock()).ToImmutableArray();
        var headers = Enumerable.Range(0, count).Select(_ => builder.CreateBlock()).ToImmutableArray();
        var latches = Enumerable.Range(0, count).Select(_ => builder.CreateBlock()).ToImmutableArray();
        for (var index = 0; index < count; index++)
        {
            builder.Branch(dispatch[index], site, factory.Boolean(false), headers[index], dispatch[index + 1]);
            builder.Branch(headers[index], site, factory.Boolean(false), latches[index], dispatch[index + 1]);
            builder.Goto(latches[index], site, headers[index]);
        }
        builder.Return(dispatch[^1], site);
        return new PassiveCallableCandidate("dominance-work-admission", builder.Build(), [], null, [],
            [new(factory.Boolean(true), factory.Boolean(true), site)]);
    }

    private static PassiveCallableCandidate HeapStores(int count)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var site = factory.CreateOperation("overlay-work-admission");
        var storageType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var storage = factory.CreateVariable("storage", storageType);
        var entry = builder.CreateBlock("entry");
        var header = builder.CreateBlock("header");
        var body = builder.CreateBlock("body");
        var exit = builder.CreateBlock("exit");
        builder.Allocate(entry, site, storageType, storage, factory.Integer(1));
        builder.Goto(entry, site, header);
        builder.Branch(header, site, factory.Boolean(false), body, exit);
        for (var index = 0; index < count; index++)
        { builder.ElementStore(body, site, factory.Variable(storage), factory.Integer(0), factory.Integer(0)); }
        builder.Goto(body, site, header);
        builder.Return(exit, site);
        return new PassiveCallableCandidate("overlay-work-admission", builder.Build(), [], null, [],
            [new(factory.Boolean(true), factory.Boolean(true), site)]);
    }

    private static void AssertReturned(IrProgramExecutionResult result)
    {
        System.ArgumentNullException.ThrowIfNull(result);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(result.ConsumedApproximation, Is.False);
            Assert.That(result.Unsupported, Is.Null);
            Assert.That(result.Exception, Is.Null);
        }
    }

    private static async Task AssertNativeProofAsync(PassiveCallableCandidate candidate)
    {
        System.ArgumentNullException.ThrowIfNull(candidate);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.None));
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync(cancellationToken: TestContext.CurrentContext.CancellationToken)).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var result = await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
    }
}
