using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A source callee with `in` parameters is inlined when each `in` argument is
// a place nothing can write while the call runs: a value C# copies into a
// temporary, a local or by-value parameter of the caller passed nowhere else
// by reference, or a readonly field of a class outside constructors. C#
// passes the place itself, so any other place could change under the callee
// through an alias it writes.
[TestFixture]
public sealed class InSourceCalleeTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public sealed class Box
        {
            public readonly int Fixed;
            public int Loose;
            public Box(int value) { Fixed = value; Loose = value; }
        }
        public static class T
        {
            public static int F;
            public static int Read(in int x) { return x + 1; }
            public static int First(in int x, int y) { return x; }
            public static int ReadAfterWrite(in int x) { F = 5; return x; }
            public static int Bump(ref int x, in int y) { x = x + 1; return y; }
            public static int Length(in string s) { return s.Length; }
        }
        """;

    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x + 1); var y = x; return T.Read(in y); }")]
    // Without the modifier C# still passes a variable by reference.
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x + 1); var y = x; return T.Read(y); }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x + 1); return T.Read(in x); }")]
    // The caller's own `in` parameter is shared, not copied.
    [TestCase("public static int M(in int x) { Contract.Ensures(Contract.Result<int>() == x + 1); return T.Read(x); }")]
    // A value that is not a variable is copied into a temporary.
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == 2 * x + 1); return T.Read(2 * x); }")]
    [TestCase("public static int M(Box b) { Contract.Requires(b != null); Contract.Ensures(Contract.Result<int>() == b.Fixed); return T.First(in b.Fixed, 0); }")]
    [TestCase("public static int M(string s) { Contract.Requires(s != null); Contract.Ensures(Contract.Result<int>() == s.Length); return T.Length(in s); }")]
    // The callee reads the variable when it is entered, after later
    // arguments have run.
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); var y = 1; return T.First(in y, y = x); }")]
    [TestCase("[EnforcePure] public static int M(int x) { var y = x; return T.Read(in y); }")]
    public async Task ReadOnlyArgumentsAreModeled(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); var y = x; return T.Read(in y); }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == 1); var y = 1; return T.First(in y, y = x); }")]
    // C# returns the value the callee stored through the alias; a copy
    // taken at the call would return the old one.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 0); T.F = 0; return T.ReadAfterWrite(in T.F); }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); var y = x; return T.Bump(ref y, in y); }")]
    public async Task WrongClaimsAreNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // A place another path can write stays unmodeled: a mutable static or
    // instance field, an array element, the same variable passed by `ref`
    // too.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 5); T.F = 0; return T.ReadAfterWrite(in T.F); }")]
    [TestCase("public static int M(Box b) { Contract.Requires(b != null); Contract.Ensures(Contract.Result<int>() == b.Loose); return T.First(in b.Loose, 0); }")]
    [TestCase("public static int M(int[] a) { Contract.Requires(a != null && a.Length > 0); Contract.Ensures(Contract.Result<int>() == a[0]); return T.First(in a[0], 0); }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x + 1); var y = x; return T.Bump(ref y, in y); }")]
    public async Task UnadmittedReadOnlyArgumentsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
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
