using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task InvalidCachePathPreservesNativeRefutation()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        request.Cache.Directory += "\0";
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        AssertClaimVerdict(response, WorkerClaimOutcome.Refuted);
        Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public async Task InvalidCompilerPathReturnsTypedInputFailure()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.CompilerManifest.Path += "\0";
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.InputUnavailable));
            Assert.That(response.Errors.Single().Code, Is.EqualTo("input.unavailable"));
            Assert.That(response.Manifest.Claims, Is.Empty);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task ReceiverPreconditionSupportsNativeEffectProof()
    {
        using var project = TestProject.Create("""
            using SharpProof.Attributes;
            public sealed class Subject {
                [DoesNotThrow]
                public int Identity(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        AssertClaimVerdict(response, WorkerClaimOutcome.Proven);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [TestCase("missing", WorkerClaimReason.InfrastructureFailure)]
    [TestCase("failure", WorkerClaimReason.InfrastructureFailure)]
    [TestCase("cancellation", WorkerClaimReason.InfrastructureFailure)]
    public async Task BackendQueryExceptionsProduceTypedFailure(string kind, WorkerClaimReason expected)
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        Exception failure = kind switch
        {
            "missing" => new DllNotFoundException("The backend dependency is unavailable."),
            "cancellation" => new OperationCanceledException("The backend canceled without a caller request."),
            _ => new InvalidOperationException("The backend failed during its query.")
        };
        using var worker = new SharpProofWorker(new QueryExceptionBackend(failure));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(response.ClaimResults.Single().Reason, Is.EqualTo(expected));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public void AggregateBackendFailuresPreserveTheThrownException()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        var failure = new AggregateException(new InvalidOperationException("Backend aggregate failure."));
        using var worker = new SharpProofWorker(new QueryExceptionBackend(failure));

        var thrown = Assert.ThrowsAsync<AggregateException>(async () => await worker.VerifyAsync(request));

        Assert.That(thrown, Is.SameAs(failure));
    }

    private sealed class QueryExceptionBackend(Exception failure) : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<BackendCheckResult>(failure);
        }
    }
}
