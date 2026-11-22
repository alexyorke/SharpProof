using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

// The lowering of admitted async roots: see RoslynTotalProgramLowerer.Async.cs.
[TestFixture]
public sealed class AsyncLoweringTests
{
    private const string Helpers = """
        public static class Inner
        {
            public static async System.Threading.Tasks.Task<int> Next(int x) { return x + 1; }
            public static async System.Threading.Tasks.Task Wait(System.Threading.Tasks.Task t) { await t; }
        }
        public struct Counter { public int N; public async System.Threading.Tasks.Task Bump() { N++; } }
        public static class Pooled
        {
            [System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
            public static async System.Threading.Tasks.ValueTask Run() { }
        }
        """;

    [TestCase("async System.Threading.Tasks.Task<int> Target(int x) { return x + 1; }")]
    [TestCase("async System.Threading.Tasks.Task Target(int x) { }")]
    [TestCase("async System.Threading.Tasks.ValueTask<int> Target(int x) => x * 2;")]
    [TestCase("async System.Threading.Tasks.ValueTask Target(int x) { if (x > 0) { return; } }")]
    [TestCase("async System.Threading.Tasks.Task<int> Target(int x) { return await Inner.Next(x); }")]
    [TestCase("async System.Threading.Tasks.Task<int> Target(int x) { return await Inner.Next(x).ConfigureAwait(false); }")]
    [TestCase("async System.Threading.Tasks.Task Target(System.Threading.Tasks.Task t) { await t; }")]
    [TestCase("async System.Threading.Tasks.Task Target(System.Threading.Tasks.Task t) { await Inner.Wait(t); }")]
    [TestCase("async System.Threading.Tasks.Task<int> Target(System.Threading.Tasks.Task<int> t) { return await t.ConfigureAwait(false) + 1; }")]
    [TestCase("async System.Threading.Tasks.Task Target(int x) { if (x == 0) { throw new System.InvalidOperationException(\"x\"); } }")]
    public void AdmittedAsyncRootsLowerExactly(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var subject = TypedProgramSubject.Create(member, Helpers, admitsAsync: true);
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.True, string.Join(", ", lowering.Abstentions.Select(static abstention => abstention.Reason)));
        // The task may be allocated, and an escaping exception is stored in it.
        Assert.That(OpaqueMembers(subject, lowering), Does.Contain("async-task"));
        Assert.That(Reachable(lowering.Program).Any(block => lowering.Program.GetBlock(block).Terminator is IrExceptionalExitInstruction), Is.False);
    }

    // A call whose task is not awaited runs its callee detached: the callee's
    // exceptions fault that task, and the value is an unknown task.
    [TestCase("System.Threading.Tasks.Task<int> Target(int x) { return Inner.Next(x); }")]
    [TestCase("int Target(int x) { return Inner.Next(x).Result; }")]
    [TestCase("async System.Threading.Tasks.Task Target(int x) { await System.Threading.Tasks.Task.CompletedTask; await System.Threading.Tasks.Task.FromResult(x); }")]
    public void DetachedCallsAndCompletedTasksLowerExactly(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var subject = TypedProgramSubject.Create(member, Helpers, admitsAsync: true);
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.True, string.Join(", ", lowering.Abstentions.Select(static abstention => abstention.Reason)));
        Assert.That(OpaqueMembers(subject, lowering), Has.None.EqualTo("await"));
    }

    [Test]
    public void AnEscapingExceptionReachesTheFaultPublication()
    {
        using var subject = TypedProgramSubject.Create(
            "async System.Threading.Tasks.Task Target(int x) { if (x == 0) { throw new System.InvalidOperationException(\"x\"); } }",
            Helpers, admitsAsync: true);
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(OpaqueMembers(subject, lowering), Does.Contain("async-fault"));
    }

    [Test]
    public void AnAwaitedCalleeThatMaySuspendResumesTheCallerLater()
    {
        using var subject = TypedProgramSubject.Create(
            "async System.Threading.Tasks.Task Target(System.Threading.Tasks.Task t) { await Inner.Wait(t); }", Helpers, admitsAsync: true);
        var lowering = subject.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(OpaqueMembers(subject, lowering), Does.Contain("async-resume").And.Contain("await"));
    }

    // Without the root's admission, without opaque calls, or for a shape
    // whose builder or receiver differs, the body is not lowered.
    [TestCase("async System.Threading.Tasks.Task<int> Target(int x) { return x + 1; }", false, true)]
    [TestCase("async System.Threading.Tasks.Task Target(System.Threading.Tasks.Task t) { await t; }", true, false)]
    [TestCase("async void Target(int x) { }", true, true)]
    [TestCase("async System.Collections.Generic.IAsyncEnumerable<int> Target(int x) { yield return x; }", true, true)]
    [TestCase("async System.Threading.Tasks.Task Target(System.Collections.Generic.IAsyncEnumerable<int> s) { await foreach (var x in s) { } }", true, true)]
    [TestCase("async System.Threading.Tasks.Task Target(System.IAsyncDisposable d) { await using (d) { } }", true, true)]
    [TestCase("async System.Threading.Tasks.Task Target(System.IAsyncDisposable d) { await using var e = d; }", true, true)]
    public void UnadmittedAsyncShapesDoNotLowerExactly(string member, bool admitsAsync, bool opaqueCalls)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var subject = TypedProgramSubject.Create(member, Helpers, admitsAsync: admitsAsync);
        Assert.That(subject.LowerSourceCalls(false, opaqueCalls: opaqueCalls).IsExact, Is.False);
    }

    // A struct member runs on the state machine's copy of `this`, and a
    // method-level builder runs its own code.
    [TestCase("Counter", "Bump")]
    [TestCase("Pooled", "Run")]
    public void AsyncMembersWithAnotherReceiverOrBuilderAreNotAdmitted(string type, string name)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        System.ArgumentNullException.ThrowIfNull(name);
        using var subject = TypedProgramSubject.Create("int Target() { return 0; }", Helpers);
        var method = subject.Compilation.GetTypeByMetadataName(type)!.GetMembers(name).OfType<IMethodSymbol>().Single();
        Assert.That(TotalLoweringContext.IsAdmittedAsync(method), Is.False);
        Assert.That(new TotalLoweringContext(new IrFactory(), method) { AdmitsAsync = true }.HasScalarSignature, Is.False);
    }

    private static ImmutableHashSet<IrBlockId> Reachable(IrProgram program)
    {
        var seen = ImmutableHashSet.CreateBuilder<IrBlockId>();
        var pending = new Stack<IrBlockId>();
        pending.Push(program.Entry);
        while (pending.Count != 0)
        {
            var block = pending.Pop();
            if (!seen.Add(block))
            { continue; }
            foreach (var next in program.GetBlock(block).Terminator switch
            {
                IrGotoInstruction go => ImmutableArray.Create(go.Target),
                IrBranchInstruction branch => ImmutableArray.Create(branch.WhenTrue, branch.WhenFalse),
                IrThrowInstruction thrown => ImmutableArray.Create(thrown.Target),
                _ => ImmutableArray<IrBlockId>.Empty
            })
            { pending.Push(next); }
        }
        return seen.ToImmutable();
    }

    private static ImmutableArray<string> OpaqueMembers(TypedProgramSubject subject, FrontendProgramLoweringResult lowering)
    {
        return [.. lowering.Program.Blocks.SelectMany(static block => block.Instructions).OfType<IrCallInstruction>()
            .Select(call => subject.Factory.GetString(subject.Factory.GetMemberInfo(call.Member).Name))
            .Where(static name => name.StartsWith("opaque-call:", StringComparison.Ordinal))
            .Select(static name => name["opaque-call:".Length..])];
    }
}
