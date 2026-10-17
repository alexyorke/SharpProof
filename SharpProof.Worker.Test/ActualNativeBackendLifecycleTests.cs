using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ActualNativeBackendLifecycleTests
{
    private const string Source = """
        using SharpProof.Attributes;
        public static class Subject
        {
            public static int Target(int value)
            {
                Contract.Requires(value >= 0);
                Contract.Requires(value <= 1000);
                Contract.Ensures(Contract.Result<int>() == value + 1);
                return value + 1;
            }
        }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task SuppliedActualNativeBackendLifecyclePreservesProofOrTypedFailure(bool disposed)
    {
        var compilation = TestCompilation.Create("NativeBackendLifecycle", ("Subject.cs", Source));
        TestCompilation.AssertNoErrors(compilation);
        using var emittedImage = new MemoryStream();
        var emitted = compilation.Emit(emittedImage);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        Assert.That(project.Request.Cache.Enabled, Is.False);
        Assert.That(project.Request.Budgets.QueryRlimit, Is.EqualTo(3_000_000));
        Assert.That(project.Request.Budgets.MethodRlimit, Is.EqualTo(20_000_000));
        Assert.That(project.Request.Budgets.MaximumExpressionDepth, Is.EqualTo(64));
        Assert.That(project.Snapshot.Callables, Has.Length.EqualTo(1));
        Assert.That(project.Snapshot.Callables.Single().Total, Is.Not.Null);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        NativeCallableBackend? backendOwner = null;
        try
        {
            backendOwner = new NativeCallableBackend(new IrSmtBackendOptions(project.Request.Budgets.QueryRlimit));
            var backend = backendOwner;
            if (disposed)
            {
                backend.Dispose();
                backendOwner = null;
            }
            using var worker = new SharpProofWorker(backend);
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
            Assert.That(response.CallableResults, Has.Length.EqualTo(1));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            Assert.That(response.Manifest.Claims.Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ClaimId)),
                Is.EqualTo(response.ClaimResults.Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ClaimId))));
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Disabled));
            Assert.That(response.Summary.CacheHit, Is.False);
            var callable = response.CallableResults.Single();
            var claim = response.ClaimResults.Single();
            if (disposed)
            {
                Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
                Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.InfrastructureFailure));
                Assert.That(callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Incomplete));
                Assert.That(callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.InfrastructureFailure));
                Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
                Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.InfrastructureFailure));
                Assert.That(claim.ProofCore, Is.Empty);
                Assert.That(claim.Model, Is.Empty);
                Assert.That(claim.EffectWitness, Is.Null);
                Assert.That(backend.ConsumedResourceCount, Is.Zero);
            }
            else
            {
                Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
                Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
                Assert.That(callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
                Assert.That(callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.None));
                Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
                Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.None));
                Assert.That(backend.ConsumedResourceCount, Is.GreaterThan(0));
            }
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
            var json = WorkerProtocolJson.SerializeResponse(response);
            Assert.That(WorkerProtocolJson.SerializeResponse(WorkerProtocolJson.DeserializeResponse(json)!), Is.EqualTo(json));
            await TestContext.Progress.WriteLineAsync("ACTUAL-NATIVE-LIFECYCLE " + JsonSerializer.Serialize(new
            {
                sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source))),
                suppliedLifecycle = disposed ? "actual-native-backend-already-disposed" : "actual-native-backend-healthy",
                runStatus = response.RunStatus.ToString(),
                failureReason = response.FailureReason.ToString(),
                callableCoverage = callable.Coverage.ToString(),
                callableReason = callable.Reason.ToString(),
                claimOutcome = claim.Outcome.ToString(),
                claimReason = claim.Reason.ToString(),
                consumedResources = backend.ConsumedResourceCount
            }));
        }
        finally
        {
            backendOwner?.Dispose();
        }
    }
}
