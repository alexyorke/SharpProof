using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class LauncherWorkflowTests
{
    [TestCase(WorkerRunStatus.TimedOut, 124)]
    [TestCase(WorkerRunStatus.Canceled, 4)]
    public void InterruptedRunExitCodesPreserveTheirStatus(WorkerRunStatus status, int expected)
    {
        Assert.That(LauncherPresentation.ExitCode(status, WorkerRunFailureReason.None), Is.EqualTo(expected));
    }

    [Test]
    public void DeclaredAssumptionMessagePreservesUserAndTrustedIdentities()
    {
        Assert.That(LauncherPresentation.AssumptionsDeclaredMessage("C.M",
            [new WorkerAssumptionEvidence { Id = "user", Kind = WorkerAssumptionKind.UserAssume },
             new WorkerAssumptionEvidence { Id = "trusted", Kind = WorkerAssumptionKind.TrustedBoundary }]),
            Is.EqualTo("User assumption/trusted evidence declared for C.M: total=2, user=1, trusted=1; user-ids=[user], trusted-ids=[trusted]."));
    }

    [TestCase(WorkerRunFailureReason.InfrastructureFailure, "worker.infrastructure")]
    [TestCase(WorkerRunFailureReason.BackendUnavailable, "backend.unavailable")]
    public void HostFallbackFailurePreservesTypedReasonAndErrorContext(WorkerRunFailureReason reason, string code)
    {
        var error = new WorkerProtocolError { Code = code, Message = "Original initialization failure." };
        var response = WorkerHost.Failure(reason, [error], new WorkerBudgets());
        var roundtrip = WorkerProtocolJson.DeserializeResponse(WorkerProtocolJson.SerializeResponse(response))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerProtocolJson.Validate(roundtrip).IsValid, Is.True);
            Assert.That(roundtrip.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(roundtrip.FailureReason, Is.EqualTo(reason));
            Assert.That(roundtrip.Errors.Single().Message, Is.EqualTo(error.Message));
            Assert.That(roundtrip.Errors.Single().Code, Is.EqualTo(code));
            Assert.That(roundtrip.Manifest.Claims, Is.Empty);
            Assert.That(roundtrip.ClaimResults, Is.Empty);
        }
    }

    [Test]
    public async Task UnsafeRuntimeEnvironmentStopsBeforeArgumentParsing()
    {
        const string variable = "DOTNET_STARTUP_HOOKS";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "unreviewed-hook");
            Assert.That(await Program.Main([]), Is.EqualTo(125));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [TestCase("missing")]
    [TestCase("unknown")]
    [TestCase("policy")]
    public async Task InvalidInvocationProducesUsageWithoutWritingEvidence(string invalid)
    {
        using var directory = new TempDirectory("sharpproof-direct-invalid-");
        var arguments = invalid == "missing" ? [] : Arguments(directory.FullName);
        arguments = invalid switch
        {
            "unknown" => [.. arguments, "--unknown", "value"],
            "policy" => [.. arguments, "--verify-policy", "invalid"],
            _ => arguments
        };
        Assert.That(await Program.RunMain(arguments), Is.EqualTo(2));
        Assert.That(File.Exists(Path.Combine(directory.FullName, "request.json")), Is.False);
        Assert.That(File.Exists(Path.Combine(directory.FullName, "result.json")), Is.False);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task PreManifestTimeoutIsPromotedOnlyWhenItsRequestAuthorityIsValid(bool invalidAuthority, bool requireProven)
    {
        using var directory = new TempDirectory("sharpproof-pre-manifest-timeout-");
        var arguments = Arguments(directory.FullName);
        if (requireProven)
        {
            arguments[Array.IndexOf(arguments, "--verify-policy") + 1] = "require-proven";
        }
        var exit = await Program.RunMain(arguments, (request, _) =>
            Task.FromResult(WorkerResultAssembler.Create(
                WorkerResultAssembler.EmptyInputHash, WorkerResultAssembler.EmptyManifest(),
                WorkerRunStatus.TimedOut, WorkerRunFailureReason.None, [], [],
                request.Budgets, WorkerCacheStatus.Disabled, 1,
                [new WorkerProtocolError { Code = WorkerProtocolErrorCodes.WorkerTimeout, Message = "Project timed out before preparation." }],
                invalidAuthority ? new string('0', 64) : WorkerProtocolJson.ComputeRequestHash(request),
                Program.ExpectedVersions())));
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(Path.Combine(directory.FullName, "result.json")))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(invalidAuthority ? 3 : requireProven ? 6 : 0), "RequireProven must reject incomplete project coverage.");
            Assert.That(response.RunStatus, Is.EqualTo(invalidAuthority ? WorkerRunStatus.Failed : WorkerRunStatus.TimedOut));
            Assert.That(response.FailureReason, Is.EqualTo(invalidAuthority ? WorkerRunFailureReason.MalformedResult : WorkerRunFailureReason.None));
            Assert.That(response.InputHash, Is.Not.EqualTo(WorkerResultAssembler.EmptyInputHash));
            Assert.That(response.Errors.Select(static item => item.Code),
                invalidAuthority ? Does.Contain("worker.malformed_result") : Does.Contain(WorkerProtocolErrorCodes.WorkerTimeout));
            if (!invalidAuthority)
            {
                Assert.That(response.Errors.Single().Message, Is.EqualTo("Project timed out before preparation."));
            }
        }
    }

    [Test]
    public async Task PublicationFailurePreservesTheValidPrivateResult()
    {
        using var directory = new TempDirectory("sharpproof-publication-failure-");
        var result = Path.Combine(directory.FullName, "published-result.json");
        Directory.CreateDirectory(result);
        var arguments = Arguments(directory.FullName);
        arguments = [.. arguments,
            "--publish-request", Path.Combine(directory.FullName, "published-request.json"),
            "--publish-result", result,
            "--publish-compiler-manifest", Path.Combine(directory.FullName, "published-manifest.json")];
        Assert.That(await Program.RunMain(arguments), Is.EqualTo(3));
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(Path.Combine(directory.FullName, "result.json")))!;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        Assert.That(Directory.Exists(result), Is.True);
    }

    [Test]
    public void AggregateFailureIsPropagatedWithoutPublishingAResult()
    {
        using var directory = new TempDirectory("sharpproof-aggregate-failure-");
        var failure = new AggregateException("Cannot classify combined failures", new FormatException());
        Assert.That(Assert.ThrowsAsync<AggregateException>((Func<Task>)(async () =>
            await Program.RunMain(Arguments(directory.FullName), (_, _) => throw failure))), Is.SameAs(failure));
        Assert.That(File.Exists(Path.Combine(directory.FullName, "result.json")), Is.False);
    }

    [Test]
    public async Task DirectWorkerPublishesOneBoundRequestResultAndSarifSet()
    {
        using var directory = new TempDirectory("sharpproof-direct-publication-");
        var arguments = Arguments(directory.FullName);
        var publishedRequest = Path.Combine(directory.FullName, "published-request.json");
        var publishedResult = Path.Combine(directory.FullName, "published-result.json");
        var publishedManifest = Path.Combine(directory.FullName, "published-manifest.json");
        var sarif = Path.Combine(directory.FullName, "published.sarif");
        arguments = [.. arguments,
            "--publish-request", publishedRequest, "--publish-result", publishedResult,
            "--publish-compiler-manifest", publishedManifest, "--publish-sarif", sarif];
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, "result.json"), "stale result");

        Assert.That(await Program.RunMain(arguments), Is.Zero);
        var request = WorkerProtocolJson.DeserializeRequest(await File.ReadAllTextAsync(publishedRequest))!;
        var response = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(publishedResult))!;
        var manifestBytes = await File.ReadAllBytesAsync(publishedManifest);
        var artifact = CompilerManifestArtifactJson.Deserialize(Encoding.UTF8.GetString(manifestBytes))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.CompilerManifest.Path, Is.EqualTo(publishedManifest));
            Assert.That(request.CompilerManifest.Sha256, Is.EqualTo(ArtifactDigest.Compute(manifestBytes)));
            Assert.That(WorkerProtocolJson.ValidateForRequest(response,
                WorkerProtocolJson.ComputeRequestHash(request),
                Program.ComputeExpectedInputHash(request, manifestBytes), artifact.Manifest,
                request, Program.ExpectedVersions()).IsValid, Is.True);
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Disabled));
        }
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(sarif));
        Assert.That(document.RootElement.GetProperty("runs")[0]
            .GetProperty("invocations")[0].GetProperty("executionSuccessful").GetBoolean(), Is.True);
    }

    [TestCase("infrastructure", WorkerRunFailureReason.InfrastructureFailure)]
    [TestCase("containment", WorkerRunFailureReason.ContainmentFailure)]
    [TestCase("platform", WorkerRunFailureReason.ContainmentFailure)]
    [TestCase("overflow", WorkerRunFailureReason.InvalidRequest)]
    [TestCase("malformed", WorkerRunFailureReason.MalformedResult)]
    public async Task LauncherFailuresReplaceStaleEvidenceWithBoundFailure(
        string failure, WorkerRunFailureReason expected)
    {
        using var directory = new TempDirectory("sharpproof-direct-failure-");
        var arguments = Arguments(directory.FullName);
        var resultPath = Path.Combine(directory.FullName, "result.json");
        await File.WriteAllTextAsync(resultPath, "stale result");
        var exit = await Program.RunMain(arguments, (_, _) => failure switch
        {
            "containment" => throw new IOException("cannot establish containment"),
            "platform" => throw new PlatformNotSupportedException("unsupported host"),
            "overflow" => throw new OverflowException("invalid combined budget"),
            "infrastructure" => throw new FormatException("unexpected verifier state"),
            _ => Task.FromResult(new WorkerVerifyResponse())
        });
        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(Path.Combine(directory.FullName, "request.json")))!;
        var response = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(resultPath))!;
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory.FullName, "manifest.json"));
        var artifact = CompilerManifestArtifactJson.Deserialize(Encoding.UTF8.GetString(bytes))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.Not.Zero);
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(response.FailureReason, Is.EqualTo(expected));
            Assert.That(response.Errors, Has.Length.EqualTo(1));
            Assert.That(WorkerProtocolJson.ValidateForRequest(response,
                WorkerProtocolJson.ComputeRequestHash(request),
                Program.ComputeExpectedInputHash(request, bytes), artifact.Manifest,
                request, Program.ExpectedVersions()).IsValid, Is.True);
        }
    }

    [Test]
    public async Task PreflightFailureWritesNoInvocationEvidence()
    {
        using var directory = new TempDirectory("sharpproof-direct-preflight-");
        var exit = await Program.RunMain(Arguments(directory.FullName),
            validatePreflight: static _ => throw new PlatformNotSupportedException("unsupported container"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(125));
            Assert.That(File.Exists(Path.Combine(directory.FullName, "request.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(directory.FullName, "result.json")), Is.False);
        }
    }

    [Test]
    public async Task WorkerHostRejectsInvalidRequestWithoutClaims()
    {
        var request = new WorkerVerifyRequest { Budgets = new WorkerBudgets { MaxParallelism = 0 } };
        var response = await WorkerHost.VerifyAsync(request, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.InvalidRequest));
            Assert.That(response.Errors.Select(static item => item.Code), Does.Contain("budgets.parallelism"));
            Assert.That(response.Manifest.Claims, Is.Empty);
            Assert.That(response.ClaimResults, Is.Empty);
        }
    }

    private static string[] Arguments(string directory)
    {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        var artifact = new CompilerManifestArtifact
        {
            Features = WorkerFeatureSet.All,
            Compilation = new CompilerCompilationSnapshot { ProjectDirectory = directory },
            Manifest = manifest
        };
        var artifactPath = Path.Combine(directory, "manifest.json");
        File.WriteAllText(artifactPath, CompilerManifestArtifactJson.Serialize(artifact));
        return [
            "verify", "--worker", typeof(SharpProofWorker).Assembly.Location,
            "--request", Path.Combine(directory, "request.json"),
            "--result", Path.Combine(directory, "result.json"),
            "--compiler-manifest", artifactPath,
            "--verify-policy", "advisory", "--assumption-policy", "allow",
            "--cache-enabled", "false"
        ];
    }
}
