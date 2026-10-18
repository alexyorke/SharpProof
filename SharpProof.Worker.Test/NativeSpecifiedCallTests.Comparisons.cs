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

    [TestCase("int")]
    [TestCase("long")]
    [TestCase("uint")]
    [TestCase("ulong")]
    public async Task DefaultComparerAliasesRetainIntegerSemantics(string type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var declaration = "System.Collections.Generic.Comparer<" + type + ">";
        foreach (var body in new[]
        {
            "var comparer = " + declaration + ".Default; return comparer.Compare(left, right);",
            "var comparer = " + declaration + ".Default; var alias = comparer; return alias.Compare(y: right, x: left);",
            declaration + " comparer; if (left == right) comparer = " + declaration + ".Default; else comparer = " + declaration + ".Default; return comparer.Compare(left, right);",
            "var comparer = left == right ? " + declaration + ".Default : " + declaration + ".Default; return comparer.Compare(left, right);",
            "var comparer = " + declaration + ".Default; return comparer.Compare(left, right == left ? left : right);",
            "var comparer = " + declaration + ".Default; comparer = " + declaration + ".Default; return comparer.Compare(left, right);",
            "var comparer = " + declaration + ".Default; return ((" + declaration + ")comparer).Compare(left, right);"
        })
        { await CheckComparisonContract(type, body, WorkerClaimOutcome.Proven); }
    }

    [TestCase("var comparer = System.Collections.Generic.Comparer<int>.Default; return Compare(comparer, left, right); static int Compare(System.Collections.Generic.Comparer<int> value, int x, int y) => value.Compare(x, y);")]
    [TestCase("return Compare(System.Collections.Generic.Comparer<int>.Default, left, right); static int Compare(System.Collections.Generic.Comparer<int> value, int x, int y) { var alias = value; return Next(alias, x, y); } static int Next(System.Collections.Generic.Comparer<int> value, int x, int y) => value.Compare(x, y);")]
    public async Task DefaultComparerArgumentsSpecializeImmutableSourceParameters(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        await CheckComparisonContract("int", body, WorkerClaimOutcome.Proven);
    }

    [TestCase("var comparer = System.Collections.Generic.Comparer<int>.Default; comparer = System.Collections.Generic.Comparer<int>.Create((x, y) => 42); return comparer.Compare(left, right);")]
    [TestCase("var comparer = left == right ? System.Collections.Generic.Comparer<int>.Default : System.Collections.Generic.Comparer<int>.Create((x, y) => 42); return comparer.Compare(left, right);")]
    [TestCase("var comparer = System.Collections.Generic.Comparer<int>.Default; Change(ref comparer); return comparer.Compare(left, right); static void Change(ref System.Collections.Generic.Comparer<int> value) { value = System.Collections.Generic.Comparer<int>.Create((x, y) => 42); }")]
    [TestCase("var comparer = System.Collections.Generic.Comparer<int>.Default; System.Action change = () => comparer = System.Collections.Generic.Comparer<int>.Create((x, y) => 42); change(); return comparer.Compare(left, right);")]
    [TestCase("var comparer = System.Collections.Generic.Comparer<int>.Default; var other = System.Collections.Generic.Comparer<int>.Create((x, y) => 42); (comparer, other) = (other, comparer); return comparer.Compare(left, right);")]
    [TestCase("return Compare(System.Collections.Generic.Comparer<int>.Default, left, right); static int Compare(System.Collections.Generic.Comparer<int> value, int x, int y) { value = System.Collections.Generic.Comparer<int>.Create((a, b) => 42); return value.Compare(x, y); }")]
    [TestCase("var comparer = System.Collections.Generic.Comparer<int>.Default; ref var alias = ref comparer; alias = System.Collections.Generic.Comparer<int>.Create((x, y) => 42); return comparer.Compare(left, right);")]
    [TestCase("return Compare(System.Collections.Generic.Comparer<int>.Default, left, right); static int Compare(System.Collections.Generic.Comparer<int> value, int x, int y) { ref var alias = ref value; alias = System.Collections.Generic.Comparer<int>.Create((a, b) => 42); return value.Compare(x, y); }")]
    [TestCase("var comparer = System.Collections.Generic.Comparer<int>.Default; ref var reference = ref comparer; reference = System.Collections.Generic.Comparer<int>.Create((x, y) => 42); var alias = comparer; return alias.Compare(left, right);")]
    public async Task UncertainComparerBindingsDoNotGainDefaultSemantics(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        await CheckComparisonContract("int", body, WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task WrongAliasComparisonClaimCannotBeProven()
    {
        await CheckComparisonContract("int", "var comparer = System.Collections.Generic.Comparer<int>.Default; return comparer.Compare(left, right);", WorkerClaimOutcome.Refuted, "42");
    }
    [Test]
    public async Task AliasedDefaultGetterRetainsAllocationAndExceptionEffects()
    {
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations] public static int Target(int left, int right) { var comparer = System.Collections.Generic.Comparer<int>.Default; return comparer.Compare(left, right); }");
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(exceptions.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        Assert.That(allocations.Outcome, Is.Not.TypeOf<ProvenOutcome>());
    }
    [TestCase("var left = values[0]; return System.Collections.Generic.Comparer<T>.Default.Compare(left, left);")]
    [TestCase("var left = values[0]; var comparer = System.Collections.Generic.Comparer<T>.Default; var alias = comparer; return alias.Compare(left, left);")]
    public async Task ConcreteArrayElementsSpecializeGenericDefaultComparison(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length == 2 && values[0] == 17 && values[1] == 37); Contract.Ensures(Contract.Result<int>() == 0); return Compare<int>(values);",
            WorkerClaimOutcome.Proven,
            members: "private static int Compare<T>(System.Collections.Generic.IList<T> values) { " + body + " }");
    }
    [Test]
    public async Task GenericDefaultInitializationPreservesEarlierScalarSnapshots()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length == 2 && values[0] == 17 && values[1] == 37); Contract.Ensures(Contract.Result<int>() == -1); return Compare<int>(values);",
            WorkerClaimOutcome.Proven,
            members: "private static int Compare<T>(System.Collections.Generic.IList<T> values) { var left = values[0]; var right = values[1]; return System.Collections.Generic.Comparer<T>.Default.Compare(left, right); }");
    }
    [Test]
    public async Task DefaultComparerProvenanceHasBoundedCastResolution()
    {
        var receiver = "System.Collections.Generic.Comparer<int>.Default";
        for (var index = 0; index < 70; index++)
        { receiver = "((System.Collections.Generic.Comparer<int>)" + receiver + ")"; }
        await CheckComparisonContract("int", "return " + receiver + ".Compare(left, right);", WorkerClaimOutcome.Unknown);
        await CheckComparisonContract("int", "var comparer = " + receiver + "; return comparer.Compare(left, right);", WorkerClaimOutcome.Unknown);
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
