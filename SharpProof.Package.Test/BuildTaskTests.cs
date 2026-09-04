using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SharpProof.BuildTasks;
using SharpProof.Host;
using SharpProof.Worker;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class BuildTaskTests
{
    private static readonly string DotNetHost =
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    private const string ValidSupervisorNonce =
        "0123456789abcdef0123456789abcdef" +
        "0123456789abcdef0123456789abcdef";

    [Test]
    public async System.Threading.Tasks.Task
        MissingCompilerHostVersionFailsClosedUnlessProfileIsOff()
    {
        var enabled = await RunCompilerHostGateAsync(profile: null);
        var disabled = await RunCompilerHostGateAsync(profile: "off");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                enabled.ExitCode,
                Is.Not.Zero,
                enabled.Output);
            Assert.That(
                enabled.Output,
                Does.Contain("NETCoreSdkVersion is unset")
                    .And.Contain("Roslyn 4.14 or newer"));
            Assert.That(
                disabled.ExitCode,
                Is.Zero,
                disabled.Output);
        }
    }

    [Test]
    public void GeneratedSupervisorNoncePassesSupervisorGateValidation()
    {
        var nonce = RunVerifier.CreateSupervisorNonce();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nonce, Has.Length.EqualTo(64));
            Assert.That(nonce, Is.EqualTo(nonce.ToUpperInvariant()));
            Assert.That(VerifierProcessSupervisor.IsValidNonce(nonce), Is.True);
        }
    }

    [Test]
    public void SupervisorCleanupReceiptsRequireAnExactNonceAndRecord()
    {
        const string nonce = ValidSupervisorNonce;
        var output = "verifier output\nSharpProof.Armed/1 " + nonce +
            "\nSharpProof.Cleanup/1 " + nonce + "\n";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                RunVerifier.HasSupervisorProtocolRecord(
                    output,
                    "SharpProof.Armed/1",
                    nonce),
                Is.True);
            Assert.That(
                RunVerifier.HasSupervisorProtocolRecord(
                    output,
                    "SharpProof.Cleanup/1",
                    "f" + nonce[1..]),
                Is.False);
            Assert.That(
                RunVerifier.HasSupervisorProtocolRecord(
                    output,
                    "SharpProof.Cleanup/1 trailing",
                    nonce),
                Is.False);
        }
    }

    [Test]
    public void MissingCleanupReceiptInvokesContainmentFailureDecision()
    {
        var failure = string.Empty;
        using var task = new RunVerifier
        {
            ContainmentAuthenticationFailureOverride = message =>
                failure = message
        };

        var authenticated = task.RequireSupervisorCleanupReceipt(
            cleanupAuthenticated: false,
            authenticationRequired: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(authenticated, Is.False);
            Assert.That(failure, Does.Contain("cleanup receipt"));
        }
    }

    [Test]
    public async System.Threading.Tasks.Task
        VerifierOutputDrainIsBoundedAndStillAuthenticatesCleanup()
    {
        const string nonce = ValidSupervisorNonce;
        var input = "SharpProof.Armed/1 " + nonce + "\n" +
            new string(
                'x',
                RunVerifier.MaximumCapturedOutputCharacters + 1) +
            "\n\nSharpProof.Cleanup/1 " + nonce + "\n";
        using var signal = new ManualResetEventSlim();

        var result = await RunVerifier.ReadBoundedOutputAsync(
            new StringReader(input),
            nonce,
            signal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Text.Length,
                Is.EqualTo(
                    RunVerifier.MaximumCapturedOutputCharacters));
            Assert.That(result.LimitExceeded, Is.True);
            Assert.That(signal.IsSet, Is.True);
            Assert.That(result.SupervisorArmed, Is.True);
            Assert.That(result.CleanupAuthenticated, Is.True);
        }
    }

    [Test]
    public async System.Threading.Tasks.Task
        VerifierArmedStateIsPublishedIndependentlyOfOutputCompletion()
    {
        const string nonce = ValidSupervisorNonce;
        using var signal = new ManualResetEventSlim();
        var armed = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions
                .RunContinuationsAsynchronously);
        using var reader = new GatedTextReader(
            "SharpProof.Armed/1 " + nonce + "\n");

        var read = RunVerifier.ReadBoundedOutputAsync(
            reader,
            nonce,
            signal,
            armed);
        try
        {
            Assert.That(
                await armed.Task.WaitAsync(TimeSpan.FromSeconds(1)),
                Is.True);
            Assert.That(read.IsCompleted, Is.False);
        }
        finally
        {
            reader.Complete();
            await read;
        }
    }

    [Test]
    public async System.Threading.Tasks.Task
        VerifierCleanupStateIsPublishedIndependentlyOfOutputCompletion()
    {
        const string nonce = ValidSupervisorNonce;
        using var signal = new ManualResetEventSlim();
        var cleanup = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions
                .RunContinuationsAsynchronously);
        using var reader = new GatedTextReader(
            "SharpProof.Armed/1 " + nonce + "\n" +
            "SharpProof.Cleanup/1 " + nonce + "\n");

        var read = RunVerifier.ReadBoundedOutputAsync(
            reader,
            nonce,
            signal,
            supervisorCleanupSignal: cleanup);
        try
        {
            Assert.That(
                await cleanup.Task.WaitAsync(TimeSpan.FromSeconds(1)),
                Is.True);
            Assert.That(read.IsCompleted, Is.False);
        }
        finally
        {
            reader.Complete();
            await read;
        }
    }

    [Test]
    public void InterruptedAuthenticationWaitDefersIncompleteProtocolDrain()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                RunVerifier.ShouldDeferSupervisorAuthentication(
                    authenticationRequired: true,
                    outputCompleted: false),
                Is.True);
            Assert.That(
                RunVerifier.ShouldDeferSupervisorAuthentication(
                    authenticationRequired: true,
                    outputCompleted: true),
                Is.False);
        }
    }

    [Test]
    public void OutputDrainWaitRechecksInterruptionsBetweenBoundedSlices()
    {
        var interrupted = false;
        var waits = 0;
        var incomplete = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var completed = RunVerifier.WaitForOutputCompletion(
            incomplete.Task,
            timeoutMilliseconds: 1000,
            () => interrupted,
            milliseconds =>
            {
                Assert.That(
                    milliseconds,
                    Is.InRange(
                        1,
                        RunVerifier.OutputDrainPollingMilliseconds));
                waits++;
                interrupted = true;
                return false;
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completed, Is.False);
            Assert.That(waits, Is.EqualTo(1));
        }
    }

    [Test]
    public void OutputDrainWaitReturnsImmediatelyForCompletedOutput()
    {
        Assert.That(
            RunVerifier.WaitForOutputCompletion(
                System.Threading.Tasks.Task.CompletedTask,
                timeoutMilliseconds: 1000,
                static () => false,
                _ => throw new AssertionException(
                    "Completed output must not enter the polling wait.")),
            Is.True);
    }

    [Test]
    public void SupervisorReadinessWaitObservesArmedSignal()
    {
        var armed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var waits = 0;

        var result = RunVerifier.WaitForSupervisorReadiness(
            armed.Task,
            System.Threading.Tasks.Task.CompletedTask,
            static () => false,
            timeoutMilliseconds: 1000,
            _ =>
            {
                waits++;
                armed.TrySetResult(true);
                return true;
            });

        Assert.That(result, Is.EqualTo(RunVerifier.SupervisorReadiness.Armed));
        Assert.That(waits, Is.EqualTo(1));
    }

    [Test]
    public void SupervisorReadinessWaitObservesPreArmedExit()
    {
        var exited = false;
        var armed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var result = RunVerifier.WaitForSupervisorReadiness(
            armed.Task,
            System.Threading.Tasks.Task.CompletedTask,
            () => exited,
            timeoutMilliseconds: 1000,
            _ =>
            {
                exited = true;
                return false;
            });

        Assert.That(
            result,
            Is.EqualTo(RunVerifier.SupervisorReadiness.ExitedBeforeArmed));
    }

    [Test]
    public void SupervisorReadinessDoesNotInferPreArmedExitBeforeOutputDrain()
    {
        var armed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var result = RunVerifier.WaitForSupervisorReadiness(
            armed.Task,
            output.Task,
            static () => true,
            timeoutMilliseconds: 1,
            _ => false);

        Assert.That(
            result,
            Is.EqualTo(RunVerifier.SupervisorReadiness.NotReady));
    }

    [Test]
    public void SupervisorReadinessRechecksArmedAfterExitObservation()
    {
        var armed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var result = RunVerifier.WaitForSupervisorReadiness(
            armed.Task,
            System.Threading.Tasks.Task.CompletedTask,
            () =>
            {
                armed.TrySetResult(true);
                return true;
            },
            timeoutMilliseconds: 1000);

        Assert.That(result, Is.EqualTo(RunVerifier.SupervisorReadiness.Armed));
    }

    [Test]
    public void SupervisorReadinessWaitFailsClosedAtBound()
    {
        var armed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var result = RunVerifier.WaitForSupervisorReadiness(
            armed.Task,
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously).Task,
            static () => false,
            timeoutMilliseconds: 1,
            _ => false);

        Assert.That(
            result,
            Is.EqualTo(RunVerifier.SupervisorReadiness.NotReady));
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public async System.Threading.Tasks.Task
        RetainedCleanupAnchorRejectsMissingEventualReceipt()
    {
        const string nonce = ValidSupervisorNonce;
        var failure = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var process = Process.Start("/bin/true");
        Assert.That(process, Is.Not.Null);

        RunVerifier.RetainCleanupAnchorForTest(
            process!,
            System.Threading.Tasks.Task.FromResult(
                "SharpProof.Armed/1 " + nonce + "\n"),
            nonce,
            message => failure.TrySetResult(message));

        Assert.That(
            await failure.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            Does.Contain("cleanup receipt"));
        Assert.That(
            SpinWait.SpinUntil(
                () => RunVerifier.RetainedCleanupAnchorCount == 0,
                TimeSpan.FromSeconds(2)),
            Is.True);
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void UnterminatedVerifierOutputDoesNotCorruptCleanupReceipt()
    {
        using var directory = new TempDirectory("sharpproof-receipt-framing-");
        var helper = CreateTimedProcessAssembly(
            directory.FullName,
            "System.Console.Out.Write(\"partial\");");
        using var task = CreateVerifier(directory, helper, 2000, 1);

        Assert.That(task.Execute(), Is.True);
        Assert.That(task.ExitCode, Is.EqualTo(0));
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void OversizedVerifierOutputTriggersPromptBoundedContainment()
    {
        using var directory = new TempDirectory("sharpproof-output-limit-");
        var containmentFailure = string.Empty;
        var helper = CreateTimedProcessAssembly(
            directory.FullName,
            "System.Console.Out.Write(new string('x', " +
            (RunVerifier.MaximumCapturedOutputCharacters + 1)
                .ToString(CultureInfo.InvariantCulture) +
            ")); System.Threading.Thread.Sleep(5000);");
        using var task = CreateVerifier(directory, helper, 5000, 1000);
        task.ContainmentAuthenticationFailureOverride = message =>
            Volatile.Write(ref containmentFailure, message);
        var stopwatch = Stopwatch.StartNew();

        Assert.That(task.Execute(), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.ExitCode, Is.EqualTo(124));
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromSeconds(3)));
        }
        Assert.That(
            SpinWait.SpinUntil(
                () => RunVerifier.RetainedCleanupAnchorCount == 0,
                TimeSpan.FromSeconds(6)),
            Is.True,
            "The bounded output cleanup anchor did not drain.");
        Assert.That(
            Volatile.Read(ref containmentFailure),
            Is.Empty);
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void OversizedOutputWithIncompleteCleanupReturnsPromptly()
    {
        using var directory = new TempDirectory(
            "sharpproof-output-limit-retained-");
        var helper = CreateTimedProcessAssembly(
            directory.FullName,
            "System.Console.Out.Write(new string('x', " +
            (RunVerifier.MaximumCapturedOutputCharacters + 1)
                .ToString(CultureInfo.InvariantCulture) +
            ")); System.Threading.Thread.Sleep(1500);");
        using var task = CreateVerifier(directory, helper, 5000, 1);
        task.TryTerminateOverride = static (_, _, _) => false;
        var stopwatch = Stopwatch.StartNew();

        Assert.That(task.Execute(), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.ExitCode, Is.EqualTo(-1));
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromSeconds(1)));
            Assert.That(
                RunVerifier.RetainedCleanupAnchorCount,
                Is.GreaterThan(0));
        }
        Assert.That(
            SpinWait.SpinUntil(
                () => RunVerifier.RetainedCleanupAnchorCount == 0,
                TimeSpan.FromSeconds(3)),
            Is.True);
    }

    [TestCase("missing")]
    [TestCase("malformed")]
    [TestCase("stale-request")]
    [TestCase("complete-without-payload")]
    [Platform("Linux")]
    public void PublishedResultValidatorRejectsInvalidEvidence(string kind)
    {
        using var directory = new TempDirectory("sharpproof-result-binding-");
        var manifest = Path.Combine(directory.FullName, "compiler-manifest.json");
        var request = Path.Combine(directory.FullName, "request.json");
        var result = Path.Combine(directory.FullName, "result.json");
        File.WriteAllText(manifest, "{}");
        var manifestHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(manifest)));
        var requestJson = JsonSerializer.Serialize(new
        {
            protocolVersion = "11",
            compilerManifest = new { path = manifest, sha256 = manifestHash },
            budgets = new { },
            cache = new { },
            verifyPolicy = "Advisory",
            assumptionPolicy = "Allow"
        });
        File.WriteAllText(request, requestJson);
        if (kind == "malformed")
        {
            File.WriteAllText(result, "not json");
        }
        else if (kind == "stale-request")
        {
            File.WriteAllText(result, JsonSerializer.Serialize(new
            {
                protocolVersion = "11",
                requestHash = new string('0', 64),
                inputHash = new string('1', 64),
                runStatus = "Complete"
            }));
        }
        else if (kind == "complete-without-payload")
        {
            var requestHash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(request)));
            File.WriteAllText(
                result,
                JsonSerializer.Serialize(new
                {
                    protocolVersion = "11",
                    requestHash,
                    inputHash = new string('z', 64),
                    runStatus = "Complete"
                }));
        }

        var engine = new RecordingBuildEngine();
        var task = new ValidatePublishedVerificationResult
        {
            BuildEngine = engine,
            RequestPath = request,
            ResultPath = result,
            ManifestPath = manifest
        };

        Assert.That(task.Execute(), Is.False);
        Assert.That(engine.Errors, Is.Not.Empty);
    }

    [TestCase("invocation-result")]
    [TestCase("request")]
    [TestCase("result")]
    [TestCase("manifest")]
    [Platform("Linux")]
    public void PublishedResultValidatorRejectsOversizedProtocolFilesBeforeReading(
        string oversizedMember)
    {
        using var directory = new TempDirectory("sharpproof-result-size-");
        var manifest = Path.Combine(
            directory.FullName,
            "compiler-manifest.json");
        var requestPath = Path.Combine(
            directory.FullName,
            "request.json");
        var resultPath = Path.Combine(
            directory.FullName,
            "result.json");
        var invocationResultPath = Path.Combine(
            directory.FullName,
            "invocation-result.json");
        var manifestBytes = "{}"u8.ToArray();
        File.WriteAllBytes(manifest, manifestBytes);
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = manifest,
                Sha256 = WorkerProtocolJson.ComputeSha256(manifestBytes)
            }
        };
        var responseManifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(responseManifest);
        var response = new WorkerVerifyResponse
        {
            RequestHash = WorkerProtocolJson.ComputeRequestHash(request),
            InputHash = new('a', 64),
            Manifest = responseManifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Miss,
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "build-task-test",
                    ApiSpecVersion = "build-task-test"
                },
                Budgets = request.Budgets
            }
        };
        Assert.That(
            WorkerProtocolJson.Validate(request).IsValid,
            Is.True);
        Assert.That(
            WorkerProtocolJson.Validate(response).IsValid,
            Is.True);
        File.WriteAllText(
            requestPath,
            WorkerProtocolJson.SerializeRequest(request));
        File.WriteAllText(
            resultPath,
            WorkerProtocolJson.SerializeResponse(response));

        var oversizedPath = oversizedMember switch
        {
            "invocation-result" => invocationResultPath,
            "request" => requestPath,
            "result" => resultPath,
            "manifest" => manifest,
            _ => throw new InvalidOperationException(
                "Unknown oversized protocol member.")
        };
        using (var stream = new FileStream(
                   oversizedPath,
                   FileMode.OpenOrCreate,
                   FileAccess.Write,
                   FileShare.None))
        {
            stream.SetLength(WorkerProtocolJson.MaximumJsonBytes + 1L);
        }

        var engine = new RecordingBuildEngine();
        var task = new ValidatePublishedVerificationResult
        {
            BuildEngine = engine,
            RequestPath = requestPath,
            ResultPath = resultPath,
            ManifestPath = manifest,
            InvocationResultPath = oversizedMember == "invocation-result"
                ? invocationResultPath
                : null
        };

        Assert.That(task.Execute(), Is.False);
        Assert.That(
            engine.Errors.Single().Message,
            Does.Contain(
                $"exceeds the {WorkerProtocolJson.MaximumJsonBytes} byte limit"));
    }

    [Test]
    [Platform("Linux")]
    public void PublishedResultValidatorResolvesRelativePathsAgainstProjectDirectory()
    {
        using var parent = new TempDirectory("sharpproof-result-relative-");
        var project = Directory.CreateDirectory(
            Path.Combine(parent.FullName, "project"));
        var evidence = Directory.CreateDirectory(
            Path.Combine(project.FullName, "evidence"));
        File.WriteAllText(Path.Combine(evidence.FullName, "request.json"), "{}");
        var engine = new RecordingBuildEngine();
        var task = new ValidatePublishedVerificationResult
        {
            BuildEngine = engine,
            ProjectDirectory = project.FullName,
            RequestPath = Path.Combine("evidence", "request.json"),
            ResultPath = Path.Combine("evidence", "result.json"),
            ManifestPath = Path.Combine("evidence", "manifest.json")
        };

        Assert.That(task.Execute(), Is.False);
        Assert.That(
            engine.Errors.Single().Message,
            Does.Contain(
                    "SharpProof verification did not publish a valid current result")
                .And.Not.Contain("Could not find file"));
    }

    [Test]
    public void CanceledVerifierTaskDoesNotLaunchAProcess()
    {
        var engine = new RecordingBuildEngine();
        using var task = new RunVerifier
        {
            BuildEngine = engine,
            Executable = "dotnet",
            WorkingDirectory = TestContext.CurrentContext.WorkDirectory,
            Arguments = [new TaskItem("--info")]
        };

        task.Cancel();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(task, Is.InstanceOf<ICancelableTask>());
            Assert.That(task.Execute(), Is.True);
            Assert.That(task.ExitCode, Is.EqualTo(-1));
            Assert.That(engine.Errors, Is.Empty);
        }
    }

    [Test]
    public void VerifierWarningsReachTheMsBuildWarningChannel()
    {
        var engine = new RecordingBuildEngine();
        using var task = new RunVerifier { BuildEngine = engine };

        task.LogStandardError(
            "source.cs(12,3): warning SP0047: incomplete" + Environment.NewLine +
            "SharpProof: warning SP0048: assumptions" + Environment.NewLine +
            "source.cs(x,3): warning SP0047: malformed location" + Environment.NewLine +
            "worker stderr");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                engine.Warnings.Select(static warning => warning.Code),
                Is.EqualTo((string[])["SP0047", "SP0048"]));
            Assert.That(engine.Warnings[0].File, Is.EqualTo("source.cs"));
            Assert.That(engine.Warnings[0].LineNumber, Is.EqualTo(12));
            Assert.That(engine.Warnings[0].ColumnNumber, Is.EqualTo(3));
            Assert.That(
                engine.Messages.Select(static message => message.Message),
                Does.Contain("source.cs(x,3): warning SP0047: malformed location"));
            Assert.That(
                engine.Messages.Select(static message => message.Message),
                Does.Contain("worker stderr"));
        }
    }

    [Test]
    public void VerifierDiagnosticGrammarPreservesMarkerLikePathsAndSeverity()
    {
        var engine = new RecordingBuildEngine();
        using var task = new RunVerifier { BuildEngine = engine };

        task.LogStandardError(
            "/tmp/source: warning SP0047: detail.cs(4,5): warning SP0048: assumptions" +
            Environment.NewLine +
            "punctuation (draft), v2.cs(7,9): error SP0047: incomplete: detail" +
            Environment.NewLine +
            "SharpProof: error SP0048: strict assumptions");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.Warnings, Has.Count.EqualTo(1));
            Assert.That(engine.Warnings[0].Code, Is.EqualTo("SP0048"));
            Assert.That(
                engine.Warnings[0].File,
                Is.EqualTo("/tmp/source: warning SP0047: detail.cs"));
            Assert.That(engine.Warnings[0].LineNumber, Is.EqualTo(4));
            Assert.That(engine.Warnings[0].ColumnNumber, Is.EqualTo(5));
            Assert.That(engine.Warnings[0].Message, Is.EqualTo("assumptions"));

            Assert.That(engine.Errors, Has.Count.EqualTo(2));
            Assert.That(engine.Errors[0].Code, Is.EqualTo("SP0047"));
            Assert.That(
                engine.Errors[0].File,
                Is.EqualTo("punctuation (draft), v2.cs"));
            Assert.That(engine.Errors[0].LineNumber, Is.EqualTo(7));
            Assert.That(engine.Errors[0].ColumnNumber, Is.EqualTo(9));
            Assert.That(engine.Errors[1].Code, Is.EqualTo("SP0048"));
            Assert.That(engine.Errors[1].File, Is.Empty);
            Assert.That(task.HasStructuredError, Is.True);
        }
    }

    [Test]
    public void StructuredVerifierDiagnosticsPreserveArbitraryPathText()
    {
        var engine = new RecordingBuildEngine();
        using var task = new RunVerifier { BuildEngine = engine };
        var path = "/tmp/line\nbreak: warning SP0047: (draft), \u03c0.cs";
        var warning = VerifierDiagnosticTransport.Serialize(
            new VerifierDiagnostic(
                "warning",
                "SP0048",
                path,
                12,
                14,
                "assumptions: (user, trusted)"));
        var error = VerifierDiagnosticTransport.Serialize(
            new VerifierDiagnostic(
                "error",
                "SP0047",
                string.Empty,
                0,
                0,
                "strict incomplete"));
        var unknown = warning.Replace(
            "SP0048",
            "SP9999",
            StringComparison.Ordinal);

        task.LogStandardError(
            warning + Environment.NewLine +
            error + Environment.NewLine +
            unknown + Environment.NewLine +
            VerifierDiagnosticTransport.Prefix + "{malformed");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.Warnings, Has.Count.EqualTo(1));
            Assert.That(engine.Warnings[0].Code, Is.EqualTo("SP0048"));
            Assert.That(engine.Warnings[0].File, Is.EqualTo(path));
            Assert.That(engine.Warnings[0].LineNumber, Is.EqualTo(12));
            Assert.That(engine.Warnings[0].ColumnNumber, Is.EqualTo(14));
            Assert.That(
                engine.Warnings[0].Message,
                Is.EqualTo("assumptions: (user, trusted)"));
            Assert.That(engine.Errors, Has.Count.EqualTo(1));
            Assert.That(engine.Errors[0].Code, Is.EqualTo("SP0047"));
            Assert.That(engine.Errors[0].File, Is.Empty);
            Assert.That(task.HasStructuredError, Is.True);
            Assert.That(engine.Messages, Has.Count.EqualTo(2));
        }
    }

    [TestCase(6, true)]
    [TestCase(42, false)]
    [TestCase(124, false)]
    [Platform("Linux")]
    [NonParallelizable]
    public void StructuredErrorsSuppressOnlySemanticVerifierExitDiagnostics(
        int exitCode,
        bool suppressExitDiagnostic)
    {
        using var directory = new TempDirectory("sharpproof-structured-exit-");
        var diagnostic = VerifierDiagnosticTransport.Serialize(
            new VerifierDiagnostic(
                "error",
                "SP0047",
                "source.cs",
                1,
                1,
                "strict incomplete"));
        var helper = CreateTimedProcessAssembly(
            directory.FullName,
            "System.Console.Error.WriteLine(" +
            JsonSerializer.Serialize(diagnostic) +
            "); return " +
            exitCode.ToString(CultureInfo.InvariantCulture) +
            ";");
        var engine = new RecordingBuildEngine();
        using var task = CreateVerifier(
            directory,
            helper,
            2000,
            1000,
            engine);

        Assert.That(task.Execute(), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.ExitCode, Is.EqualTo(exitCode));
            Assert.That(
                engine.Errors.Select(static error => error.Code),
                Does.Contain("SP0047"));
            Assert.That(
                task.HasStructuredError,
                Is.EqualTo(suppressExitDiagnostic),
                "A partial semantic diagnostic must not suppress an " +
                "infrastructure exit diagnostic.");
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void DotNetHostValidationRejectsUntrustedForms()
    {
        var originalHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        using var directory = new TempDirectory("sharpproof-dotnet-host-");
        try
        {
            var trusted = RunVerifier.ResolveDotNetHost("dotnet");
            Assert.That(
                Assert.Throws<InvalidOperationException>(
                    (Action)(() => RunVerifier.ResolveDotNetHost(string.Empty)))!.Message,
                Does.Contain("direct dotnet muxer"));
            Assert.That(
                Assert.Throws<InvalidOperationException>(
                    (Action)(() => RunVerifier.ResolveDotNetHost("./dotnet")))!.Message,
                Does.Contain("direct dotnet muxer"));

            Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", null);
            Environment.SetEnvironmentVariable(
                "PATH",
                "relative" + Path.PathSeparator + ".");
            Assert.That(
                Assert.Throws<InvalidOperationException>(
                    (Action)(() => RunVerifier.ResolveDotNetHost("dotnet")))!.Message,
                Does.Contain("resolve a trusted dotnet muxer"));

            var wrongName = Path.Combine(directory.FullName, "not-dotnet");
            File.WriteAllText(wrongName, string.Empty);
            Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", wrongName);
            Assert.That(
                Assert.Throws<InvalidOperationException>(
                    (Action)(() => RunVerifier.ResolveDotNetHost("dotnet")))!.Message,
                Does.Contain("direct dotnet muxer"));

            var incompleteDirectory = Directory.CreateDirectory(
                Path.Combine(directory.FullName, "incomplete"));
            var incomplete = Path.Combine(incompleteDirectory.FullName, "dotnet");
            File.WriteAllText(incomplete, string.Empty);
            Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", incomplete);
            Assert.That(
                Assert.Throws<InvalidOperationException>(
                    (Action)(() => RunVerifier.ResolveDotNetHost("dotnet")))!.Message,
                Does.Contain("complete dotnet installation"));

            var alternateDirectory = Directory.CreateDirectory(
                Path.Combine(directory.FullName, "alternate"));
            Directory.CreateDirectory(
                Path.Combine(alternateDirectory.FullName, "host", "fxr"));
            var alternate = Path.Combine(alternateDirectory.FullName, "dotnet");
            File.Copy(trusted, alternate);
            Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", trusted);
            Assert.That(
                Assert.Throws<InvalidOperationException>(
                    (Action)(() => RunVerifier.ResolveDotNetHost(alternate)))!.Message,
                Does.Contain("trusted current dotnet muxer"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", originalHost);
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierTaskCapturesDotNetOutputAndErrors()
    {
        var outputEngine = new RecordingBuildEngine();
        using var outputTask = new RunVerifier
        {
            BuildEngine = outputEngine,
            Executable = "dotnet",
            WorkingDirectory = TestContext.CurrentContext.WorkDirectory,
            Arguments = [new TaskItem("--info")]
        };
        var errorEngine = new RecordingBuildEngine();
        using var errorTask = new RunVerifier
        {
            BuildEngine = errorEngine,
            Executable = "dotnet",
            WorkingDirectory = TestContext.CurrentContext.WorkDirectory,
            Arguments = [new TaskItem("--not-a-sharpproof-dotnet-option")]
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(outputTask.Execute(), Is.True);
            Assert.That(outputTask.ExitCode, Is.Zero);
            Assert.That(outputEngine.Messages, Is.Not.Empty);
            Assert.That(errorTask.Execute(), Is.True);
            Assert.That(errorTask.ExitCode, Is.Not.Zero);
            Assert.That(errorEngine.Messages, Is.Not.Empty);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void WorkerLauncherReserveRequiresLauncherAndOptionPosition()
    {
        const int projectWallTimeMilliseconds = 1234;
        var launcher = typeof(LauncherArguments).Assembly.Location;

        using var valid = CreateTask(
            launcher,
            "verify",
            "--project-wall-ms",
            projectWallTimeMilliseconds.ToString(CultureInfo.InvariantCulture));
        using var unrelated = CreateTask(
            Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "unrelated-verifier.dll"),
            "verify",
            "--project-wall-ms",
            projectWallTimeMilliseconds.ToString(CultureInfo.InvariantCulture));
        using var misplaced = CreateTask(
            launcher,
            "verify",
            "--worker",
            "--project-wall-ms");
        using var missingValue = CreateTask(
            launcher,
            "verify",
            "--project-wall-ms");
        using var malformedValue = CreateTask(
            launcher,
            "verify",
            "--project-wall-ms",
            "not-a-timeout");
        using var mismatchedValue = CreateTask(
            launcher,
            "verify",
            "--project-wall-ms",
            (projectWallTimeMilliseconds + 1).ToString(
                CultureInfo.InvariantCulture));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(HasWorkerLauncherBudget(valid), Is.True);
            Assert.That(HasWorkerLauncherBudget(unrelated), Is.False);
            Assert.That(HasWorkerLauncherBudget(misplaced), Is.False);
            Assert.That(HasWorkerLauncherBudget(missingValue), Is.False);
            Assert.That(HasWorkerLauncherBudget(malformedValue), Is.False);
            Assert.That(HasWorkerLauncherBudget(mismatchedValue), Is.False);
        }

        RunVerifier CreateTask(params string[] arguments)
        {
            return new RunVerifier
            {
                ProjectWallTimeMilliseconds = projectWallTimeMilliseconds,
                Arguments = arguments
                    .Select(static argument => new TaskItem(argument))
                    .ToArray()
            };
        }

        static bool HasWorkerLauncherBudget(RunVerifier task)
        {
            var method = typeof(RunVerifier).GetMethod(
                "HasWorkerLauncherBudgetArguments",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic) ??
                throw new InvalidOperationException(
                    "The worker-launcher budget classifier is unavailable.");
            return (bool)(method.Invoke(task, null) ?? false);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierTaskBoundsTheWholeLauncherProcess()
    {
        using var directory = new TempDirectory("sharpproof-launcher-timeout-");
        var containmentFailure = string.Empty;
        const int projectWallTimeMilliseconds = 2000;
        const int terminationGraceMilliseconds = 1000;
        var helper = CreateTimedProcessAssembly(directory.FullName);
        // Let the instrumented supervisor and child finish managed startup
        // before exercising the whole-process deadline. The cleanup reserve
        // also allows for container scheduling delay.
        using var task = CreateVerifier(
            directory,
            helper,
            projectWallTimeMilliseconds,
            terminationGraceMilliseconds);
        task.ContainmentAuthenticationFailureOverride = message =>
            Volatile.Write(ref containmentFailure, message);
        var maximumElapsed = TimeSpan.FromMilliseconds(
            RunVerifier.ComputeProcessTimeout(
                projectWallTimeMilliseconds,
                terminationGraceMilliseconds)) +
            TimeSpan.FromSeconds(1);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.That(task.Execute(), Is.True);
        stopwatch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.ExitCode, Is.EqualTo(124));
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(maximumElapsed));
        }
        Assert.That(
            SpinWait.SpinUntil(
                () => RunVerifier.RetainedCleanupAnchorCount == 0,
                TimeSpan.FromSeconds(6)),
            Is.True,
            "The launcher timeout cleanup anchor did not drain.");
        Assert.That(
            Volatile.Read(ref containmentFailure),
            Is.Empty);
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierPreLaunchSetupDoesNotConsumeCleanupReserve()
    {
        using var directory = new TempDirectory("sharpproof-launcher-setup-");
        var helper = CreateTimedProcessAssembly(
            directory.FullName,
            "System.Threading.Thread.Sleep(900);");
        using var task = CreateVerifier(directory, helper, 1200, 50);
        task.PreLaunchSetupOverride = () => Thread.Sleep(1500);

        Assert.That(task.Execute(), Is.True);
        Assert.That(task.ExitCode, Is.Zero);
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierTaskRejectsOverflowingTimeoutBeforeLaunch()
    {
        using var directory = new TempDirectory("sharpproof-launcher-overflow-");
        var marker = Path.Combine(directory.FullName, "started.txt");
        var helper = CreateTimedProcessAssembly(
            directory.FullName,
            "System.IO.File.WriteAllText(\"started.txt\", \"started\"); " +
            "System.Threading.Thread.Sleep(3000);");
        using var task = CreateVerifier(
            directory,
            helper,
            int.MaxValue,
            1);

        Assert.That(task.Execute(), Is.True);
        Thread.Sleep(250);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.ExitCode, Is.EqualTo(-1));
            Assert.That(File.Exists(marker), Is.False);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierTaskUsesOneDeadlineAndStopsOutputHoldingDescendants()
    {
        using var directory = new TempDirectory("sharpproof-launcher-descendant-");
        int? descendantId = null;
        try
        {
            var pidPath = Path.Combine(directory.FullName, "descendant.pid");
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "using System.Diagnostics; using System.IO; using System.Threading; " +
                "var start = new ProcessStartInfo(\"/bin/sleep\"); " +
                "start.ArgumentList.Add(\"10\"); start.UseShellExecute = false; " +
                "var child = Process.Start(start)!; " +
                "File.WriteAllText(\"descendant.pid\", child.Id.ToString()); " +
                "Thread.Sleep(800);");
            // Let the instrumented supervisor and child finish managed
            // startup before asserting descendant cleanup behavior.
            using var task = CreateVerifier(directory, helper, 2000, 50);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Assert.That(task.Execute(), Is.True);
            stopwatch.Stop();
            Assert.That(File.Exists(pidPath), Is.True);
            descendantId = int.Parse(
                File.ReadAllText(pidPath),
                CultureInfo.InvariantCulture);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(124));
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
                Assert.That(
                    SpinWait.SpinUntil(
                        () => !IsProcessRunning(descendantId.Value),
                        TimeSpan.FromSeconds(1)),
                    Is.True);
            }
        }
        finally
        {
            if (descendantId.HasValue && IsProcessRunning(descendantId.Value))
            {
                Process.GetProcessById(descendantId.Value).Kill(entireProcessTree: true);
            }
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierSupervisorStopsSessionEscapingDescendants()
    {
        using var directory = new TempDirectory("sharpproof-launcher-daemon-");
        int? descendantId = null;
        try
        {
            var pidPath = Path.Combine(directory.FullName, "daemon.pid");
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "using System.Diagnostics; using System.Threading; " +
                "var start = new ProcessStartInfo(\"/usr/bin/setsid\"); " +
                "start.ArgumentList.Add(\"/bin/sh\"); " +
                "start.ArgumentList.Add(\"-c\"); " +
                "start.ArgumentList.Add(\"exec >/dev/null 2>&1; echo $$ > daemon.pid; exec sleep 10\"); " +
                "start.UseShellExecute = false; Process.Start(start); " +
                "var wait = Stopwatch.StartNew(); " +
                "while (!System.IO.File.Exists(\"daemon.pid\") && wait.ElapsedMilliseconds < 500) Thread.Sleep(1);");
            using var task = CreateVerifier(directory, helper, 1000, 1);

            Assert.That(task.Execute(), Is.True);
            Assert.That(File.Exists(pidPath), Is.True);
            descendantId = int.Parse(
                File.ReadAllText(pidPath),
                CultureInfo.InvariantCulture);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(124));
                Assert.That(
                    SpinWait.SpinUntil(
                        () => !IsProcessRunning(descendantId.Value),
                        TimeSpan.FromSeconds(1)),
                    Is.True);
            }
        }
        finally
        {
            if (descendantId.HasValue && IsProcessRunning(descendantId.Value))
            {
                Process.GetProcessById(descendantId.Value)
                    .Kill(entireProcessTree: true);
            }
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierSupervisorReportsBoundedCleanupFailure()
    {
        using var descendant = Process.Start("/bin/sleep", "10");
        Assert.That(descendant, Is.Not.Null);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var cleanup = VerifierProcessSupervisor.StopDescendants(
                Environment.ProcessId,
                25,
                static _ => -1);
            stopwatch.Stop();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cleanup.HadDescendants, Is.True);
                Assert.That(cleanup.Complete, Is.False);
                Assert.That(
                    stopwatch.Elapsed,
                    Is.LessThan(TimeSpan.FromSeconds(1)));
            }
        }
        finally
        {
            if (descendant is { HasExited: false })
            {
                descendant.Kill(entireProcessTree: true);
                descendant.WaitForExit();
            }
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void RetainedCleanupAnchorRemainsOwnedUntilExit()
    {
        var process = Process.Start("/bin/sleep", "0.2");
        Assert.That(process, Is.Not.Null);
        RunVerifier.RetainCleanupAnchorForTest(process!);

        Assert.That(RunVerifier.RetainedCleanupAnchorCount, Is.GreaterThan(0));
        Assert.That(
            SpinWait.SpinUntil(
                () => RunVerifier.RetainedCleanupAnchorCount == 0,
                TimeSpan.FromSeconds(2)),
            Is.True);
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierExecutionRetainsLiveIncompleteCleanupAnchor()
    {
        using var directory = new TempDirectory("sharpproof-retained-cleanup-");
        var helper = CreateTimedProcessAssembly(
            directory.FullName,
            "using System.Threading; Thread.Sleep(1500);");
        using var task = CreateVerifier(directory, helper, 10, 1);
        task.TryTerminateOverride = static (_, _, _) => false;

        Assert.That(task.Execute(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.ExitCode, Is.EqualTo(-1));
            Assert.That(
                RunVerifier.RetainedCleanupAnchorCount,
                Is.GreaterThan(0));
        }
        Assert.That(
            SpinWait.SpinUntil(
                () => RunVerifier.RetainedCleanupAnchorCount == 0,
                TimeSpan.FromSeconds(3)),
            Is.True);
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public async System.Threading.Tasks.Task CancellationInterruptsForegroundWait()
    {
        using var directory = new TempDirectory("sharpproof-cancel-wait-");
        var helper = CreateTimedProcessAssembly(
            directory.FullName,
            "using System.Threading; Thread.Sleep(1500);");
        using var task = CreateVerifier(directory, helper, 300000, 1);
        task.TryTerminateOverride = static (_, _, _) => false;
        var execution = System.Threading.Tasks.Task.Run(task.Execute);
        Assert.That(
            SpinWait.SpinUntil(
                () => task.HasActiveProcess,
                TimeSpan.FromSeconds(2)),
            Is.True);

        task.Cancel();

        Assert.That(
            await execution.WaitAsync(TimeSpan.FromSeconds(2)),
            Is.True);
        Assert.That(task.ExitCode, Is.EqualTo(-1));
        Assert.That(
            SpinWait.SpinUntil(
                () => RunVerifier.RetainedCleanupAnchorCount == 0,
                TimeSpan.FromSeconds(3)),
            Is.True);
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void SupervisorContainsVerifierThatKillsItsImmediateParent()
    {
        using var directory = new TempDirectory("sharpproof-supervisor-anchor-");
        int? descendantId = null;
        try
        {
            var pidPath = Path.Combine(directory.FullName, "daemon.pid");
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "using System.Diagnostics; using System.Runtime.InteropServices; using System.Threading; " +
                "var start = new ProcessStartInfo(\"/usr/bin/setsid\"); " +
                "start.ArgumentList.Add(\"/bin/sh\"); start.ArgumentList.Add(\"-c\"); " +
                "start.ArgumentList.Add(\"exec >/dev/null 2>&1; echo $$ > daemon.pid; exec sleep 10\"); " +
                "start.UseShellExecute = false; Process.Start(start); " +
                "var wait = Stopwatch.StartNew(); while (!System.IO.File.Exists(\"daemon.pid\") && wait.ElapsedMilliseconds < 500) Thread.Sleep(1); " +
                "Native.Kill(Native.GetParent(), 9); Thread.Sleep(1000); " +
                "internal static class Native { [DllImport(\"libc\", EntryPoint=\"getppid\")] internal static extern int GetParent(); [DllImport(\"libc\", EntryPoint=\"kill\")] internal static extern int Kill(int processId, int signal); }");
            using var task = CreateVerifier(directory, helper, 2000, 1);

            Assert.That(task.Execute(), Is.True);
            Assert.That(File.Exists(pidPath), Is.True);
            descendantId = int.Parse(
                File.ReadAllText(pidPath),
                CultureInfo.InvariantCulture);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(124));
                Assert.That(
                    SpinWait.SpinUntil(
                        () => !IsProcessRunning(descendantId.Value),
                        TimeSpan.FromSeconds(1)),
                    Is.True);
            }
        }
        finally
        {
            if (descendantId.HasValue && IsProcessRunning(descendantId.Value))
            {
                Process.GetProcessById(descendantId.Value)
                    .Kill(entireProcessTree: true);
            }
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierTaskDoesNotReleaseCommandBeforePidFdAcquisition()
    {
        using var directory = new TempDirectory("sharpproof-launcher-gate-");
        var marker = Path.Combine(directory.FullName, "started.txt");
        var helper = CreateTimedProcessAssembly(
            directory.FullName,
            "using System.IO; File.WriteAllText(\"started.txt\", \"started\");");
        using var task = CreateVerifier(directory, helper);
        task.OpenPidFdOverride = static _ =>
            throw new InvalidOperationException("forced pidfd failure");

        Assert.That(task.Execute(), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.ExitCode, Is.EqualTo(-1));
            Assert.That(File.Exists(marker), Is.False);
            Assert.That(task.HasActiveProcess, Is.False);
        }
    }

    [Test]
    public void CanceledInvalidationDoesNotMutate()
    {
        var task = new InvalidatePublishedResult
        {
            BuildEngine = new RecordingBuildEngine()
        };

        task.Cancel();

        Assert.That(task.Execute(), Is.False);
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public async System.Threading.Tasks.Task ActiveVerifierTaskCancellationStopsTheProcess()
    {
        using var directory = new TempDirectory("sharpproof-cancel-");
        var helper = CreateTimedProcessAssembly(directory.FullName);
        var containmentFailure = string.Empty;
        using var task = CreateVerifier(directory, helper);
        task.ContainmentAuthenticationFailureOverride = message =>
            containmentFailure = message;

        var execution = System.Threading.Tasks.Task.Run(task.Execute);
        Assert.That(
            SpinWait.SpinUntil(
                () => task.HasActiveProcess,
                TimeSpan.FromSeconds(5)),
            Is.True,
            "The verifier child did not start.");

        task.Cancel();

        var completed = await System.Threading.Tasks.Task.WhenAny(
            execution,
            System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(500)));
        var canceledPromptly = ReferenceEquals(completed, execution);
        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(canceledPromptly, Is.True);
            Assert.That(await execution, Is.True);
            Assert.That(task.ExitCode, Is.Not.Zero);
            Assert.That(containmentFailure, Is.Empty);
        }
    }

    private static string CreateTimedProcessAssembly(
        string directory,
        string source = "using System.Threading; Thread.Sleep(3000);")
    {
        var assemblyPath = Path.Combine(directory, "TimedProcess.dll");
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "The trusted platform assembly list is unavailable.");
        var references = trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "TimedProcess",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        using (var stream = File.Create(assemblyPath))
        {
            var result = compilation.Emit(stream);
            Assert.That(
                result.Success,
                Is.True,
                string.Join(Environment.NewLine, result.Diagnostics));
        }
        File.WriteAllText(
            Path.ChangeExtension(assemblyPath, ".runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net9.0",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "9.0.0"
                }
              }
            }
            """);
        return assemblyPath;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var stat = File.ReadAllText(
                    $"/proc/{processId.ToString(CultureInfo.InvariantCulture)}/stat");
                var commandEnd = stat.LastIndexOf(')');
                return commandEnd < 0 ||
                    commandEnd + 2 >= stat.Length ||
                    stat[commandEnd + 2] != 'Z';
            }
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    [Platform("Linux")]
    [TestCase("cache-below-output")]
    [TestCase("output-below-cache")]
    [TestCase("input-below-output")]
    [TestCase("output-above-runtime")]
    [TestCase("output-below-runtime")]
    public void InvalidationRejectsSymmetricIoTopologyBeforeMutation(
        string collision)
    {
        using var directory = new TempDirectory("sharpproof-topology-");
        var tools = Directory.CreateDirectory(
            Path.Combine(directory.FullName, "tools"));
        var worker = Path.Combine(tools.FullName, "worker.dll");
        var launcher = Path.Combine(tools.FullName, "launcher.dll");
        var protocol = Path.Combine(tools.FullName, "protocol.dll");
        foreach (var path in new[] { worker, launcher, protocol })
        {
            File.WriteAllText(path, "runtime");
        }
        var result = Path.Combine(directory.FullName, "result.json");
        var request = Path.Combine(directory.FullName, "request.json");
        var manifest = Path.Combine(directory.FullName, "manifest.json");
        var cache = Path.Combine(directory.FullName, "cache");
        var invocationRequest = Path.Combine(
            directory.FullName,
            "runs",
            "request.json");
        switch (collision)
        {
            case "cache-below-output":
                cache = Path.Combine(result, "cache");
                break;
            case "output-below-cache":
                result = Path.Combine(cache, "result.json");
                break;
            case "input-below-output":
                invocationRequest = Path.Combine(result, "request.json");
                break;
            case "output-above-runtime":
                result = tools.FullName;
                break;
            case "output-below-runtime":
                result = Path.Combine(tools.FullName, "result.json");
                break;
            default:
                throw new AssertionException("Unknown topology fixture.");
        }
        var engine = new RecordingBuildEngine();
        var task = new InvalidatePublishedResult
        {
            BuildEngine = engine,
            ProjectDirectory = directory.FullName,
            ResultPath = result,
            RequestPath = request,
            ManifestPath = manifest,
            InvocationRequestPath = invocationRequest,
            WorkerPath = worker,
            LauncherPath = launcher,
            WorkerProtocolPath = protocol,
            CachePath = cache
        };

        Assert.That(task.Execute(), Is.False);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.Errors, Is.Not.Empty);
            Assert.That(File.Exists(result), Is.False);
            Assert.That(
                new[] { request, manifest, result }
                    .Select(LinuxPathIdentity.PublicationLockName),
                Has.None.Matches<string>(File.Exists));
            Assert.That(
                new[] { request, manifest, result }
                    .Select(LinuxPathIdentity.PublicationMarkerPath),
                Has.None.Matches<string>(File.Exists));
        }
    }

    [Test]
    [Platform("Linux")]
    public void InvalidationDeletesOnlyThePublishedOutputs()
    {
        using var directory = new TempDirectory("sharpproof-task-");
        var publication = Directory.CreateDirectory(
            Path.Combine(directory.FullName, "publication"));
        var tools = Directory.CreateDirectory(
            Path.Combine(directory.FullName, "tools"));
        var result = Path.Combine(publication.FullName, "result.json");
        var sarif = Path.Combine(publication.FullName, "result.sarif");
        var request = Path.Combine(publication.FullName, "request.json");
        var manifest = Path.Combine(publication.FullName, "manifest.json");
        var worker = Path.Combine(tools.FullName, "worker.dll");
        var launcher = Path.Combine(tools.FullName, "launcher.dll");
        var protocol = Path.Combine(tools.FullName, "protocol.dll");
        using (LinuxPathIdentity.AcquirePublicationSet(
                   [request, result, manifest, sarif],
                   TimeSpan.FromSeconds(5)))
        {
        }
        foreach (var path in new[]
                 {
                         result,
                         sarif,
                         request,
                         manifest,
                         worker,
                         launcher,
                         protocol
                     })
        {
            File.WriteAllText(path, Path.GetFileName(path));
        }

        var engine = new RecordingBuildEngine();
        var task = new InvalidatePublishedResult
        {
            BuildEngine = engine,
            ResultPath = result,
            SarifPath = sarif,
            RequestPath = request,
            ManifestPath = manifest,
            ProjectDirectory = directory.FullName,
            WorkerPath = worker,
            LauncherPath = launcher,
            WorkerProtocolPath = protocol,
            CachePath = Path.Combine(directory.FullName, "cache")
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.Execute(), Is.True);
            Assert.That(File.Exists(result), Is.False);
            Assert.That(File.Exists(sarif), Is.False);
            Assert.That(File.ReadAllText(request), Is.EqualTo("request.json"));
            Assert.That(File.ReadAllText(manifest), Is.EqualTo("manifest.json"));
            Assert.That(File.ReadAllText(worker), Is.EqualTo("worker.dll"));
            Assert.That(File.ReadAllText(launcher), Is.EqualTo("launcher.dll"));
            Assert.That(File.ReadAllText(protocol), Is.EqualTo("protocol.dll"));
            Assert.That(engine.Errors, Is.Empty);
        }
    }

    [Test]
    [Platform("Linux")]
    public void EveryPublicationMemberRejectsEveryCompilerOwnedOutput()
    {
        using var directory = new TempDirectory("sharpproof-compiler-output-");
        var tools = Directory.CreateDirectory(
            Path.Combine(directory.FullName, "tools"));
        var worker = Path.Combine(tools.FullName, "worker.dll");
        var launcher = Path.Combine(tools.FullName, "launcher.dll");
        var protocol = Path.Combine(tools.FullName, "protocol.dll");
        foreach (var path in new[] { worker, launcher, protocol })
        {
            File.WriteAllText(path, Path.GetFileName(path));
        }

        var compilerNames = new[]
        {
                "Consumer.dll",
                "obj/Consumer.dll",
                "Consumer.xml",
                "obj/Consumer.pdb",
                "obj/ref/Consumer.dll",
                "obj/refint/Consumer.dll",
                "obj/Consumer.AssemblyInfo.cs",
                "obj/Consumer.GeneratedMSBuildEditorConfig.editorconfig",
                "obj/Consumer.deps.json",
                "obj/Consumer.runtimeconfig.json"
            };
        foreach (var compilerName in compilerNames)
        {
            foreach (var member in new[]
                     {
                             "request", "result", "manifest", "sarif",
                             "invocation-request", "invocation-result",
                             "invocation-manifest"
                         })
            {
                var root = Directory.CreateDirectory(Path.Combine(
                    directory.FullName,
                    Guid.NewGuid().ToString("N")));
                var request = Path.Combine(root.FullName, "request.json");
                var result = Path.Combine(root.FullName, "result.json");
                var manifest = Path.Combine(root.FullName, "manifest.json");
                var sarif = Path.Combine(root.FullName, "result.sarif");
                var invocationRequest = Path.Combine(
                    root.FullName,
                    "invocation-request.json");
                var invocationResult = Path.Combine(
                    root.FullName,
                    "invocation-result.json");
                var invocationManifest = Path.Combine(
                    root.FullName,
                    "invocation-manifest.json");
                var compilerOutput = Path.Combine(root.FullName, compilerName);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(compilerOutput)!);
                switch (member)
                {
                    case "request":
                        request = compilerOutput;
                        break;
                    case "result":
                        result = compilerOutput;
                        break;
                    case "manifest":
                        manifest = compilerOutput;
                        break;
                    case "sarif":
                        sarif = compilerOutput;
                        break;
                    case "invocation-request":
                        invocationRequest = compilerOutput;
                        break;
                    case "invocation-result":
                        invocationResult = compilerOutput;
                        break;
                    case "invocation-manifest":
                        invocationManifest = compilerOutput;
                        break;
                }
                var task = new InvalidatePublishedResult
                {
                    BuildEngine = new RecordingBuildEngine(),
                    ResultPath = result,
                    RequestPath = request,
                    ManifestPath = manifest,
                    SarifPath = sarif,
                    InvocationRequestPath = invocationRequest,
                    InvocationResultPath = invocationResult,
                    InvocationManifestPath = invocationManifest,
                    ProjectDirectory = root.FullName,
                    WorkerPath = worker,
                    LauncherPath = launcher,
                    WorkerProtocolPath = protocol,
                    CompilerOutputPaths = [new TaskItem(compilerOutput)]
                };

                Assert.That(
                    task.Execute(),
                    Is.False,
                    $"{member} -> {compilerName}");
                Assert.That(File.Exists(compilerOutput), Is.False);
                foreach (var publication in new[]
                         {
                                 request, result, manifest, sarif,
                                 invocationRequest, invocationResult,
                                 invocationManifest
                             })
                {
                    Assert.That(
                        File.Exists(
                            LinuxPathIdentity.PublicationMarkerPath(
                                publication)),
                        Is.False,
                        $"{member} -> {compilerName}");
                }
            }
        }
    }

    [Test]
    [Platform("Linux")]
    public async System.Threading.Tasks.Task PublicationResetRemovesOnlyCompleteOwnedSet()
    {
        using var directory = new TempDirectory("sharpproof-publication-reset-");
        var request = Path.Combine(directory.FullName, "request.json");
        var oldResult = Path.Combine(directory.FullName, "result-a.json");
        var newResult = Path.Combine(directory.FullName, "result-b.json");
        var manifest = Path.Combine(directory.FullName, "manifest.json");
        var unrelated = Path.Combine(directory.FullName, "neighbor.txt");
        var setA = new[] { request, oldResult, manifest };
        using (LinuxPathIdentity.AcquirePublicationSet(
                   setA,
                   TimeSpan.FromSeconds(5)))
        {
        }
        foreach (var path in setA.Append(unrelated))
        {
            await File.WriteAllTextAsync(path, Path.GetFileName(path));
        }
        var setB = new[] { request, newResult, manifest };
        Assert.That(
            (Action)(() =>
            {
                using var unexpected =
                    LinuxPathIdentity.AcquirePublicationSet(
                        setB,
                        TimeSpan.FromSeconds(1));
            }),
            Throws.TypeOf<IOException>());

        var reset = new ResetPublishedVerification
        {
            BuildEngine = new RecordingBuildEngine(),
            RequestPath = request,
            ResultPath = oldResult,
            ManifestPath = manifest
        };
        Assert.That(reset.Execute(), Is.True);
        using (LinuxPathIdentity.AcquirePublicationSet(
                   setB,
                   TimeSpan.FromSeconds(5)))
        {
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(oldResult), Is.False);
            Assert.That(
                File.Exists(LinuxPathIdentity.PublicationMarkerPath(
                    oldResult)),
                Is.False);
            Assert.That(
                setB.All(path => File.Exists(
                    LinuxPathIdentity.PublicationMarkerPath(path))),
                Is.True);
            Assert.That(File.Exists(unrelated), Is.True);
        }
        Assert.That(reset.Execute(), Is.False, "set A is no longer complete");

        LinuxPathIdentity.ResetPublicationSet(
            setB,
            TimeSpan.FromSeconds(5));
        LinuxPathIdentity.ResetPublicationSet(
            setB,
            TimeSpan.FromSeconds(5));
    }

    [Test]
    [Platform("Linux")]
    public async System.Threading.Tasks.Task PublicationResetResolvesRelativePathsAgainstProjectDirectory()
    {
        using var parent = new TempDirectory(
            "sharpproof-publication-reset-relative-");
        var project = Directory.CreateDirectory(
            Path.Combine(parent.FullName, "project"));
        var evidence = Directory.CreateDirectory(
            Path.Combine(project.FullName, "evidence"));
        var names = new[]
        {
                "request.json", "result.json", "manifest.json"
            };
        var paths = names
            .Select(name => Path.Combine(evidence.FullName, name))
            .ToArray();
        using (LinuxPathIdentity.AcquirePublicationSet(
                   paths,
                   TimeSpan.FromSeconds(5)))
        {
        }
        foreach (var path in paths)
        {
            await File.WriteAllTextAsync(path, Path.GetFileName(path));
        }

        var reset = new ResetPublishedVerification
        {
            BuildEngine = new RecordingBuildEngine(),
            ProjectDirectory = project.FullName,
            RequestPath = Path.Combine("evidence", names[0]),
            ResultPath = Path.Combine("evidence", names[1]),
            ManifestPath = Path.Combine("evidence", names[2])
        };

        Assert.That(reset.Execute(), Is.True);
        Assert.That(paths.Any(File.Exists), Is.False);
    }

    [Test]
    [Platform("Linux")]
    public void PublicationResetRejectsPartialOwnershipWithoutDeletingMembers()
    {
        using var directory = new TempDirectory(
            "sharpproof-publication-reset-partial-");
        var set = new[]
        {
                Path.Combine(directory.FullName, "request.json"),
                Path.Combine(directory.FullName, "result.json"),
                Path.Combine(directory.FullName, "manifest.json")
            };
        using (LinuxPathIdentity.AcquirePublicationSet(
                   set,
                   TimeSpan.FromSeconds(5)))
        {
        }
        foreach (var path in set)
        {
            File.WriteAllText(path, Path.GetFileName(path));
        }
        File.Delete(LinuxPathIdentity.PublicationMarkerPath(set[1]));

        Assert.That(
            (Action)(() => LinuxPathIdentity.ResetPublicationSet(
                set,
                TimeSpan.FromSeconds(5))),
            Throws.TypeOf<IOException>());
        Assert.That(set.All(File.Exists), Is.True);
    }

    [Test]
    [Platform("Linux")]
    public void PublicationResetRecoversInterruptedMarkerCleanupWhenMembersAreAbsent()
    {
        using var directory = new TempDirectory(
            "sharpproof-publication-reset-recovery-");
        var set = new[]
        {
                Path.Combine(directory.FullName, "request.json"),
                Path.Combine(directory.FullName, "result.json"),
                Path.Combine(directory.FullName, "manifest.json")
            };
        using (LinuxPathIdentity.AcquirePublicationSet(
                   set,
                   TimeSpan.FromSeconds(5)))
        {
        }
        File.Delete(LinuxPathIdentity.PublicationMarkerPath(set[1]));

        Assert.That(
            (Action)(() => LinuxPathIdentity.ResetPublicationSet(
                set,
                TimeSpan.FromSeconds(5))),
            Throws.Nothing);
        Assert.That(
            set.Any(path => File.Exists(
                LinuxPathIdentity.PublicationMarkerPath(path))),
            Is.False);
    }

    [Test]
    [Platform("Linux")]
    public async System.Threading.Tasks.Task InvalidationCancellationInterruptsPublicationLockWait()
    {
        using var directory = new TempDirectory(
            "sharpproof-invalidation-cancel-");
        var tools = Directory.CreateDirectory(
            Path.Combine(directory.FullName, "tools"));
        var result = Path.Combine(directory.FullName, "result.json");
        var request = Path.Combine(directory.FullName, "request.json");
        var manifest = Path.Combine(directory.FullName, "manifest.json");
        var worker = Path.Combine(tools.FullName, "worker.dll");
        var launcher = Path.Combine(tools.FullName, "launcher.dll");
        var protocol = Path.Combine(tools.FullName, "protocol.dll");
        var publicationPaths = new[] { request, result, manifest };
        using (LinuxPathIdentity.AcquirePublicationSet(
                   publicationPaths,
                   TimeSpan.FromSeconds(5)))
        {
        }
        foreach (var path in new[]
                 {
                         result,
                         request,
                         manifest,
                         worker,
                         launcher,
                         protocol
                     })
        {
            await File.WriteAllTextAsync(path, Path.GetFileName(path));
        }

        var task = new InvalidatePublishedResult
        {
            BuildEngine = new RecordingBuildEngine(),
            ResultPath = result,
            RequestPath = request,
            ManifestPath = manifest,
            ProjectDirectory = directory.FullName,
            WorkerPath = worker,
            LauncherPath = launcher,
            WorkerProtocolPath = protocol
        };
        if ((object)task is not ICancelableTask cancelable)
        {
            Assert.Fail("Invalidation must implement the MSBuild cancellation boundary.");
            return;
        }

        using var lockAcquired = new ManualResetEventSlim();
        using var releaseLock = new ManualResetEventSlim();
        var lockTask = System.Threading.Tasks.Task.Run(() =>
        {
            using var held = LinuxPathIdentity.AcquirePublicationSet(
                publicationPaths,
                TimeSpan.FromSeconds(5));
            lockAcquired.Set();
            releaseLock.Wait(TimeSpan.FromSeconds(10));
        });
        try
        {
            Assert.That(
                lockAcquired.Wait(TimeSpan.FromSeconds(5)),
                Is.True);
            var execution = System.Threading.Tasks.Task.Run(task.Execute);
            await System.Threading.Tasks.Task.Delay(100);
            Assert.That(execution.IsCompleted, Is.False);

            cancelable.Cancel();

            Assert.That(
                await execution.WaitAsync(TimeSpan.FromSeconds(2)),
                Is.False);
            Assert.That(File.Exists(result), Is.True);
        }
        finally
        {
            releaseLock.Set();
            await lockTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static RunVerifier CreateVerifier(
        TempDirectory directory,
        string helper,
        int wallTimeMilliseconds = 300000,
        int graceMilliseconds = 1000,
        RecordingBuildEngine? buildEngine = null)
    {
        return new RunVerifier
        {
            BuildEngine = buildEngine ?? new RecordingBuildEngine(),
            Executable = DotNetHost,
            WorkingDirectory = directory.FullName,
            Arguments = [new TaskItem(helper)],
            ProjectWallTimeMilliseconds = wallTimeMilliseconds,
            TerminationGraceMilliseconds = graceMilliseconds
        };
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public List<BuildWarningEventArgs> Warnings { get; } = [];
        public List<BuildMessageEventArgs> Messages { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public void LogErrorEvent(BuildErrorEventArgs e)
        {
            Errors.Add(e);
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
            Warnings.Add(e);
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
            Messages.Add(e);
        }

        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs)
        {
            return false;
        }
    }

    private static async System.Threading.Tasks.Task<
        (int ExitCode, string Output)> RunCompilerHostGateAsync(
            string? profile)
    {
        var repository = TestRepository.FindRoot();
        var targets = Path.Combine(
            repository,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.targets");
        var placeholder = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.CompilerHostGate");
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable(
                "DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
                 {
                     "msbuild",
                     targets,
                     "-t:_SharpProofValidateConfiguration",
                     "-p:SharpProofAnalyzerDirectory=" + placeholder,
                     "-p:SharpProofCollectorDirectory=" + placeholder,
                     "-p:_SharpProofSharedDirectory=" + placeholder,
                     "--nologo",
                     "--verbosity:minimal"
                 })
        {
            start.ArgumentList.Add(argument);
        }
        if (profile != null)
        {
            start.ArgumentList.Add("-p:SharpProofProfile=" + profile);
        }

        using var process = Process.Start(start) ??
            throw new InvalidOperationException(
                "The compiler-host gate process could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput + await standardError;
        return (process.ExitCode, output);
    }

    private sealed class GatedTextReader(string initialText) : TextReader
    {
        private readonly System.Threading.Tasks.TaskCompletionSource<bool>
            completion = new(
                System.Threading.Tasks.TaskCreationOptions
                    .RunContinuationsAsynchronously);
        private int position;

        public void Complete()
        {
            completion.TrySetResult(true);
        }

        public override async System.Threading.Tasks.Task<int> ReadAsync(
            char[] buffer,
            int index,
            int count)
        {
            if (position < initialText.Length)
            {
                var copied = Math.Min(count, initialText.Length - position);
                initialText.CopyTo(position, buffer, index, copied);
                position += copied;
                return copied;
            }
            await completion.Task.ConfigureAwait(false);
            return 0;
        }
    }
}
