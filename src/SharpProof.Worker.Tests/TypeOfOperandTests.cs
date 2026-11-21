using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// `typeof(X)` loads the runtime's Type object for X: no effect, no fault,
// no allocation, and X's type initializer does not run. Which object it is
// stays unknown, so nothing about its identity or nullness is proven.
[TestFixture]
public sealed class TypeOfOperandTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public static class Loud
        {
            public static int Count;
            static Loud() { System.Console.WriteLine(); }
        }
        public static class H
        {
            public static int Tag(object o, int x) { return x + 1; }
            public static object Of<T>() { return typeof(T); }
        }
        """;

    [TestCase("[EnforcePure] public static object M() { return typeof(string); }")]
    [TestCase("[EnforcePure] public static object M() { return typeof(Loud); }")]
    [TestCase("[EnforcePure] public static object M() { return H.Of<int>(); }")]
    [TestCase("[ZeroAllocations] public static object M() { return typeof(int[]); }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return H.Tag(typeof(C), x); }")]
    public async Task TypeOfIsAnEffectFreeValue(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // The Type object is not modeled: neither its identity nor that it is
    // non-null is proven, and an effect beside it still counts.
    [TestCase("public static object M() { Contract.Ensures(Contract.Result<object>() != null); return typeof(string); }")]
    [TestCase("public static bool M() { Contract.Ensures(Contract.Result<bool>()); return (object)typeof(string) == (object)typeof(string); }")]
    [TestCase("public static bool M() { Contract.Ensures(!Contract.Result<bool>()); return (object)typeof(string) == (object)typeof(int); }")]
    public async Task TheTypeObjectIsUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("static int s_count; [EnforcePure] public static object M() { s_count = 1; return typeof(Loud); }")]
    public async Task EffectsBesideTypeOfAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
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
