using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Converting a lambda to a delegate runs none of its body. It makes a
// delegate: a capturing one is new each time, a non-capturing one is the
// compiler's cached instance. Invoking the delegate is an unknown call, and
// no function may change a variable it captures.
[TestFixture]
public sealed class AnonymousFunctionCreationTests
{
    private const string Types = "using SharpProof.Attributes;\n" +
        "public struct Counter { public int Value; public void Bump() { Value++; } }\n";

    private const string Statics = "static int s_count; static void Increment(ref int value) { value++; } ";

    [TestCase("[EnforcePure] public static System.Func<int,int> M() { return x => x + 1; }")]
    [TestCase("[DoesNotThrow] public static System.Func<int,int> M() { return x => x + 1; }")]
    [TestCase("[EnforcePure] public static System.Func<int,int> M(int k) { if (k < 0) { k = 0; } return x => x + k; }")]
    [TestCase("[DoesNotThrow] public static System.Func<int,int> M(int k) { if (k < 0) { k = 0; } return x => x + k; }")]
    [TestCase("[EnforcePure] public static System.Func<T,int> M<T>(System.Func<T,int> g) { return t => g(t) + 1; }")]
    [TestCase("[EnforcePure] public static System.Func<int,System.Func<int>> M() { return x => { int c = x * 2; return () => c; }; }")]
    [TestCase("[EnforcePure] public static System.Action M() { return () => { s_count++; }; }")]
    [TestCase("[DoesNotThrow] public static System.Action M() { return () => throw new System.InvalidOperationException(); }")]
    [TestCase("[EnforcePure] public static int M(int n) { System.Func<int> f = () => n; return n + 1; }")]
    [TestCase("[EnforcePure] public static System.Func<int> M(int n) { int x = n; x++; return () => x; }")]
    [TestCase("[EnforcePure] public static System.Action<int> M() { return delegate (int x) { s_count = x; }; }")]
    public async Task CreatingADelegateRunsNoneOfTheBody(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync("public static class C { " + Statics + member + " }");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("[EnforcePure] public System.Func<int> M() { return () => field; }")]
    [TestCase("[DoesNotThrow] public System.Func<int> M() { return () => field + 1; }")]
    [TestCase("[EnforcePure] public System.Action M() { return () => { field = 2; }; }")]
    public async Task ReceiverCapturesAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync("public sealed class C { private int field; " + member + " }");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("[EnforcePure] public static System.Func<int> M(int[] a) { a[0] = 1; return () => a.Length; }")]
    [TestCase("[EnforcePure] public static System.Func<int> M() { s_count = 1; return () => s_count; }")]
    [TestCase("[DoesNotThrow] public static System.Func<int,int> M(int k) { if (k < 0) { throw new System.ArgumentOutOfRangeException(\"k\", \"negative\"); } return x => x + k; }")]
    public async Task VisibleEffectsBesideALambdaAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync("public static class C { " + Statics + member + " }");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // The invocation is an unknown call, but no lambda here writes `x`, so the
    // method's own variable keeps its value.
    [TestCase("public static int M(int n) { Contract.Ensures(Contract.Result<int>() == n); int x = n; System.Func<int> f = () => x; f(); return x; }")]
    [TestCase("public static int M(int n) { Contract.Ensures(Contract.Result<int>() == n + 1); int x = n; System.Func<int> f = () => x; x = n + 1; f(); return x; }")]
    public async Task ReadOnlyCapturesKeepTheirValues(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync("public static class C { " + Statics + member + " }");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // An invocation the method cannot see may run a function that changes a
    // captured variable, so these never prove the stale value.
    [TestCase("int x = n; System.Action a = () => x++; a(); return x;")]
    [TestCase("int x = n; System.Action a = () => { x = 0; }; a(); return x;")]
    [TestCase("int x = n; System.Action a = () => Increment(ref x); a(); return x;")]
    [TestCase("int x = n; System.Func<int> f = () => x; System.Action b = () => x--; f(); b(); return x;")]
    [TestCase("int x = n; void Bump() { x++; } System.Func<int> f = () => x; f(); return x;")]
    [TestCase("Counter c = default; c.Value = n; System.Action a = () => c.Bump(); a(); return c.Value;")]
    [TestCase("Counter c = default; c.Value = n; System.Action a = () => { c.Value++; }; a(); return c.Value;")]
    [TestCase("int x = n; System.Action a = () => { ref int r = ref x; r++; }; a(); return x;")]
    [TestCase("int x = n; int y = 0; System.Action a = () => { (x, y) = (y, x); }; a(); return x;")]
    public async Task CapturedWritesStayUnknown(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var result = await ResultAsync("public static class C { " + Statics +
            "public static int M(int n) { Contract.Ensures(Contract.Result<int>() == n); " + body + " } }");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    // A capturing lambda allocates its delegate every time. A non-capturing
    // one allocates only on its first evaluation, and a closure frame is
    // allocated where the captured variable's scope begins, whether or not
    // the lambda is ever reached: neither is a definite allocation.
    [TestCase("public static System.Func<int> M(int n) { if (n < 0) { n = 0; } return () => n; }", true)]
    [TestCase("public System.Func<int> M() { return () => field; }", true)]
    [TestCase("public static System.Func<int,int> M() { return x => x + 1; }", false)]
    [TestCase("public static int M(int n) { if (n != n) { System.Func<int> f = () => n; f(); } return n; }", false)]
    public async Task LambdasAreNeverAllocationFree(string member, bool definite)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync("public sealed class C { private int field; [ZeroAllocations] " + member + " }");
        Assert.That(result.Outcome, Is.EqualTo(definite ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    // Building an expression tree runs library code.
    [TestCase("[EnforcePure] public static System.Linq.Expressions.Expression<System.Func<int>> M() { return () => 1; }")]
    public async Task ExpressionTreesStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync("public static class C { " + member + " }");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var project = new ShadowTestProject(Types + source + "\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
