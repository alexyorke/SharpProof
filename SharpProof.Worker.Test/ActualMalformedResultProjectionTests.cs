using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ActualMalformedResultProjectionTests
{
    [Test]
    public async Task MalformedProjectionUsesActualTargetBindingsAndActualForeignResponseValidation()
    {
        using var first = CreateProject("FirstSubject", "ActualMalformedProjectionFirst");
        using var other = CreateProject("OtherSubject", "ActualMalformedProjectionOther");
        using var firstWorker = SharpProofWorker.Create(first.Request.Budgets);
        using var otherWorker = SharpProofWorker.Create(other.Request.Budgets);
        var actual = await firstWorker.VerifyAsync(first.Request, first.Snapshot, CancellationToken.None);
        var otherActual = await otherWorker.VerifyAsync(other.Request, other.Snapshot, CancellationToken.None);
        foreach (var response in new[] { actual, otherActual })
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(response.ClaimResults.Single().Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(response.ClaimResults.Single().Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            Assert.That(response.ClaimResults.Single().Model, Is.Empty);
            Assert.That(response.ClaimResults.Single().EffectWitness, Is.Null);
            Assert.That(WorkerProtocolJson.Validate(response, response.InputHash, response.Manifest).IsValid, Is.True);
        }
        Assert.That(actual.InputHash, Is.Not.EqualTo(otherActual.InputHash));
        var actualJson = WorkerProtocolJson.SerializeCanonicalResponse(actual);
        var otherHash = other.Bind().InputHash;
        // Actual output of a different owned project is only a raw negative validation input.
        // Neither response is mutated or enrolled as evidence for the other target.
        var rejected = WorkerProtocolJson.Validate(actual, otherHash, other.Snapshot.CompilerManifest.Manifest);
        Assert.That(rejected.IsValid, Is.False);
        Assert.That(rejected.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)), Does.Contain("response.input_mismatch"));
        Assert.That(WorkerProtocolJson.SerializeCanonicalResponse(actual), Is.EqualTo(actualJson));
        var projected = SharpProofWorker.ProjectMalformedResults(other.Snapshot.Callables);
        Assert.That(projected.Callables, Has.Length.EqualTo(other.Snapshot.Callables.Length));
        foreach (var callable in projected.Callables)
        {
            Assert.That(callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.MissingClaimResult));
            Assert.That(other.Snapshot.Callables.Any(target => target is null ? throw new System.ArgumentNullException(nameof(target)) : (target.Entry.CallableId == callable.CallableId)), Is.True);
        }
        Assert.That(projected.Claims, Has.Length.EqualTo(other.Snapshot.CompilerManifest.Manifest.Claims.Length));
        foreach (var claim in projected.Claims)
        {
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.InfrastructureFailure));
            Assert.That(claim.ProofCore, Is.Empty);
            Assert.That(claim.Model, Is.Empty);
            Assert.That(claim.EffectWitness, Is.Null);
            Assert.That(claim.Assumptions.All(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (!assumption.Used)), Is.True);
        }
        Assert.That(WorkerResultAssembler.TryProjectRunState(projected.Callables, projected.Claims,
            rejected.Errors, out var status, out var failure), Is.True);
        Assert.That(status, Is.EqualTo(WorkerRunStatus.Failed));
        Assert.That(failure, Is.EqualTo(WorkerRunFailureReason.MalformedResult));
        var fallback = WorkerResultAssembler.Create(otherHash, other.Snapshot.CompilerManifest.Manifest,
            status, failure, projected.Callables, projected.Claims, other.Request.Budgets, WorkerCacheStatus.Rejected, 0,
            rejected.Errors, WorkerProtocolJson.ComputeRequestHash(other.Request), otherActual.Summary.Versions);
        var validation = WorkerProtocolJson.Validate(fallback, otherHash, other.Snapshot.CompilerManifest.Manifest);
        Assert.That(validation.IsValid, Is.True, string.Join(", ", validation.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code))));
        var json = WorkerProtocolJson.SerializeCanonicalResponse(fallback);
        var roundTrip = WorkerProtocolJson.DeserializeResponse(json)
            ?? throw new AssertionException("The canonical malformed fallback did not deserialize.");
        Assert.That(WorkerProtocolJson.Validate(roundTrip, otherHash, other.Snapshot.CompilerManifest.Manifest).IsValid, Is.True);
        Assert.That(WorkerProtocolJson.SerializeCanonicalResponse(roundTrip), Is.EqualTo(json));
        // This exercises the maintained projection API; it does not claim the outer worker guard executed.
    }

    private static ShadowTestProject CreateProject(string typeName, string assemblyName)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyName);
        System.ArgumentNullException.ThrowIfNull(typeName);
        var source = "using SharpProof.Attributes; public static class " + typeName +
            " { public static int Target(int value) { Contract.Ensures(Contract.Result<int>() == value); return value; } }";
        var compilation = TestCompilation.Create(assemblyName, ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var project = new ShadowTestProject(artifact, cacheEnabled: false);
        Assert.That(project.Request.Budgets.QueryRlimit, Is.EqualTo(3_000_000));
        Assert.That(project.Request.Budgets.MethodRlimit, Is.EqualTo(20_000_000));
        Assert.That(project.Request.Budgets.MaximumExpressionDepth, Is.EqualTo(64));
        Assert.That(project.Request.Budgets.MethodWallTimeMilliseconds, Is.EqualTo(10_000));
        Assert.That(project.Request.Budgets.ProjectWallTimeMilliseconds, Is.EqualTo(300_000));
        return project;
    }
}
