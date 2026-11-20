using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Default API table rows for concrete List and Dictionary members and static
// string members, as a metadata call through a parameter or argument sees
// them. U has a user Equals and GetHashCode, so any member that consults the
// element or key comparer could run user code.
[TestFixture]
public sealed class CollectionAndStringCatalogTests
{
    private const string Types = "using SharpProof.Attributes;\nusing System.Collections.Generic;\n" +
        "public sealed class U { public override bool Equals(object o) { return true; } public override int GetHashCode() { return 0; } }\n";

    [TestCase("[DoesNotThrow] public static int M(List<int> l, int i) { return l != null && i >= 0 && i < l.Count ? l[i] : 0; }")]
    [TestCase("[DoesNotThrow] public static string M(List<string> l, int i) { return l != null && i >= 0 && i < l.Count ? l[i] : null; }")]
    [TestCase("[DoesNotThrow] public static U M(List<U> l, int i) { return l != null && i >= 0 && i < l.Count ? l[i] : null; }")]
    [TestCase("[EnforcePure] public static int M(List<int> l) { return l == null ? 0 : l.Count; }")]
    [TestCase("[EnforcePure] public static int M(List<U> l) { return l == null ? 0 : l.Count; }")]
    [TestCase("[EnforcePure] public static int M(Dictionary<U, int> d) { return d == null ? 0 : d.Count; }")]
    [TestCase("[DoesNotThrow] public static void M(List<int> l) { if (l != null) { l.Clear(); } }")]
    [TestCase("[DoesNotThrow] public static void M(List<U> l) { if (l != null) { l.Clear(); } }")]
    [TestCase("[ZeroAllocations] public static void M(List<U> l) { if (l != null) { l.Clear(); } }")]
    [TestCase("[DoesNotThrow] public static void M(Dictionary<int, int> d) { if (d != null) { d.Clear(); } }")]
    [TestCase("[DoesNotThrow] public static void M(Dictionary<U, U> d) { if (d != null) { d.Clear(); } }")]
    [TestCase("[DoesNotThrow] public static int M(List<int> l) { if (l == null) { return 0; } l.Clear(); return l.Count; }")]
    [TestCase("[DoesNotThrow] public static string M(string a, string b, string c) { return string.Concat(a, b, c); }")]
    [TestCase("[DoesNotThrow] public static string M(string a, string b, string c, string d) { return string.Concat(a, b, c, d); }")]
    [TestCase("[EnforcePure] public static string M(string a, string b, string c) { return string.Concat(a, b, c); }")]
    [TestCase("[DoesNotThrow] public static int M(string a, string b) { return string.CompareOrdinal(a, b); }")]
    [TestCase("[EnforcePure] public static bool M(string a, string b) { return string.CompareOrdinal(a, b) < 0; }")]
    [TestCase("[ZeroAllocations] public static int M(string a, string b) { return string.CompareOrdinal(a, b); }")]
    public async Task SpecifiedMembersAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // A null receiver still faults before Clear runs.
    [TestCase("[DoesNotThrow] public static void M(List<int> l) { l.Clear(); }")]
    [TestCase("[DoesNotThrow] public static void M(Dictionary<int, int> d) { d.Clear(); }")]
    public async Task UnguardedReceiversAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // Clear writes the receiver, Concat allocates, and no row relates a
    // result to its inputs. Members that consult a comparer, or call
    // ToString on elements, have no row, so user code keeps them Unknown.
    [TestCase("[EnforcePure] public static void M(List<int> l) { if (l != null) { l.Clear(); } }")]
    [TestCase("[ZeroAllocations] public static string M(string a, string b, string c) { return string.Concat(a, b, c); }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 0); return string.CompareOrdinal(\"a\", \"b\"); }")]
    [TestCase("[DoesNotThrow] public static bool M(List<U> l, U x) { return l != null && l.Contains(x); }")]
    [TestCase("[DoesNotThrow] public static bool M(List<int> l, int x) { return l != null && l.Contains(x); }")]
    [TestCase("[DoesNotThrow] public static bool M(Dictionary<U, int> d, U k) { return d != null && k != null && d.ContainsKey(k); }")]
    [TestCase("[DoesNotThrow] public static bool M(Dictionary<string, int> d, string k) { return d != null && k != null && d.TryGetValue(k, out var v) && v > 0; }")]
    [TestCase("[DoesNotThrow] public static string M(U[] a) { return a == null ? \"\" : string.Join(\",\", (object[])a); }")]
    public async Task UnspecifiedBehaviorStaysUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    // Clear's opaque write must invalidate what the body knew about the
    // collection's contents, whether it built them itself or assumed them.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 1); var l = new List<int>(); l.Add(1); l.Clear(); return l.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 7); var l = new List<int>() { 7 }; l.Clear(); return l.Count > 0 ? l[0] : 7; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 1); var d = new Dictionary<int, int>(); d.Add(1, 1); d.Clear(); return d.Count; }")]
    [TestCase("public static bool M() { Contract.Ensures(Contract.Result<bool>()); var d = new Dictionary<int, int>() { { 1, 1 } }; d.Clear(); return d.ContainsKey(1); }")]
    [TestCase("public static int M(List<int> l) { Contract.Requires(l != null && l.Count == 1); Contract.Ensures(Contract.Result<int>() == 1); l.Clear(); return l.Count; }")]
    public async Task ClearInvalidatesKnownContents(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var project = new ShadowTestProject(Types + "public static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
