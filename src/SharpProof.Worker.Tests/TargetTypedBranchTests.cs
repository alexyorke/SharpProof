using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A target-typed `switch` or `?:` over references with no common type keeps
// the chosen reference: each arm converts to the target type, and the merged
// value is converted to that same type again.
[TestFixture]
public sealed class TargetTypedBranchTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public abstract class N { }
        public sealed class T : N { }
        public sealed class B : N { }
        """;

    [TestCase("[EnforcePure] public static N M(int i, T t, B b) { return i switch { 0 => t, 1 => b, _ => null }; }")]
    [TestCase("[DoesNotThrow] public static N M(int i, T t, B b) { return i switch { 0 => t, _ => b }; }")]
    [TestCase("[EnforcePure] public static N M(bool c, T t, B b) { return c ? t : b; }")]
    [TestCase("public static N M(int i, T t, B b) { Contract.Ensures(i != 0 || (object)Contract.Result<N>() == t); return i switch { 0 => t, 1 => b, _ => null }; }")]
    public async Task TargetTypedBranchesAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("public static N M(int i, T t, B b) { Contract.Ensures((object)Contract.Result<N>() == t); return i switch { 0 => t, 1 => b, _ => null }; }")]
    public async Task WrongBranchClaimsAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
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
