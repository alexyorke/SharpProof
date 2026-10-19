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
    public async Task ActualEntrypointDocumentsSupportedCommands()
    {
        Assert.That(await Program.Main(["--help"]), Is.Zero);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync([], output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.Zero);
        Assert.That(output.ToString(), Does.Contain("explain <result.json>").And.Contain("does not verify current source"));
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
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DoctorReportsDiscoveryWithoutClaimingSolverOrProjectQualification(bool json)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(json ? ["doctor", "--json"] : ["doctor"],
            output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.Zero);
        Assert.That(output.ToString(), Does.Contain("10.0.402").And.Contain("no project evaluation"));
        if (json)
        {
            using var report = JsonDocument.Parse(output.ToString());
            Assert.That(report.RootElement.GetProperty("sdk").GetString(), Is.EqualTo("10.0.402"));
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
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, WorkerProtocolJson.SerializeResponse(CreateResponse()));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.That(await ToolApplication.RunAsync(json ? ["explain", path, "--json"] : ["explain", path],
                output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.Zero, error.ToString());
            Assert.That(output.ToString(), Does.Contain("Stored evidence only").And.Contain("Unknown")
                .And.Contain("UnsupportedBody").And.Contain("Incomplete").And.Contain("Subject.cs"));
            if (json)
            {
                using var report = JsonDocument.Parse(output.ToString());
                Assert.That(report.RootElement.GetProperty("entries").GetArrayLength(), Is.EqualTo(2));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase("Subject.cs", "2", 2)]
    [TestCase("Subject.cs", "3", 0)]
    [TestCase("subject.cs", "2", 0)]
    public async Task ExactFileAndStartLineFiltersNeverInventAnOutcome(string file, string line, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(line);
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, WorkerProtocolJson.SerializeResponse(CreateResponse()));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.That(await ToolApplication.RunAsync(["explain", path, "--file", file, "--line", line, "--json"],
                output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.Zero);
            using var report = JsonDocument.Parse(output.ToString());
            Assert.That(report.RootElement.GetProperty("entries").GetArrayLength(), Is.EqualTo(expectedCount));
            Assert.That(report.RootElement.GetProperty("selectedClaims").GetInt32(), Is.EqualTo(1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("not json")]
    public async Task ExplainRejectsMalformedOrProtocolInvalidEvidence(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, content);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.That(await ToolApplication.RunAsync(["explain", path], output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
            Assert.That(output.ToString(), Is.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ExplainRejectsOversizedEvidenceBeforeParsingOrRendering()
    {
        var path = Path.GetTempFileName();
        try
        {
            CreateOversizedEvidence(path);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.That(await ToolApplication.RunAsync(["explain", path], output, error, DiscoverSdk,
                TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Does.Contain("byte limit"));
        }
        finally
        {
            File.Delete(path);
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
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, WorkerProtocolJson.SerializeResponse(response));
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.That(await ToolApplication.RunAsync(["explain", path], output, error, DiscoverSdk, TestContext.CurrentContext.CancellationToken), Is.Zero, error.ToString());
            Assert.That(output.ToString(), Does.Contain("Failed").And.Contain("input.unavailable: fixture failure")
                .And.Contain("Matching entries: 0"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ActualSdkDiscoveryRunsNormalCliSelection()
    {
        Assert.That(await SdkProbe.ReadAsync(TestContext.CurrentContext.CancellationToken), Does.Match(@"^\d+\.\d+\.\d+"));
    }

    [TestCase("exit 7")]
    [TestCase("exit 0")]
    [TestCase("printf '%5000s' x")]
    [TestCase("printf '10.0.402'; printf '%5000s' x >&2")]
    [Platform("Linux")]
    public void SdkDiscoveryRejectsFailureEmptyAndOversizedOutput(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () =>
        {
            _ = await SdkProbe.RunAsync("/bin/sh", ["-c", command], TimeSpan.FromSeconds(5), TestContext.CurrentContext.CancellationToken);
        }));
    }

    [Test]
    public void SdkDiscoveryDoesNotHideMissingExecutable()
    {
        var missing = Path.Combine(Path.GetTempPath(), "sharpproof-missing-sdk-" + Guid.NewGuid().ToString("N"));
        Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(new Func<Task>(async () =>
        {
            _ = await SdkProbe.RunAsync(missing, ["--version"], TimeSpan.FromSeconds(5), TestContext.CurrentContext.CancellationToken);
        }));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(4294967295)]
    public void SdkDiscoveryRejectsInvalidDeadlinesBeforeStartingAProcess(long milliseconds)
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(new Func<Task>(async () =>
        {
            _ = await SdkProbe.RunAsync("must-not-start", [], TimeSpan.FromMilliseconds(milliseconds),
                TestContext.CurrentContext.CancellationToken);
        }));
    }

    [Test]
    [Platform("Linux")]
    public async Task TerminationSafelyAcceptsAnAlreadyExitedOwnedProcess()
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new("/bin/sh") { UseShellExecute = false }
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("exit 0");
        process.Start();
        await process.WaitForExitAsync(TestContext.CurrentContext.CancellationToken);
        SdkProbe.TerminateAndJoin(process);
        Assert.That(process.ExitCode, Is.Zero);
    }

    [Test]
    [Platform("Linux")]
    public void SdkDiscoveryDeadlineTerminatesAndJoinsOwnedProcessAndPipes()
    {
        Assert.ThrowsAsync<TimeoutException>(new Func<Task>(async () =>
        {
            _ = await SdkProbe.RunAsync("/bin/sh", ["-c", "sleep 60"], TimeSpan.FromMilliseconds(100), TestContext.CurrentContext.CancellationToken);
        }));
    }

    [Test]
    [Platform("Linux")]
    public void CallerCancellationTerminatesDiscoveryAndRemainsCancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.CatchAsync<OperationCanceledException>(new Func<Task>(async () =>
        {
            _ = await SdkProbe.RunAsync("/bin/sh", ["-c", "sleep 60"], TimeSpan.FromSeconds(15), cancellation.Token);
        }));
    }

    private static Task<string> DiscoverSdk(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("10.0.402\n");
    }

    private static void CreateOversizedEvidence(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
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
