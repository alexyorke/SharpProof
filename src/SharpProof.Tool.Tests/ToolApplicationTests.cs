using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Tool;
using SharpProof.Worker.Protocol;

namespace SharpProof.Tool.Test;

[TestFixture]
public sealed class ToolApplicationTests
{
    [Test]
    [NonParallelizable]
    public async Task ConsoleScopeDisposalDoesNotWaitForRegisteredCancellationCallbacks()
    {
        using var scope = new ConsoleCancellationScope();
        var handler = (ConsoleCancelEventHandler)typeof(ConsoleCancellationScope)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scope)!;
        var eventArgs = (ConsoleCancelEventArgs)Activator.CreateInstance(typeof(ConsoleCancelEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
            args: [ConsoleSpecialKey.ControlC], culture: CultureInfo.InvariantCulture)!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var registration = scope.Token.Register(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("The cancellation test callback was not released.");
            }
        });
        var cancel = Task.Run(() => handler(null, eventArgs));
        Task? disposal = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CurrentContext.CancellationToken);
            disposal = Task.Run(scope.Dispose);
            await disposal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CurrentContext.CancellationToken);
            // A captured late event must be ignored even while the first callback is still active.
            Assert.DoesNotThrow((Action)(() => handler(null, eventArgs)));
            Assert.DoesNotThrow((Action)scope.Dispose);
        }
        finally
        {
            // Release before joining either task so the old blocking implementation fails boundedly.
            release.Set();
            await (disposal is null ? cancel : Task.WhenAll(cancel, disposal)).WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Throws<ObjectDisposedException>((Action)(() =>
        {
            _ = scope.Token;
        }));
        Assert.DoesNotThrow((Action)(() => handler(null, eventArgs)));
    }

    [Test]
    public void EntrypointRejectsDefaultArgumentsBeforeDispatch()
    {
        var failure = Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(async () =>
        {
            _ = await Program.RunMainAsync(default, TestContext.CurrentContext.CancellationToken);
        }));
        Assert.That(failure!.ParamName, Is.EqualTo("args"));
    }

    [Test]
    public void ApplicationRejectsDefaultArgumentsBeforeOutputOrDiscovery()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var failure = Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(async () =>
        {
            _ = await ToolApplication.RunAsync(default, output, error,
                static _ => throw new AssertionException("Discovery must not start."),
                TestContext.CurrentContext.CancellationToken);
        }));
        Assert.That(failure!.ParamName, Is.EqualTo("args"));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Is.Empty);
    }

    [Test]
    public async Task ActualEntrypointDocumentsSupportedCommands()
    {
        Assert.That(await Program.Main(["--help"]), Is.Zero);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync([], output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.Zero);
        Assert.That(output.ToString(), Does.Contain("explain --result <result.json>").And.Contain("does not verify current source"));
    }

    [Test]
    public async Task EntrypointReturnsConventionalCancellationExitCode()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.That(await Program.RunMainAsync(["doctor"], cancellation.Token), Is.EqualTo(130));
    }

    [Test]
    [NonParallelizable]
    public void ConsoleInterruptHandlerCancelsOwnedTokenAndPreventsAbruptTermination()
    {
        using var scope = new ConsoleCancellationScope();
        var handlerField = typeof(ConsoleCancellationScope).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(handlerField, Is.Not.Null);
        var handler = handlerField!.GetValue(scope) as ConsoleCancelEventHandler;
        Assert.That(handler, Is.Not.Null);
        var eventArgs = Activator.CreateInstance(typeof(ConsoleCancelEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [ConsoleSpecialKey.ControlC], culture: CultureInfo.InvariantCulture) as ConsoleCancelEventArgs;
        Assert.That(eventArgs, Is.Not.Null);
        Assert.That(scope.Token.IsCancellationRequested, Is.False);
        handler!(null, eventArgs!);
        Assert.That(eventArgs!.Cancel, Is.True);
        Assert.That(scope.Token.IsCancellationRequested, Is.True);
        scope.Dispose();
        Assert.DoesNotThrow((Action)(() => handler!(null, eventArgs!)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DoctorReportsDiscoveryWithoutClaimingSolverOrProjectQualification(bool json)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(json ? ["doctor", "--format", "json"] : ["doctor"],
            output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.Zero);
        Assert.That(output.ToString(), Does.Contain("10.0.402").And.Contain("no project evaluation"));
        if (json)
        {
            using var report = JsonDocument.Parse(output.ToString());
            Assert.That(report.RootElement.GetProperty("command").GetString(), Is.EqualTo("doctor"));
        }
    }

    [Test]
    public async Task FailedSdkDiscoveryDoesNotReportSuccessfulDoctor()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(["doctor"], output, error,
            static _ => throw new TimeoutException("discovery deadline"), TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.Contain("discovery deadline"));
    }

    [TestCase("doctor --unknown")]
    [TestCase("doctor --json extra")]
    [TestCase("prove")]
    [TestCase("explain")]
    [TestCase("explain --json")]
    [TestCase("explain missing --line 0")]
    [TestCase("explain missing --line nope")]
    [TestCase("explain missing --line")]
    [TestCase("explain missing --file")]
    [TestCase("explain missing --json --json")]
    [TestCase("explain missing --unknown")]
    [TestCase("explain missing")]
    public async Task InvalidArgumentsAndUnavailableEvidenceReturnDiagnosticFailure(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(command.Split(' ').ToImmutableArray(), output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.StartWith("SharpProof: "));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExplainPreservesUnknownCoverageAndScopeInTextAndJson(bool json)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("sharpproof-explain-").FullName, "result.json");
        try
        {
            await File.WriteAllTextAsync(path, WorkerProtocolJson.SerializeResponse(CreateResponse()));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.That(await ToolApplication.RunAsync(json ? ["explain", "--result", path, "--format", "json"] : ["explain", "--result", path],
                output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.Zero, error.ToString());
            Assert.That(output.ToString(), Does.Contain("1 Unknown").And.Contain("1 incomplete callables"));
            if (json)
            {
                using var report = JsonDocument.Parse(output.ToString());
                Assert.That(report.RootElement.GetProperty("command").GetString(), Is.EqualTo("explain"));
            }
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("not json")]
    public async Task ExplainRejectsMalformedOrProtocolInvalidEvidence(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = Path.Combine(Directory.CreateTempSubdirectory("sharpproof-explain-").FullName, "result.json");
        try
        {
            await File.WriteAllTextAsync(path, content);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.That(await ToolApplication.RunAsync(["explain", "--result", path], output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
            Assert.That(output.ToString(), Is.Empty);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Test]
    public async Task ExplainRejectsOversizedEvidenceBeforeParsingOrRendering()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("sharpproof-explain-").FullName, "result.json");
        try
        {
            CreateOversizedEvidence(path);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.That(await ToolApplication.RunAsync(["explain", "--result", path], output, error, DiscoverSdk,
                TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Does.Contain("size limit"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Test]
    public async Task ExplainReportsRecordedInfrastructureFailureWithoutSemanticSuccess()
    {
        var response = CreateResponse() with
        {
            Manifest = new(),
            CallableResults = [],
            ClaimResults = [],
            RunStatus = WorkerRunStatus.Failed,
            FailureReason = WorkerRunFailureReason.InputUnavailable,
            Errors = [new() { Code = "input.unavailable", Message = "fixture failure" }]
        };
        WorkerProtocolJson.SealManifest(response.Manifest);
        var path = Path.Combine(Directory.CreateTempSubdirectory("sharpproof-explain-").FullName, "result.json");
        try
        {
            await File.WriteAllTextAsync(path, WorkerProtocolJson.SerializeResponse(response));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.That(await ToolApplication.RunAsync(["explain", "--result", path], output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.Zero, error.ToString());
            Assert.That(output.ToString(), Does.Contain("Run: Failed"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static Task<string> DiscoverSdk(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("10.0.402\n");
    }

    private static void CreateOversizedEvidence(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        file.SetLength(16 * 1024 * 1024 + 1);
    }

    private static WorkerVerifyResponse CreateResponse()
    {
        var location = new WorkerSourceLocation { Path = "Subject.cs", Start = 10, Length = 20, Line = 2, Column = 5 };
        var manifest = new WorkerClaimManifest
        {
            Callables = [new()
            {
                CallableId = "M:Subject.Identity(System.Int64)",
                SelectedFeatures = [WorkerSelectedFeature.Contracts],
                SelectionReasons = [WorkerSelectionReason.DiscoveredPostcondition],
                Location = location,
                ClaimIds = ["claim.0"]
            }],
            Claims = [new()
            {
                ClaimId = "claim.0",
                CallableId = "M:Subject.Identity(System.Int64)",
                Kind = WorkerClaimKind.Postcondition,
                Evidence = WorkerClaimEvidence.DirectClause,
                Location = location
            }]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            InputHash = new string('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [new()
            {
                CallableId = "M:Subject.Identity(System.Int64)",
                Coverage = WorkerCallableCoverage.Incomplete,
                Reason = WorkerCallableCoverageReason.SemanticUnknown
            }],
            ClaimResults = [new() { ClaimId = "claim.0", Outcome = WorkerClaimOutcome.Unknown, Reason = WorkerClaimReason.UnsupportedBody }],
            Summary = new()
            {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new()
                {
                    WorkerVersion = "test-worker",
                    ApiSpecVersion = "test-spec",
                    WorkerBinarySha256 = new string('a', 64),
                    ApiSpecContentSha256 = new string('b', 64)
                }
            }
        };
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True, "Fixture must satisfy the real protocol.");
        return response;
    }
}
