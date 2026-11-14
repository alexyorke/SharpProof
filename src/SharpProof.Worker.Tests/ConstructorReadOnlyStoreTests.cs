using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A constructor's stores to its own readonly fields, and to its get-only
// auto-properties, are modeled like any other store into the object under
// construction, so a later read sees the stored value.
[TestFixture]
public sealed class ConstructorReadOnlyStoreTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public sealed class Box
        {
            private readonly long ticks;
            public Box(long ticks) { this.ticks = ticks; }
            public Box(long first, long second) { ticks = first; ticks = second; }
            public long Ticks => ticks;
        }
        public sealed class Named
        {
            public Named(int id) { Id = id; }
            public int Id { get; }
        }
        """;

    [TestCase("[EnforcePure] public static long M(long x) { return new Box(x).Ticks; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); return new Box(x).Ticks; }")]
    [TestCase("public static long M(long x, long y) { Contract.Ensures(Contract.Result<long>() == y); return new Box(x, y).Ticks; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); return new Named(x).Id; }")]
    [TestCase("[EnforcePure] public static int M(int x) { return new Named(x).Id; }")]
    public async Task StoredValueIsReadBack(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x + 1); return new Box(x).Ticks; }")]
    [TestCase("public static long M(long x, long y) { Contract.Ensures(Contract.Result<long>() == x); return new Box(x, y).Ticks; }")]
    public async Task AWrongPostconditionIsNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That(await OutcomeAsync(member), Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    private static async Task<WorkerClaimOutcome> OutcomeAsync(string member)
    { return (await ResultAsync(member)).Outcome; }

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
