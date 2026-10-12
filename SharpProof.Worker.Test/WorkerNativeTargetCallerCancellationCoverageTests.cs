using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task CompleteNativeTargetApiHonorsActualCallerCancellationBeforeQuerying()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                [ZeroAllocations]
                public static int Target(int value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures(Contract.Result<int>() > 0);
                    return value;
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var serialized = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(serialized, out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        Assert.That(preparation.Entry.ClaimIds, Has.Length.EqualTo(2));
        Assert.That(preparation.Total!.IsBodyAbstraction, Is.False);
        Assert.That(preparation.Total.EffectsCompleteAtEntry, Is.True);
        Assert.That(preparation.Total.ValidEffectClaimIds, Does.Contain(preparation.EffectClaims.Single().ClaimId));
        var budgets = new WorkerBudgets();
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var backend = new NativeCallableBackend(new IrSmtBackendOptions(budgets.QueryRlimit));
        using var projectBoundary = new CancellationTokenSource();
        using var callerCancellation = new CancellationTokenSource();
        await callerCancellation.CancelAsync();
        var canceled = await CallableVerificationPolicy.VerifyNativeTargetAsync(backend, preparation, budgets,
            () => backend.ConsumedResourceCount, budgets.MethodWallTimeMilliseconds, projectBoundary, callerCancellation.Token);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(canceled.Callable.CallableId, Is.EqualTo(preparation.Entry.CallableId));
            Assert.That(canceled.Callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(canceled.Callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.Canceled));
            Assert.That(canceled.Claims.Select(claim => claim.ClaimId), Is.EquivalentTo(preparation.Entry.ClaimIds));
            Assert.That(canceled.Claims.All(claim => claim.Outcome == WorkerClaimOutcome.Unknown &&
                claim.Reason == WorkerClaimReason.Canceled && claim.Vacuity == WorkerVacuityKind.None &&
                claim.EffectCertainty == WorkerEffectEvidenceCertainty.Unavailable &&
                claim.ProofCore.Length == 0 && claim.EffectWitness == null), Is.True);
            Assert.That(backend.ConsumedResourceCount, Is.Zero);
            Assert.That(projectBoundary.IsCancellationRequested, Is.False);
            Assert.That(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), Is.EqualTo(serialized));
        }
        var verified = await CallableVerificationPolicy.VerifyNativeTargetAsync(backend, preparation, budgets,
            () => backend.ConsumedResourceCount, budgets.MethodWallTimeMilliseconds, projectBoundary, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(verified.Callable.CallableId, Is.EqualTo(preparation.Entry.CallableId));
            Assert.That(verified.Callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(verified.Callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.None));
            Assert.That(verified.Claims.Select(claim => claim.ClaimId), Is.EquivalentTo(preparation.Entry.ClaimIds));
            Assert.That(verified.Claims.All(claim => claim.Outcome == WorkerClaimOutcome.Proven &&
                claim.Reason == WorkerClaimReason.None && claim.Vacuity == WorkerVacuityKind.None &&
                claim.ProofCore.Length != 0 && claim.EffectWitness == null), Is.True);
            Assert.That(backend.ConsumedResourceCount, Is.GreaterThan(0));
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
            Assert.That(response.ClaimResults.All(claim => claim.Outcome == WorkerClaimOutcome.Proven &&
                claim.Reason == WorkerClaimReason.None && claim.ProofCore.Length != 0), Is.True);
            Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
        var publicCanceled = await worker.VerifyAsync(request, callerCancellation.Token);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(publicCanceled.RunStatus, Is.EqualTo(WorkerRunStatus.Canceled));
            Assert.That(publicCanceled.Manifest.Claims, Is.Empty);
            Assert.That(publicCanceled.ClaimResults, Is.Empty);
            Assert.That(publicCanceled.Errors.Select(error => error.Code), Does.Contain("worker.canceled"));
            Assert.That(WorkerProtocolJson.Validate(publicCanceled).IsValid, Is.True);
            Assert.That(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), Is.EqualTo(serialized));
        }
    }
}
