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
            public static async Task Spin(Task t) { try { await t; } catch (System.Exception) { } while (true) { } }
            public static async Task Fail() { throw new System.InvalidOperationException("x"); }
            public static async Task<int> Positive(int x) { Contract.Requires(x > 0); return x - 1; }
            public static async Task Unwinds(Box b) { try { throw new System.InvalidOperationException("x"); } finally { b.F = 1; } }
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
    // An awaited task that is complete when created does not suspend.
    [TestCase("[EnforcePure] public static async Task M() { await Task.CompletedTask; await ValueTask.CompletedTask.ConfigureAwait(false); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { return await Task.FromResult(x) + await ValueTask.FromResult(1); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { return await new ValueTask<int>(x).ConfigureAwait(false); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { var y = await Task.FromResult(x).ConfigureAwait(false); return await Inner.Next(y); }")]
    // A call whose task is not awaited runs its body until it suspends; its
    // exceptions fault that task.
    [TestCase("[EnforcePure] public static void M() { _ = Inner.Next(1); }")]
    [TestCase("[EnforcePure] public static Task<int> M(int x) { return Inner.Next(x); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(int x) { var t = Inner.Next(x); return await Inner.Next(x); }")]
    [TestCase("[DoesNotThrow] public static void M() { _ = Inner.Fail(); }")]
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
    [TestCase("[EnforcePure] public static void M() { _ = Inner.Store(); }")]
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
    [TestCase("[ZeroAllocations] public static async Task<int> M(int x) { return await Task.FromResult(x); }")]
    [TestCase("[ZeroAllocations] public static void M() { _ = Inner.Next(1); }")]
    [TestCase("[EnforcePure] public static void M() { _ = Inner.Fail(); }")]
    [TestCase("[EnforcePure] public static void M(Task t) { _ = Inner.Wait(t); }")]
    [TestCase("[EnforcePure] public static async Task<int> M(Task<int> t) { return await Task.FromResult(t).Result; }")]
    [TestCase("[ZeroAllocations] public static async Task<int> M(int x) { return await Inner.Next(x); }")]
    [TestCase("[EffectContract(SharpProofEffect.None, Complete = true)] public static async Task M() { }")]
    [TestCase("public static async Task<int> M(int x) { Contract.Ensures(Contract.Result<Task<int>>() != null); return x; }")]
    [TestCase("[EnforcePure] public static async void M() { }")]
    [TestCase("[EnforcePure] public static async Task M(System.Collections.Generic.IAsyncEnumerable<int> s) { await foreach (var x in s) { } }")]
    [TestCase("[EnforcePure] public static async Task M(System.IAsyncDisposable d) { await using (d) { } }")]
    [TestCase("[EnforcePure] public static async Task M(System.IAsyncDisposable d) { await using var e = d; }")]
    [TestCase("[EnforcePure] public static async System.Collections.Generic.IAsyncEnumerable<int> M() { yield return 1; }")]
    [TestCase("[EnforcePure] public static int M() { return Inner.Next(1).Result; }")]
    [TestCase("[EnforcePure] public static async Task M(Counter c) { await c.Bump(); }")]
    // A detached callee that may suspend is an unknown call.
    [TestCase("[DoesNotThrow] public static Task M(Task t) { return Inner.Wait(t); }")]
    public async Task UnmodeledAsyncBehaviorStaysUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    // SuppressThrowing completes the await without rethrowing the fault, so
    // the store after it runs; only ConfigureAwait(bool) is a plain await.
    [TestCase("[EnforcePure] public static async Task M() { try { await Inner.Fail().ConfigureAwait(System.Threading.Tasks.ConfigureAwaitOptions.SuppressThrowing); Inner.S = 1; } catch (System.InvalidOperationException) { } }")]
    // The callee's finally runs before its task faults, so the caller's filter
    // sees b.F == 1 and the store runs (UnwoundCalleeFaultRunsTheFilteredHandler).
    [TestCase("[EnforcePure] public static async Task M() { var b = new Box(); try { await Inner.Unwinds(b); } catch (System.InvalidOperationException) when (b.F == 1) { Inner.S = 5; } catch (System.InvalidOperationException) { } }")]
    // The caller goes on once Spin suspends, though Spin never ends.
    [TestCase("[EnforcePure] public static void M(Task t) { _ = Inner.Spin(t); Inner.S = 1; }")]
    public async Task FaultsThatTheAwaitMaySeeDifferentlyAreNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [Test]
    public async Task UnwoundCalleeFaultRunsTheFilteredHandler()
    {
        const string source = Types + "public sealed class C { public static async Task M() { var b = new Box(); try { await Inner.Unwinds(b); } " +
            "catch (System.InvalidOperationException) when (b.F == 1) { Inner.S = 5; } catch (System.InvalidOperationException) { } } }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("AsyncFilterRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("AsyncFilterRuntime", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            await (Task)assembly.GetType("C")!.GetMethod("M")!.Invoke(null, null)!;
            Assert.That(assembly.GetType("Inner")!.GetField("S")!.GetValue(null), Is.EqualTo(5));
        }
        finally { context.Unload(); }
    }

    // On an async method DoesNotThrow states that the returned task never
    // faults or is canceled; AllowedExceptions bounds what it may hold.
    [TestCase("[DoesNotThrow] public static async Task M() { }", "Proven")]
    [TestCase("[DoesNotThrow] public static async Task<int> M(int x) { return await Inner.Next(x); }", "Proven")]
    [TestCase("[DoesNotThrow] public static async Task<int> M(int x) { return await Inner.Caught(x); }", "Proven")]
    [TestCase("[DoesNotThrow] public static async Task<int> M(int x) { return await Task.FromResult(x); }", "Proven")]
    [TestCase("[DoesNotThrow] public static async Task M() { throw new System.InvalidOperationException(\"x\"); }", "Refuted")]
    [TestCase("[DoesNotThrow] public static async Task M() { await Inner.Fail(); }", "Refuted")]
    [TestCase("[DoesNotThrow] public static async ValueTask<int> M(int[] a) { return a.Length; }", "Refuted")]
    [TestCase("[AllowedExceptions(typeof(System.InvalidOperationException))] public static async Task M() { throw new System.InvalidOperationException(\"x\"); }", "Proven")]
    [TestCase("[AllowedExceptions(typeof(System.ArgumentException))] public static async Task M() { await Inner.Fail(); }", "Refuted")]
    // An unknown await may fault with anything, which no replay shows.
    [TestCase("[DoesNotThrow] public static async Task M(Task t) { await t; }", "Unknown")]
    [TestCase("[DoesNotThrow] public static async Task M(Task t) { await Inner.Wait(t); }", "Unknown")]
    public async Task ExceptionClaimsDescribeTheReturnedTask(string member, string outcome)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        System.ArgumentNullException.ThrowIfNull(outcome);
        var expected = Enum.Parse<WorkerClaimOutcome>(outcome);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(expected), result.Reason.ToString());
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
