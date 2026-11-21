using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A `new` of a source class whose constructor the lowerer cannot expand runs
// that constructor as an unknown call: one of a class whose instance
// initializers cannot be separated from its body, such as a generic class,
// and a primary constructor. The body around the construction still lowers,
// so a claim that does not depend on the object is proven. The unknown call
// may do anything, so nothing about the object or the call's effects is. A
// class whose type initializer has an effect is still not constructed, nor
// is one through a constructor with an `in` parameter of a struct type.
[TestFixture]
public sealed class SourceConstructionCoverageTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public sealed class Box<T>
        {
            public int N = 1;
            public Box(int n) { N = n; }
        }
        public sealed class Notifier
        {
            public event System.Action Changed;
            public int N = 1;
            public Notifier(int n) { N = n; }
        }
        public sealed class Primary(int value)
        {
            public int Value => value;
        }
        public readonly struct Mark
        {
            public readonly int Line;
            public Mark(int line) { Line = line; }
        }
        public class Event : System.EventArgs
        {
            public Event(in Mark start) { }
        }
        public class Loud
        {
            public static int Count;
            static Loud() { System.Console.WriteLine(); }
            public Loud(int v) { }
        }
        """;

    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); var b = new Box<int>(x); return x + 1; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); var p = new Primary(x); return x + 1; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); var n = new Notifier(x); return x + 1; }")]
    public async Task ConstructionRunsAsAnUnknownCall(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // The object's state, the constructor's effects and its allocation are
    // unknown, and an effectful type initializer is not admitted.
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); return new Box<int>(x).N; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); return new Primary(x).Value; }")]
    [TestCase("[EnforcePure] public static object M(int x) { return new Box<int>(x); }")]
    [TestCase("[EnforcePure] public static object M(int x) { return new Primary(x); }")]
    [TestCase("[ZeroAllocations] public static object M(int x) { return new Primary(x); }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); var l = new Loud(x); return x + 1; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); var m = new Mark(x); var e = new Event(m); return x + 1; }")]
    public async Task TheConstructionStaysConservative(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var project = new ShadowTestProject(Types + "\npublic static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
