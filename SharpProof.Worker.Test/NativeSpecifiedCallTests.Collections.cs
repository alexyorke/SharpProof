using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("System.Collections.Generic.List<int>")]
    [TestCase("System.Collections.Generic.Dictionary<int, int>")]
    [TestCase("System.Collections.Generic.Queue<int>")]
    [TestCase("System.Collections.Generic.Stack<int>")]
    public async Task ConcreteCountGettersProveAllocationAndExceptionClaims(string type)
    {
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations] public static int Target(" + type +
            " value) { Contract.Requires(value != null); return value.Count; }");
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exceptions.Outcome, Is.TypeOf<ProvenOutcome>(), exceptions.Reason.ToString());
            Assert.That(allocations.Outcome, Is.TypeOf<ProvenOutcome>(), allocations.Reason.ToString());
        }
    }

    [TestCase("System.Collections.Generic.ICollection<int>", "value.Count")]
    [TestCase("System.Collections.Generic.IList<int>", "value[0]")]
    [TestCase("System.Collections.Generic.Dictionary<int, int>", "value[0]")]
    [TestCase("System.Collections.Generic.List<int>", "value[-1]")]
    public async Task AbstractAndPotentiallyThrowingGettersDoNotGainExceptionFreedom(string type, string expression)
    {
        var preparation = Prepare("[DoesNotThrow] public static int Target(" + type +
            " value) { Contract.Requires(value != null); return " + expression + "; }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [TestCase("System.Collections.Generic.List<int>")]
    [TestCase("System.Collections.Generic.Dictionary<int, int>")]
    [TestCase("System.Collections.Generic.Queue<int>")]
    [TestCase("System.Collections.Generic.Stack<int>")]
    public async Task CountGettersProveNonnegativeResults(string type)
    {
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target(" +
            type + " value) { Contract.Requires(value != null); Contract.Ensures(Contract.Result<int>() >= 0); return value.Count; } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors.Select(error => error.Code + ": " + error.Message), Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), response.ClaimResults.Single().ToString());
    }

    [Test]
    public async Task AListGetterSpecDoesNotProveSetterPurity()
    {
        var preparation = Prepare("[EnforcePure] public static void Target(System.Collections.Generic.List<int> value) { " +
            "Contract.Requires(value != null); value[0] = 42; }");
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [Test]
    public async Task ListIndexerRetainsConservativeNativeExceptionAndAllocationOutcomes()
    {
        var preparation = Prepare("[AllowedExceptions(typeof(System.ArgumentOutOfRangeException)), ZeroAllocations] " +
            "public static int Target(System.Collections.Generic.List<int> value, int index) { " +
            "Contract.Requires(value != null); return value[index]; }");
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            // Native opaque-call IR preserves a may-throw bit, not the catalog's
            // named exception set. Portable projection is tested independently.
            Assert.That(exceptions.Outcome, Is.Null);
            Assert.That(exceptions.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(allocations.Outcome, Is.Not.TypeOf<ProvenOutcome>(), allocations.Reason.ToString());
        }
    }
}
