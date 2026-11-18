using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Calling an iterator runs none of its body: the call creates the compiler's
// enumerator object, which captures the arguments, and returns it. The body,
// argument checks included, runs only as the result is enumerated. A claim on
// an iterator, or on a body that calls one, describes that call alone.
[TestFixture]
public sealed class IteratorCallTests
{
    private const string Types = "using System; using System.Collections.Generic; using SharpProof.Attributes;\n";

    [TestCase("[EnforcePure] public static IEnumerable<int> M(int n) { for (var i = 0; i < n; i++) { yield return i; } }")]
    [TestCase("[DoesNotThrow] public static IEnumerable<int> M(int[] a) { if (a == null) { throw new ArgumentNullException(nameof(a)); } foreach (var x in a) { yield return x; } }")]
    [TestCase("[DoesNotThrow] public static IEnumerator<int> M() { yield return 1; yield return 2 / Zero(); } private static int Zero() { return 0; }")]
    [TestCase("[EnforcePure] public static IEnumerable<int> M() { s_count++; yield return s_count; } private static int s_count;")]
    [TestCase("[EnforcePure] public static System.Collections.IEnumerable M(string s) { yield return s.Length; }")]
    [TestCase("[EnforcePure] public static bool M() { return Items(3) != null; } private static IEnumerable<int> Items(int n) { yield return n; }")]
    [TestCase("[DoesNotThrow] public static object M(string s) { return Lines(s); } private static IEnumerable<string> Lines(string s) { if (s == null) { throw new ArgumentNullException(nameof(s)); } yield return s; }")]
    [TestCase("[EnforcePure] public static bool M() { return !ReferenceEquals(Items(1), Items(1)); } private static IEnumerable<int> Items(int n) { s_count++; yield return n; } private static int s_count;")]
    [TestCase("public static bool M() { Contract.Ensures(Contract.Result<bool>()); return Items(1) != null; } private static IEnumerable<int> Items(int n) { yield return n; }")]
    public async Task IteratorCallsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // The enumerator is a new object on every call.
    [TestCase("[ZeroAllocations] public static IEnumerable<int> M() { yield break; }")]
    [TestCase("[ZeroAllocations] public static bool M() { return Items() != null; } private static IEnumerable<int> Items() { yield break; }")]
    [TestCase("public static bool M() { Contract.Ensures(Contract.Result<bool>()); return Items() == null; } private static IEnumerable<int> Items() { yield break; }")]
    public async Task IteratorAllocationsAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // Contract clauses on an iterator would describe its enumeration, which
    // is not modeled, and enumerating the result is a call on an object
    // whose type is not known.
    [TestCase("[DoesNotThrow] public static IEnumerable<int> M(int n) { Contract.Requires(n > 0); yield return n; }")]
    [TestCase("[DoesNotThrow] public static bool M() { return Items(1) != null; } private static IEnumerable<int> Items(int n) { Contract.Requires(n > 0); yield return n; }")]
    [TestCase("[DoesNotThrow] public static int M() { foreach (var x in Items()) { return x; } return 0; } private static IEnumerable<int> Items() { yield return 1; }")]
    [TestCase("[EnforcePure] public static bool M() { return Items().GetEnumerator().MoveNext(); } private static IEnumerable<int> Items() { yield return 1; }")]
    public async Task IteratorEnumerationStaysUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    [Test]
    public async Task InstanceIteratorDoesNotRunItsBody()
    {
        var result = await ResultAsync("private int _count; [EnforcePure, DoesNotThrow] public IEnumerable<int> M() { _count++; yield return _count; }",
            isStatic: false, claimCount: 2);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member, bool isStatic = true, int claimCount = 1)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var type = isStatic ? "public static class C { " : "public sealed class C { ";
        using var project = new ShadowTestProject(Types + type + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claims = response.Manifest.Claims.Where(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect).ToArray();
        Assert.That(claims, Has.Length.EqualTo(claimCount));
        var results = claims.Select(claim => response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId)).ToArray();
        Assert.That(results.Select(static result => result.Outcome).Distinct().Count(), Is.EqualTo(1),
            string.Join("; ", results.Select(static result => result.Outcome + ": " + result.Reason)));
        return results[0];
    }
}
