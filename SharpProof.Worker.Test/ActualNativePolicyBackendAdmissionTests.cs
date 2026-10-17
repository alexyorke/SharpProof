using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ActualNativePolicyBackendAdmissionTests
{
    [Test]
    public async Task MissingRequiredBackendRejectsBeforeNativeQueryWithOwnedTarget()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value >= 0);
                    Contract.Requires(value <= 1000);
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """;
        var compilation = TestCompilation.Create("NativePolicyBackendAdmission", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var target = project.Snapshot.Callables.Single();
        var budgets = project.Request.Budgets;
        Assert.That(budgets.QueryRlimit, Is.EqualTo(3_000_000));
        Assert.That(budgets.MethodRlimit, Is.EqualTo(20_000_000));
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
        Assert.That(budgets.MethodWallTimeMilliseconds, Is.EqualTo(10_000));
        Assert.That(budgets.ProjectWallTimeMilliseconds, Is.EqualTo(300_000));
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var backend = new NativeCallableBackend(new IrSmtBackendOptions(budgets.QueryRlimit));
        using var boundary = new CancellationTokenSource();
        boundary.CancelAfter(budgets.ProjectWallTimeMilliseconds);
        var healthy = await CallableVerificationPolicy.VerifyNativeTargetAsync(backend, target, budgets,
            () => backend.ConsumedResourceCount, budgets.MethodWallTimeMilliseconds, boundary, TestContext.CurrentContext.CancellationToken);
        Assert.That(healthy.Callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
        Assert.That(healthy.Callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.None));
        Assert.That(healthy.Claims.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(healthy.Claims.Single().Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(backend.ConsumedResourceCount, Is.GreaterThan(0));
        ValidateRoundTrip(healthy);
        var before = backend.ConsumedResourceCount;
        // Ordinary raw API missing-argument admission: no provider or native result supplied.
        var rejected = await CallableVerificationPolicy.VerifyNativeTargetAsync(null!, target, budgets,
            readConsumedResourceCount: null, budgets.MethodWallTimeMilliseconds, boundary, TestContext.CurrentContext.CancellationToken);
        Assert.That(rejected.Callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Incomplete));
        Assert.That(rejected.Callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.InfrastructureFailure));
        var claim = rejected.Claims.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.InfrastructureFailure));
        Assert.That(claim.ProofCore, Is.Empty);
        Assert.That(claim.Model, Is.Empty);
        Assert.That(claim.EffectWitness, Is.Null);
        Assert.That(claim.Assumptions.Any(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Used)), Is.False);
        Assert.That(backend.ConsumedResourceCount, Is.EqualTo(before));
        ValidateRoundTrip(rejected);

        void ValidateRoundTrip(CallableVerificationResult actual)
        {
            System.ArgumentNullException.ThrowIfNull(actual);
            Assert.That(WorkerResultAssembler.TryProjectRunState([actual.Callable], actual.Claims, [],
                out var status, out var failure), Is.True);
            var response = WorkerResultAssembler.Create(project.Bind().InputHash, project.Snapshot.CompilerManifest.Manifest,
                status, failure, [actual.Callable], actual.Claims, budgets, WorkerCacheStatus.Disabled, 0);
            var validation = WorkerProtocolJson.Validate(response, response.InputHash, response.Manifest);
            Assert.That(validation.IsValid, Is.True, string.Join(", ", validation.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code))));
            var json = WorkerProtocolJson.SerializeResponse(response);
            var roundTrip = WorkerProtocolJson.DeserializeResponse(json)
                ?? throw new AssertionException("Actual admission response did not deserialize.");
            Assert.That(WorkerProtocolJson.SerializeResponse(roundTrip), Is.EqualTo(json));
        }
    }
}
