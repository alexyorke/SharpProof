using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerOwnedBodyAbstractionEffectAdmissionTests
{
    // Exact established producers from CompilerBodyAbstractionTests.
    private const string ScalarSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x >= 0);
                Contract.Ensures(Contract.Result<int>() == Contract.Result<int>());
                Contract.Ensures(Contract.Result<int>() == x);
                var unsupported = 1.5 * x;
                throw null!;
            }
        }
        """;
    private const string VoidSource = """
        using SharpProof.Attributes;
        public static class Subject { public static void Target() {
            Contract.Ensures(true);
            Contract.Ensures(false);
            var unsupported = 0; System.Threading.Interlocked.Increment(ref unsupported);
            throw null!;
        } }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualCompilerAbstractionCannotSupplyAnEffectSiteProof(bool emptyVoid)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(emptyVoid ? VoidSource : ScalarSource);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.IsBodyAbstraction, Is.True);
        Assert.That(preparation.Total.CallPreconditions, Is.Empty);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(candidate!.IsBodyAbstraction, Is.True);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True, failure.ToString());
        Assert.That(plan!.HasBodyAbstraction, Is.True);
        var budgets = new WorkerBudgets();
        using var solver = new PassiveCallableSolver(plan, budgets.QueryRlimit, budgets.MethodRlimit);
        var result = await solver.VerifyEffectSitesAsync(static (_, _) => true, readsViolate: true,
            locksViolate: true, static _ => true, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(result.EntryModel, Is.Empty);
            Assert.That(result.Core, Is.Empty);
            Assert.That(result.BodyAssumptions, Is.Empty);
            Assert.That(result.QueryCompleted, Is.False);
        }
    }
}
