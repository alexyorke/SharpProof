using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;
public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("int")]
    [TestCase("long")]
    [TestCase("uint")]
    [TestCase("ulong")]
    public async Task IntegerCompareToProvesResultAndEffectClaims(string type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        await CheckComparisonContract(type, "return left.CompareTo(right);", WorkerClaimOutcome.Proven);
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations, EnforcePure] public static int Target(" + type + " left, " + type + " right) { return left.CompareTo(right); }");
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        foreach (var result in new[]
        {
            exceptions,
            allocations,
            purity
        }

        )
        {
            Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        }
    }

    [TestCase("int")]
    [TestCase("long")]
    [TestCase("uint")]
    [TestCase("ulong")]
    public async Task DefaultIntegerComparerProvesResultWithoutErasingGetterEffects(string type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        await CheckComparisonContract(type, "return System.Collections.Generic.Comparer<" + type + ">.Default.Compare(y: right, x: left);", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task IntegerComparisonPreservesReceiverAndArgumentEvaluationOrder()
    {
        await CheckComparisonContract("int", "var saved = left; return saved.CompareTo(saved = right);", WorkerClaimOutcome.Proven);
        await CheckComparisonContract("int", "var saved = right; return System.Collections.Generic.Comparer<int>.Default.Compare(y: saved, x: saved = left);", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task CustomComparerDoesNotGainDefaultComparerSemantics()
    {
        await CheckComparisonContract("int", "return System.Collections.Generic.Comparer<int>.Create((x, y) => 42).Compare(left, right);", WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task ComparisonModelRejectsAnIncorrectResultContract()
    {
        await CheckComparisonContract("int", "return left.CompareTo(right);", WorkerClaimOutcome.Refuted, "42");
    }

    [Test]
    public async Task DefaultGetterStillHasConservativeAllocationAndExceptionOutcomes()
    {
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations] public static int Target(int left, int right) { " + "return System.Collections.Generic.Comparer<int>.Default.Compare(left, right); }");
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(exceptions.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        Assert.That(allocations.Outcome, Is.Not.TypeOf<ProvenOutcome>());
    }

    [TestCase("System.Collections.Generic.List<int>")]
    [TestCase("System.Collections.Generic.Dictionary<int, int>")]
    [TestCase("System.Collections.Generic.Queue<int>")]
    [TestCase("System.Collections.Generic.Stack<int>")]
    [TestCase("System.Collections.Generic.ICollection<int>")]
    public async Task CountDoesNotProveStrictPositivityOrAnInterfaceInvariant(string type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var comparison = type == "System.Collections.Generic.ICollection<int>" ? ">= 0" : "> 0";
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target(" + type + " value) { Contract.Requires(value != null); Contract.Ensures(Contract.Result<int>() " + comparison + "); return value.Count; } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code + ": " + error.Message)), Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    private static async Task CheckComparisonContract(string type, string body, WorkerClaimOutcome outcome, string? expected = null)
    {
        global::System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(type);
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target(" + type + " left, " + type + " right) { Contract.Ensures(Contract.Result<int>() == (" + (expected ?? "left < right ? -1 : left > right ? 1 : 0") + ")); " + body + " } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code + ": " + error.Message)), Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(outcome), response.ClaimResults.Single().ToString());
    }
}
