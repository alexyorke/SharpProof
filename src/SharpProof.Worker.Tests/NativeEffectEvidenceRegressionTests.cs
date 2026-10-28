using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeEffectEvidenceRegressionTests
{
    [TestCase("ZeroAllocations")]
    [TestCase("AllowedExceptions(typeof(System.Exception))")]
    public void CancellationOfALaterClaimKeepsTheCompletedNativePublication(string laterAttribute)
    {
        ArgumentNullException.ThrowIfNull(laterAttribute);
        var target = Prepare("[DoesNotThrow, " + laterAttribute + "] public static int Target(int value) { return value; }");
        Assert.That(target.EffectClaims, Has.Length.EqualTo(2));
        using var cancellation = new CancellationTokenSource();
        var published = new List<WorkerClaimResult>();
        Assert.ThrowsAsync<OperationCanceledException>((Func<Task>)(async () =>
            await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), cancellation.Token,
                result =>
                {
                    ArgumentNullException.ThrowIfNull(result);
                    published.Add(result);
                    cancellation.Cancel();
                })));
        Assert.That(published, Has.Count.EqualTo(1));
        Assert.That(published[0].Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task LaterNativeSessionUsesTheExistingMethodResourceBudget()
    {
        var target = Prepare("[DoesNotThrow, ZeroAllocations] public static int Target(int value) { return value; }");
        var budgets = new WorkerBudgets();
        long previousBackendConsumption = 0;
        var budget = new MethodResourceBudget(() => previousBackendConsumption, budgets.QueryRlimit, budgets.MethodRlimit);
        var results = await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, budgets,
            TestContext.CurrentContext.CancellationToken, result =>
            {
                ArgumentNullException.ThrowIfNull(result);
                if (result.Outcome == WorkerClaimOutcome.Proven)
                { previousBackendConsumption = budgets.MethodRlimit; }
            }, budget);
        var exception = target.EffectClaims.Single(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.ContractKind == WorkerEffectContractKind.DoesNotThrow);
        var allocation = target.EffectClaims.Single(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.ContractKind == WorkerEffectContractKind.ZeroAllocations);
        Assert.That(results.Single(result => result is null ? throw new ArgumentNullException(nameof(result)) : result.ClaimId == exception.ClaimId).Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(results.Single(result => result is null ? throw new ArgumentNullException(nameof(result)) : result.ClaimId == allocation.ClaimId).Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [TestCase("DoesNotThrow", "Assume")]
    [TestCase("EffectContract(SharpProofEffect.None)", "Assume")]
    [TestCase("EffectContract(SharpProofEffect.None)", "Requires")]
    public async Task NativeProofPublishesDependenciesFromEveryEffectComponent(string attribute, string clause)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        ArgumentNullException.ThrowIfNull(clause);
        var target = Prepare("[" + attribute + "] public static int Target(int value) { Contract." + clause +
            "(value != 0); return 1 / value; }");
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
        var declared = target.Total!.Clauses.Single(contract => contract is null ? throw new ArgumentNullException(nameof(contract)) : contract.Kind ==
            (clause == "Assume" ? CompilerContractKind.Assume : CompilerContractKind.Requires));
        Assert.That(result.Assumptions.Single(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id == declared.AssumptionId).Used, Is.True);
    }

    private static CompilerCallablePreparation Prepare(string method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class Subject { " + method + " }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
        return preparations.Single(preparation => preparation is null ? throw new ArgumentNullException(nameof(preparation)) : preparation.EffectClaims.Length != 0);
    }
}
