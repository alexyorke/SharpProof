using NUnit.Framework;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A ContractFor companion member may declare effect claims for a target its
// author cannot annotate; the claim is checked against the target's body
// under the companion's preconditions.
[TestFixture]
public sealed class CompanionEffectClaimTests
{
    [TestCase("", false, TestName = "WithoutPreconditionTheTargetThrows")]
    [TestCase("Contract.Requires(x != 0);", true, TestName = "UnderThePreconditionTheTargetDoesNotThrow")]
    public async Task DoesNotThrowOnACompanionChecksTheTarget(string requires, bool proven)
    {
        System.ArgumentNullException.ThrowIfNull(requires);
        var response = await Verify("""
            using SharpProof.Attributes;
            public static class Target { public static int F(int x) => 10 / x; }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                [DoesNotThrow]
                public static int F(int x) {
            """ + requires + """
             return 0; }
            }
            """);
        var effects = response.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.Effect).ToArray();
        Assert.That(effects, Has.Length.EqualTo(1));
        var result = response.ClaimResults.Single(result => result.ClaimId == effects[0].ClaimId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(effects[0].CallableId, Does.Contain("Target.F").And.Not.Contain("TargetContracts"));
            Assert.That(result.Outcome, Is.EqualTo(proven ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted));
        }
    }

    [Test]
    public async Task CompanionWithoutEffectAttributesDeclaresNoEffectClaim()
    {
        var response = await Verify("""
            using SharpProof.Attributes;
            public static class Target { public static int F(int x) => 10 / x; }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static int F(int x) { Contract.Requires(x != 0); return 0; }
            }
            """);
        Assert.That(response.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.Effect), Is.Empty);
    }

    private static async Task<WorkerVerifyResponse> Verify(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete), () => string.Join("; ", response.Errors.Select(static error => error.Code + ": " + error.Message)));
        return response;
    }
}
