using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Async methods returning Task, Task<T>, ValueTask or ValueTask<T>. Calling
// one runs its body up to the first await of an operation that has not
// completed; an awaited source async callee runs its body in place; any other
// await is an unknown call; an exception that leaves the root faults its task
// and may later run UnobservedTaskException handlers. Effect claims describe
// the call's effects; exception claims, postconditions and resource bounds
// stay Unknown. See SEMANTICS.md, async methods.
[TestFixture]
public sealed class AsyncMethodTests
{
    private const string Types = """
        using System.Threading.Tasks;
        using SharpProof.Attributes;
        public sealed class Box { public int F; public int G; }
        public struct Counter { public int N; public async Task Bump() { N++; } }
        public static class Inner
        {
            public static int S;
            public static async Task<int> Next(int x) { return x + 1; }
            public static async Task Nothing() { }
            public static async ValueTask<int> Twice(int x) { return x * 2; }
            public static async Task Store() { S = 1; }
            public static async Task<int> Awaits(Task<int> t) { return await t; }
            public static async Task Wait(Task t) { await t; }
            public static async Task Fail() { throw new System.InvalidOperationException("x"); }
            public static async Task<int> Positive(int x) { Contract.Requires(x > 0); return x - 1; }
            public static async Task<int> Caught(int x) { try { throw new System.InvalidOperationException("x"); } catch (System.InvalidOperationException) { return x; } }
        }
        """;

    // Each body's whole execution has no effect a caller can observe.
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { return x + 1; }")]
    [TestCase("[EnforcePure] public static async Task M() { }")]
    [TestCase("[EnforcePure] public static async ValueTask<int> M(int x) => x * 2;")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { Contract.Requires(x > 0); return x - 1; }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { return await Inner.Next(x); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { return await Inner.Next(x).ConfigureAwait(false); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { var y = await Inner.Twice(x); return y + await Inner.Next(y); }")]
    [TestCase("[EnforcePure] public static async Task M() { await Inner.Nothing(); await Inner.Nothing().ConfigureAwait(true); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { Contract.Requires(x > 1); return await Inner.Positive(x); }")]
    [TestCase("[EnforcePure] public async Task<int> M(int x) { return await Inner.Next(x); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { return await Inner.Caught(x); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { try { await Inner.Fail(); } catch (System.InvalidOperationException) { } return x; }")]
    [TestCase("[EffectContract(SharpProofEffect.Allocates, Complete = true)] public static async Task<int> M(int x) { return await Inner.Next(x); }")]
    public async Task PureAsyncBodiesAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // A store the body makes before it could suspend, directly or in an
    // awaited callee, happens within the call.
    [TestCase("[EnforcePure] public static async Task M() { Inner.S = 2; }")]
    [TestCase("[EnforcePure] public static async Task M(Box b) { if (b != null) { b.F = 1; } }")]
    [TestCase("[EnforcePure] public static async Task M() { await Inner.Store(); }")]
    [TestCase("[EnforcePure] public static async Task M() { await Inner.Nothing(); Inner.S = 3; }")]
    [TestCase("[ZeroAllocations] public static async Task M() { var o = new object(); }")]
    public async Task ObservableEffectsAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // Unknown awaitables, suspension and resumption, a fault that nothing may
    // observe, the returned task's allocation, and every exception,
    // postcondition and resource claim stay Unknown, never Proven or Refuted.
    [TestCase("[EnforcePure] public static async Task M(Task t) { await t; }")]
    [TestCase("[EnforcePure] public static async Task M(Task t) { await t.ConfigureAwait(false); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(ValueTask<int> t) { return await t; }")]
    [TestCase("[EnforcePure] public static async Task M(System.IO.Stream s) { await s.FlushAsync(); }")]
    [TestCase("[EnforcePure] public static async Task M() { await Task.Yield(); }")]
    [TestCase("[EnforcePure] public static async Task M(Task t) { await Inner.Wait(t); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(Task<int> t) { return await Inner.Awaits(t).ConfigureAwait(false); }")]
    [TestCase("[EnforcePure] public static async Task M(Box b, Task t) { if (b.F != 0) { return; } await Inner.Wait(t); if (b.F != 0) { b.G = 1; } }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int[] a) { return a[0]; }")]
    [TestCase("[EnforcePure] public static async Task M() { throw new System.InvalidOperationException(\"x\"); }")]
    [TestCase("[EnforcePure] public static async Task M() { await Inner.Fail(); }")]
    [TestCase("[EnforcePure] public static async Task M(Task t) { await t; throw new System.InvalidOperationException(\"x\"); }")]
    [TestCase("[EnforcePure] public static async Task M(System.Func<Task> f) { await f(); }")]
    [TestCase("[EnforcePure] public static async Task M() { System.Func<Task> f = async () => { Inner.S = 1; }; await f(); }")]
    [TestCase("[ZeroAllocations] public static async Task M() { }")]
    [TestCase("[ZeroAllocations] public static async Task<int> M(int x) { return await Inner.Next(x); }")]
    [TestCase("[EffectContract(SharpProofEffect.None, Complete = true)] public static async Task M() { }")]
    [TestCase("[DoesNotThrow] public static async Task M() { }")]
    [TestCase("[DoesNotThrow] public static async Task M() { throw new System.InvalidOperationException(\"x\"); }")]
    [TestCase("[DoesNotThrow] public static async Task M(Task t) { await t; }")]
    [TestCase("[AllowedExceptions(typeof(System.InvalidOperationException))] public static async Task M() { throw new System.InvalidOperationException(\"x\"); }")]
    [TestCase("public static async Task<int> M(int x) { Contract.Ensures(Contract.Result<Task<int>>() != null); return x; }")]
    [TestCase("[EnforcePure] public static async void M() { }")]
    [TestCase("[EnforcePure] public static async Task M(System.Collections.Generic.IAsyncEnumerable<int> s) { await foreach (var x in s) { } }")]
    [TestCase("[EnforcePure] public static async Task M(System.IAsyncDisposable d) { await using (d) { } }")]
    [TestCase("[EnforcePure] public static async Task M(System.IAsyncDisposable d) { await using var e = d; }")]
    [TestCase("[EnforcePure] public static async System.Collections.Generic.IAsyncEnumerable<int> M() { yield return 1; }")]
    [TestCase("[EnforcePure] public static void M() { _ = Inner.Next(1); }")]
    [TestCase("[EnforcePure] public static int M() { return Inner.Next(1).Result; }")]
    [TestCase("[EnforcePure] public static Task<int> M(int x) { return Inner.Next(x); }")]
    [TestCase("[EnforcePure] public static async Task M(Counter c) { await c.Bump(); }")]
    public async Task UnmodeledAsyncBehaviorStaysUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    // An exception claim on an async method states nothing about its task.
    [TestCase("[DoesNotThrow] public static async Task M() { }")]
    [TestCase("[DoesNotThrow] public static async Task<int> M(int x) { return await Inner.Next(x); }")]
    public async Task ExceptionClaimsAreUnsupported(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That((result.Outcome, result.Reason), Is.EqualTo((WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedContract)));
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var project = new ShadowTestProject(Types + "\npublic sealed class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
