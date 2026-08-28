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
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class BuildTaskTests
{
    [TestCase(10_000, 1_000, 0, 9_000)]
    [TestCase(10_000, 1_000, 2_500, 6_500)]
    [TestCase(10_000, 1_000, 9_500, 0)]
    public void ForegroundVerifierBudgetPreservesCleanupReserve(
        int processTimeoutMilliseconds,
        int cleanupReserveMilliseconds,
        int elapsedMilliseconds,
        int expected)
    {
        Assert.That(
            RunVerifier.ComputeForegroundTimeout(
                processTimeoutMilliseconds,
                cleanupReserveMilliseconds,
                elapsedMilliseconds),
            Is.EqualTo(expected));
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
        const string nonce =
            "0123456789abcdef0123456789abcdef" +
            "0123456789abcdef0123456789abcdef";
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

    [TestCase(0, 0, true)]
    [TestCase(-1, 3, true)]
    [TestCase(-1, 5, false)]
    public void ExitedSessionLeaderStillTriggersProcessGroupKill(
        int stopResult,
        int stopError,
        bool expected)
    {
        Assert.That(
            RunVerifier.ShouldKillProcessGroupAfterStop(stopResult, stopError),
            Is.EqualTo(expected));
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void SupervisorBoundsIncompleteCleanupWithoutAuthenticatingIt()
    {
        const string nonce =
            "0123456789abcdef0123456789abcdef" +
            "0123456789abcdef0123456789abcdef";
        var originalInput = Console.In;
        var originalOutput = Console.Out;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Console.SetIn(new StringReader(
                "SharpProof.Start/1 " + nonce + Environment.NewLine));
            Console.SetOut(output);
            VerifierProcessSupervisor.StopDescendantsOverrideForTest =
                static (_, _) => new VerifierProcessSupervisor.DescendantStopResult(
                    HadDescendants: false,
                    Complete: false);

            var exitCode = VerifierProcessSupervisor.Run(["/bin/true"]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(125));
                Assert.That(output.ToString(), Does.Not.Contain(
                    "SharpProof.Cleanup/1"));
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(7)));
            }
        }
        finally
        {
            VerifierProcessSupervisor.StopDescendantsOverrideForTest = null;
            Console.SetIn(originalInput);
            Console.SetOut(originalOutput);
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
        const string nonce =
            "0123456789abcdef0123456789abcdef" +
            "0123456789abcdef0123456789abcdef";
        var input = "SharpProof.Armed/1 " + nonce + "\n" +
            new string(
                'x',
                RunVerifier.MaximumCapturedOutputCharacters + 1) +
            "\n\nSharpProof.Cleanup/1 " + nonce + "\n";
        using var signal = new ManualResetEventSlim();

        var result = await RunVerifier.ReadBoundedOutputAsync(
            new StringReader(input),
            nonce,
            signal.Set);

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
        VerifierOutputDrainHidesAuthenticatedControlRecordsAndSeparator()
    {
        const string nonce =
            "0123456789abcdef0123456789abcdef" +
            "0123456789abcdef0123456789abcdef";
        var input = "ordinary\n" +
            "SharpProof.Armed/1 " + nonce + "\n" +
            "child output\n\n" +
            "SharpProof.Cleanup/1 " + nonce + "\n" +
            "unterminated";

        var result = await RunVerifier.ReadBoundedOutputAsync(
            new ChunkedTextReader(input, 3),
            nonce,
            static () => { });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Text, Is.EqualTo(
                "ordinary\nchild output\nunterminated"));
            Assert.That(result.SupervisorArmed, Is.True);
            Assert.That(result.CleanupAuthenticated, Is.True);
            Assert.That(result.LimitExceeded, Is.False);
        }
    }

    [Test]
    public async System.Threading.Tasks.Task
        VerifierArmedStateIsPublishedIndependentlyOfOutputCompletion()
    {
        const string nonce =
            "0123456789abcdef0123456789abcdef" +
            "0123456789abcdef0123456789abcdef";
        using var signal = new ManualResetEventSlim();
        var armed = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions
                .RunContinuationsAsynchronously);
        using var reader = new GatedTextReader(
            "SharpProof.Armed/1 " + nonce + "\n");

        var read = RunVerifier.ReadBoundedOutputAsync(
            reader,
            nonce,
            signal.Set,
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
        const string nonce =
            "0123456789abcdef0123456789abcdef" +
            "0123456789abcdef0123456789abcdef";
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
            signal.Set,
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
                    interrupted: true,
                    outputCompleted: false),
                Is.True);
            Assert.That(
                RunVerifier.ShouldDeferSupervisorAuthentication(
                    authenticationRequired: true,
                    interrupted: false,
                    outputCompleted: false),
                Is.True);
            Assert.That(
                RunVerifier.ShouldDeferSupervisorAuthentication(
                    authenticationRequired: true,
                    interrupted: true,
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

    [TestCase(0)]
    [TestCase(1)]
    public void OutputDrainDeadlinePublishesTimeoutBeforeReturning(
        int timeoutMilliseconds)
    {
        var timeoutPublished = false;
        var incomplete = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var completed = RunVerifier.WaitForOutputCompletion(
            incomplete.Task,
            timeoutMilliseconds,
            static () => false,
            static _ => false,
            () => timeoutPublished = true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completed, Is.False);
            Assert.That(timeoutPublished, Is.True);
        }
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
    public void SupervisorExitAndCleanupAuthenticationRemainSeparate()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                RunVerifier.SupervisorExitCompletesTermination(
                    RunVerifier.SupervisorReadiness.Armed,
                    125),
                Is.True);
            Assert.That(
                RunVerifier.SupervisorExitCompletesTermination(
                    RunVerifier.SupervisorReadiness.Armed,
                    1),
                Is.True);
            Assert.That(
                RunVerifier.SupervisorExitCompletesTermination(
                    RunVerifier.SupervisorReadiness.ExitedBeforeArmed,
                    125),
                Is.True);
            Assert.That(
                RunVerifier.SupervisorExitCompletesTermination(
                    RunVerifier.SupervisorReadiness.ExitedBeforeArmed,
                    1),
                Is.False);
            Assert.That(
                RunVerifier.SupervisorExitCompletesTermination(
                    RunVerifier.SupervisorReadiness.NotReady,
                    125),
                Is.False);
        }
    }

    [TestCase(false, true, false, false)]
    [TestCase(true, false, true, false)]
    [TestCase(true, false, false, true)]
    [TestCase(true, true, true, true)]
    [TestCase(true, true, false, true)]
    public void FailureRetainsArmedOrIncompleteCleanupBoundary(
        bool processStarted,
        bool supervisorArmed,
        bool containmentSucceeded,
        bool expected)
    {
        Assert.That(
            RunVerifier.ShouldRetainCleanupAfterFailure(
                processStarted,
                supervisorArmed,
                containmentSucceeded),
            Is.EqualTo(expected));
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public async System.Threading.Tasks.Task
        RetainedCleanupAnchorRejectsMissingEventualReceipt()
    {
        const string nonce =
            "0123456789abcdef0123456789abcdef" +
            "0123456789abcdef0123456789abcdef";
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
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-receipt-framing-");
        try
        {
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "System.Console.Out.Write(\"partial\");");
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = 2000,
                TerminationGraceMilliseconds = 1
            };

            Assert.That(task.Execute(), Is.True);
            Assert.That(task.ExitCode, Is.EqualTo(0));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void OversizedVerifierOutputTriggersPromptBoundedContainment()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-output-limit-");
        try
        {
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "System.Console.Out.Write(new string('x', " +
                (RunVerifier.MaximumCapturedOutputCharacters + 1)
                    .ToString(CultureInfo.InvariantCulture) +
                ")); System.Threading.Thread.Sleep(5000);");
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = 5000,
                TerminationGraceMilliseconds = 1000
            };
            var stopwatch = Stopwatch.StartNew();

            Assert.That(task.Execute(), Is.True);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(124));
                Assert.That(
                    ((RecordingBuildEngine)task.BuildEngine).Errors
                        .Select(static error => error.Code),
                    Is.All.EqualTo(VerifierBuildDiagnosticCodes.ExecutionFailure));
                Assert.That(
                    stopwatch.Elapsed,
                    Is.LessThan(TimeSpan.FromSeconds(3)));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void OversizedOutputWithIncompleteCleanupReturnsPromptly()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-output-limit-retained-");
        try
        {
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "System.Console.Out.Write(new string('x', " +
                (RunVerifier.MaximumCapturedOutputCharacters + 1)
                    .ToString(CultureInfo.InvariantCulture) +
                ")); System.Threading.Thread.Sleep(1500);");
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = 5000,
                TerminationGraceMilliseconds = 1,
                TryTerminateOverride = static (_, _, _) => false
            };
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
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void SequentialDisposePreservesRetainedOutputReader()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-output-limit-dispose-");
        try
        {
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "System.Console.Out.Write(new string('x', " +
                (RunVerifier.MaximumCapturedOutputCharacters + 1)
                    .ToString(CultureInfo.InvariantCulture) +
                ")); System.Threading.Thread.Sleep(1500);");
            var containmentFailure =
                new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            using (var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = 5000,
                TerminationGraceMilliseconds = 1,
                TryTerminateOverride = static (_, _, _) => true,
                ContainmentAuthenticationFailureOverride = message =>
                    containmentFailure.TrySetResult(message)
            })
            {
                Assert.That(task.Execute(), Is.True);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(task.ExitCode, Is.EqualTo(124));
                    Assert.That(
                        RunVerifier.RetainedCleanupAnchorCount,
                        Is.GreaterThan(0));
                }
            }

            Assert.That(
                SpinWait.SpinUntil(
                    () => RunVerifier.RetainedCleanupAnchorCount == 0,
                    TimeSpan.FromSeconds(3)),
                Is.True);
            Assert.That(containmentFailure.Task.IsCompleted, Is.False);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestCase("missing")]
    [TestCase("malformed")]
    [TestCase("stale-request")]
    [Platform("Linux")]
    public void PublishedResultValidatorRejectsAbsentOrStaleEvidence(string kind)
    {
        var directory = Directory.CreateTempSubdirectory("sharpproof-result-binding-");
        try
        {
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

            var engine = new RecordingBuildEngine();
            var task = new ValidatePublishedVerificationResult
            {
                BuildEngine = engine,
                RequestPath = request,
                ResultPath = result,
                ManifestPath = manifest
            };

            Assert.That(task.Execute(), Is.False);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(engine.Errors, Is.Not.Empty);
                Assert.That(
                    engine.Errors.Select(static error => error.Code),
                    Is.All.EqualTo(VerifierBuildDiagnosticCodes.PublishedEvidence));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public void PublishedProtocolFilesAreSizeBoundedBeforeDeserialization()
    {
        var directory = Directory.CreateTempSubdirectory("sharpproof-result-limit-");
        try
        {
            var request = Path.Combine(directory.FullName, "request.json");
            var result = Path.Combine(directory.FullName, "result.json");
            var manifest = Path.Combine(directory.FullName, "compiler-manifest.json");
            File.WriteAllText(manifest, "{}");
            using (var stream = File.Create(request))
            {
                stream.SetLength(WorkerProtocolJson.MaximumJsonBytes + 1L);
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
            Assert.That(
                engine.Errors.Select(static error => error.Message),
                Has.Some.Contains("exceeds the " +
                    WorkerProtocolJson.MaximumJsonBytes.ToString(
                        CultureInfo.InvariantCulture) + " byte limit"));
            Assert.That(
                engine.Errors.Select(static error => error.Code),
                Is.All.EqualTo(VerifierBuildDiagnosticCodes.PublishedEvidence));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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

        Assert.Multiple((Action)(() =>
        {
            Assert.That(task, Is.InstanceOf<ICancelableTask>());
            Assert.That(task.Execute(), Is.True);
            Assert.That(task.ExitCode, Is.EqualTo(-1));
            Assert.That(engine.Errors, Is.Empty);
        }));
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

        Assert.Multiple((Action)(() =>
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
        }));
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

        Assert.Multiple((Action)(() =>
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
        }));
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

        Assert.Multiple((Action)(() =>
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
        }));
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void DotNetHostValidationRejectsUntrustedForms()
    {
        var originalHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var directory = Directory.CreateTempSubdirectory("sharpproof-dotnet-host-");
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
            if (!string.Equals(
                    Path.GetFileName(Environment.ProcessPath),
                    "dotnet",
                    StringComparison.Ordinal))
            {
                Assert.That(
                    Assert.Throws<InvalidOperationException>(
                        (Action)(() => RunVerifier.ResolveDotNetHost("dotnet")))!.Message,
                    Does.Contain("resolve a trusted dotnet muxer"));
            }
            else
            {
                Assert.DoesNotThrow(
                    (Action)(() => RunVerifier.ResolveDotNetHost("dotnet")));
            }

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
            directory.Delete(recursive: true);
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
    public void VerifierTaskBoundsTheWholeLauncherProcess()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-launcher-timeout-");
        try
        {
            var helper = CreateTimedProcessAssembly(directory.FullName);
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                // Let the instrumented supervisor and child finish managed
                // startup before exercising the whole-process deadline.
                ProjectWallTimeMilliseconds = 2000,
                TerminationGraceMilliseconds = 50
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Assert.That(task.Execute(), Is.True);
            stopwatch.Stop();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(124));
                Assert.That(
                    stopwatch.Elapsed,
                    Is.LessThan(TimeSpan.FromSeconds(4)));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierTaskRejectsOverflowingTimeoutBeforeLaunch()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-launcher-overflow-");
        try
        {
            var marker = Path.Combine(directory.FullName, "started.txt");
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "System.IO.File.WriteAllText(\"started.txt\", \"started\"); " +
                "System.Threading.Thread.Sleep(3000);");
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = int.MaxValue,
                TerminationGraceMilliseconds = 1
            };

            Assert.That(task.Execute(), Is.True);
            Thread.Sleep(250);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(-1));
                Assert.That(File.Exists(marker), Is.False);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierTaskUsesOneDeadlineAndStopsOutputHoldingDescendants()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-launcher-descendant-");
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
                "Thread.Sleep(800); System.Environment.Exit(7);");
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                // Let the instrumented supervisor and child finish managed
                // startup before asserting descendant cleanup behavior.
                ProjectWallTimeMilliseconds = 2000,
                TerminationGraceMilliseconds = 50
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Assert.That(task.Execute(), Is.True);
            stopwatch.Stop();
            Assert.That(File.Exists(pidPath), Is.True);
            descendantId = int.Parse(
                File.ReadAllText(pidPath),
                CultureInfo.InvariantCulture);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(7));
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
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierSupervisorStopsSessionEscapingDescendants()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-launcher-daemon-");
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
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = 1000,
                TerminationGraceMilliseconds = 1
            };

            Assert.That(task.Execute(), Is.True);
            Assert.That(File.Exists(pidPath), Is.True);
            descendantId = int.Parse(
                File.ReadAllText(pidPath),
                CultureInfo.InvariantCulture);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.Zero);
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
            directory.Delete(recursive: true);
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
    public void SupervisorCleanupDoesNotReapTheManagedDirectChild()
    {
        using var direct = Process.Start("/bin/sleep", "1");
        Assert.That(direct, Is.Not.Null);
        try
        {
            var cleanup = VerifierProcessSupervisor.StopDescendants(
                Environment.ProcessId,
                50,
                protectedProcessId: direct!.Id);

            Assert.That(cleanup.HadDescendants, Is.False);
            Assert.That(cleanup.Complete, Is.True);
            Assert.That(direct.WaitForExit(1000), Is.True);
            Assert.That(direct.ExitCode, Is.Zero);
        }
        finally
        {
            if (direct is { HasExited: false })
            {
                direct.Kill();
                direct.WaitForExit();
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
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-retained-cleanup-");
        try
        {
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "using System.Threading; Thread.Sleep(1500);");
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = 10,
                TerminationGraceMilliseconds = 1,
                TryTerminateOverride = static (_, _, _) => false
            };

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
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public async System.Threading.Tasks.Task CancellationInterruptsForegroundWait()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-cancel-wait-");
        try
        {
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "using System.Threading; Thread.Sleep(1500);");
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = 300000,
                TerminationGraceMilliseconds = 1,
                TryTerminateOverride = static (_, _, _) => false
            };
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
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public async System.Threading.Tasks.Task
        LaterCancellationDoesNotEraseEarlierTimeoutCleanupFailure()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-timeout-cancel-order-");
        using var timeoutObserved = new ManualResetEventSlim();
        using var resumeTermination = new ManualResetEventSlim();
        var supervisorId = 0;
        try
        {
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "using System.Threading; Thread.Sleep(10000);");
            var terminationCalls = 0;
            var containmentFailure =
                new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = 10,
                TerminationGraceMilliseconds = 1,
                TryTerminateOverride = (process, _, _) =>
                {
                    if (Interlocked.Increment(ref terminationCalls) == 1)
                    {
                        supervisorId = process?.Id ?? 0;
                        timeoutObserved.Set();
                        Assert.That(
                            resumeTermination.Wait(TimeSpan.FromSeconds(2)),
                            Is.True);
                    }
                    return true;
                },
                ContainmentAuthenticationFailureOverride = message =>
                    containmentFailure.TrySetResult(message)
            };

            var execution = System.Threading.Tasks.Task.Run(task.Execute);
            Assert.That(
                timeoutObserved.Wait(TimeSpan.FromSeconds(2)),
                Is.True,
                "The wall timeout was not observed.");
            Assert.That(supervisorId, Is.GreaterThan(0));
            await System.Threading.Tasks.Task.Run(task.Cancel)
                .WaitAsync(TimeSpan.FromSeconds(2));
            resumeTermination.Set();

            Assert.That(
                await execution.WaitAsync(TimeSpan.FromSeconds(2)),
                Is.True);
            using (var supervisor = Process.GetProcessById(supervisorId))
            {
                supervisor.Kill(entireProcessTree: true);
                await supervisor.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(2));
                Assert.That(supervisor.HasExited, Is.True);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(124));
                Assert.That(
                    await containmentFailure.Task.WaitAsync(
                        TimeSpan.FromSeconds(2)),
                    Does.Contain("cleanup receipt"));
            }
            Assert.That(
                SpinWait.SpinUntil(
                    () => RunVerifier.RetainedCleanupAnchorCount == 0,
                    TimeSpan.FromSeconds(2)),
                Is.True);
        }
        finally
        {
            resumeTermination.Set();
            if (supervisorId > 0 && IsProcessRunning(supervisorId))
            {
                using var supervisor = Process.GetProcessById(supervisorId);
                supervisor.Kill(entireProcessTree: true);
                await supervisor.WaitForExitAsync();
            }
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public async System.Threading.Tasks.Task
        PostArmFaultRetainsAuthenticationAfterLaterCancellation()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-fault-cleanup-order-");
        using var faultTerminationObserved = new ManualResetEventSlim();
        using var resumeTermination = new ManualResetEventSlim();
        var supervisorId = 0;
        try
        {
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "using System.Threading; Thread.Sleep(10000);");
            var terminationCalls = 0;
            var containmentFailure =
                new TaskCompletionSource<string>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = 300000,
                TerminationGraceMilliseconds = 1,
                ArmedExecutionOverride = static () =>
                    throw new InvalidOperationException(
                        "forced post-arm execution fault"),
                TryTerminateOverride = (process, _, _) =>
                {
                    if (Interlocked.Increment(ref terminationCalls) == 1)
                    {
                        supervisorId = process?.Id ?? 0;
                        faultTerminationObserved.Set();
                        Assert.That(
                            resumeTermination.Wait(TimeSpan.FromSeconds(2)),
                            Is.True);
                    }
                    return true;
                },
                ContainmentAuthenticationFailureOverride = message =>
                    containmentFailure.TrySetResult(message)
            };

            var execution = System.Threading.Tasks.Task.Run(task.Execute);
            Assert.That(
                faultTerminationObserved.Wait(TimeSpan.FromSeconds(2)),
                Is.True,
                "The post-arm execution fault was not observed.");
            Assert.That(supervisorId, Is.GreaterThan(0));
            await System.Threading.Tasks.Task.Run(task.Cancel)
                .WaitAsync(TimeSpan.FromSeconds(2));
            resumeTermination.Set();

            Assert.That(
                await execution.WaitAsync(TimeSpan.FromSeconds(2)),
                Is.True);
            Assert.That(
                RunVerifier.RetainedCleanupAnchorCount,
                Is.GreaterThan(0));
            using (var supervisor = Process.GetProcessById(supervisorId))
            {
                supervisor.Kill(entireProcessTree: true);
                await supervisor.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(2));
                Assert.That(supervisor.HasExited, Is.True);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(-1));
                Assert.That(
                    await containmentFailure.Task.WaitAsync(
                        TimeSpan.FromSeconds(2)),
                    Does.Contain("cleanup receipt"));
            }
            Assert.That(
                SpinWait.SpinUntil(
                    () => RunVerifier.RetainedCleanupAnchorCount == 0,
                    TimeSpan.FromSeconds(2)),
                Is.True);
        }
        finally
        {
            resumeTermination.Set();
            if (supervisorId > 0 && IsProcessRunning(supervisorId))
            {
                using var supervisor = Process.GetProcessById(supervisorId);
                supervisor.Kill(entireProcessTree: true);
                await supervisor.WaitForExitAsync();
            }
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void SupervisorContainsVerifierThatKillsItsImmediateParent()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-supervisor-anchor-");
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
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                ProjectWallTimeMilliseconds = 2000,
                TerminationGraceMilliseconds = 1
            };

            Assert.That(task.Execute(), Is.True);
            Assert.That(File.Exists(pidPath), Is.True);
            descendantId = int.Parse(
                File.ReadAllText(pidPath),
                CultureInfo.InvariantCulture);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    task.ExitCode,
                    Is.EqualTo(137),
                    "A verifier killed by SIGKILL must retain its direct exit code " +
                    "after descendant cleanup.");
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
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    [NonParallelizable]
    public void VerifierTaskDoesNotReleaseCommandBeforePidFdAcquisition()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-launcher-gate-");
        try
        {
            var marker = Path.Combine(directory.FullName, "started.txt");
            var helper = CreateTimedProcessAssembly(
                directory.FullName,
                "using System.IO; File.WriteAllText(\"started.txt\", \"started\");");
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable(
                    "DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments = [new TaskItem(helper)],
                OpenPidFdOverride = static _ =>
                    throw new InvalidOperationException("forced pidfd failure")
            };

            Assert.That(task.Execute(), Is.True);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(-1));
                Assert.That(File.Exists(marker), Is.False);
                Assert.That(task.HasActiveProcess, Is.False);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
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
        var directory = Directory.CreateTempSubdirectory("sharpproof-cancel-");
        try
        {
            var helper = CreateTimedProcessAssembly(directory.FullName);
            var containmentFailure = string.Empty;
            using var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments =
                [
                    new TaskItem(helper)
                ],
                ContainmentAuthenticationFailureOverride = message =>
                    containmentFailure = message
            };

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
                Assert.That(task.ExitCode, Is.EqualTo(143));
                Assert.That(containmentFailure, Is.Empty);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
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
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-topology-");
        try
        {
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
                Assert.That(
                    engine.Errors.Select(static error => error.Code),
                    Is.All.EqualTo(VerifierBuildDiagnosticCodes.PublicationTopology));
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
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public void InvalidationDeletesOnlyThePublishedOutputs()
    {
        var directory = Directory.CreateTempSubdirectory("sharpproof-task-");
        try
        {
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

            Assert.Multiple((Action)(() =>
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
            }));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public void EveryPublicationMemberRejectsEveryCompilerOwnedOutput()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-compiler-output-");
        try
        {
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
                             "request", "result", "manifest", "sarif"
                         })
                {
                    var root = Directory.CreateDirectory(Path.Combine(
                        directory.FullName,
                        Guid.NewGuid().ToString("N")));
                    var request = Path.Combine(root.FullName, "request.json");
                    var result = Path.Combine(root.FullName, "result.json");
                    var manifest = Path.Combine(root.FullName, "manifest.json");
                    var sarif = Path.Combine(root.FullName, "result.sarif");
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
                    }
                    var task = new InvalidatePublishedResult
                    {
                        BuildEngine = new RecordingBuildEngine(),
                        ResultPath = result,
                        RequestPath = request,
                        ManifestPath = manifest,
                        SarifPath = sarif,
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
                                 request, result, manifest, sarif
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
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public void CacheRejectsCompilerOwnedOutputOverlap()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-cache-compiler-output-");
        try
        {
            var tools = Directory.CreateDirectory(
                Path.Combine(directory.FullName, "tools"));
            var worker = Path.Combine(tools.FullName, "worker.dll");
            var launcher = Path.Combine(tools.FullName, "launcher.dll");
            var protocol = Path.Combine(tools.FullName, "protocol.dll");
            foreach (var path in new[] { worker, launcher, protocol })
            {
                File.WriteAllText(path, Path.GetFileName(path));
            }

            var compilerDirectory = Directory.CreateDirectory(
                Path.Combine(directory.FullName, "obj"));
            var compilerOutput = Path.Combine(
                compilerDirectory.FullName,
                "Consumer.dll");
            File.WriteAllText(compilerOutput, "compiler output");
            var publication = Directory.CreateDirectory(
                Path.Combine(directory.FullName, "publication"));
            var result = Path.Combine(publication.FullName, "result.json");
            var request = Path.Combine(publication.FullName, "request.json");
            var manifest = Path.Combine(publication.FullName, "manifest.json");
            var engine = new RecordingBuildEngine();
            var task = new InvalidatePublishedResult
            {
                BuildEngine = engine,
                ProjectDirectory = directory.FullName,
                ResultPath = result,
                RequestPath = request,
                ManifestPath = manifest,
                WorkerPath = worker,
                LauncherPath = launcher,
                WorkerProtocolPath = protocol,
                CachePath = compilerDirectory.FullName,
                CompilerOutputPaths = [new TaskItem(compilerOutput)]
            };

            Assert.That(task.Execute(), Is.False);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(engine.Errors.Select(static error => error.Message), Has.Some.Contain(
                    "SharpProof output, input, cache, and worker paths must be distinct."));
                Assert.That(File.Exists(compilerOutput), Is.True);
                Assert.That(File.Exists(result), Is.False);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public async System.Threading.Tasks.Task PublicationResetRemovesOnlyCompleteOwnedSet()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-publication-reset-");
        try
        {
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
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public void PublicationResetUsesThePersistedTopologyForTransientSarif()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-publication-reset-topology-");
        try
        {
            var request = Path.Combine(directory.FullName, "request.json");
            var result = Path.Combine(directory.FullName, "result.json");
            var manifest = Path.Combine(directory.FullName, "manifest.json");
            var sarif = Path.Combine(directory.FullName, "custom.sarif");
            var metadata = Path.Combine(
                directory.FullName,
                "obj",
                "SharpProof",
                "publication-topology.json");
            var publication = new[] { request, result, manifest, sarif };
            using (LinuxPathIdentity.AcquirePublicationSet(
                       publication,
                       TimeSpan.FromSeconds(5)))
            {
            }
            foreach (var path in publication)
            {
                File.WriteAllText(path, path);
            }

            var persist = new PersistPublishedVerification
            {
                BuildEngine = new RecordingBuildEngine(),
                MetadataPath = metadata,
                ProjectDirectory = directory.FullName,
                RequestPath = request,
                ResultPath = result,
                ManifestPath = manifest,
                SarifPath = sarif
            };
            Assert.That(persist.Execute(), Is.True);

            var reset = new ResetPublishedVerification
            {
                BuildEngine = new RecordingBuildEngine(),
                RequestPath = request,
                ResultPath = result,
                ManifestPath = manifest,
                PublicationTopologyPath = metadata,
                ProjectDirectory = directory.FullName
            };

            Assert.That(reset.Execute(), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(publication.All(path => !File.Exists(path)), Is.True);
                Assert.That(
                    publication.All(path =>
                        !File.Exists(LinuxPathIdentity.PublicationMarkerPath(path))),
                    Is.True);
                Assert.That(File.Exists(metadata), Is.False);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public void PublicationResetRemovesCompilerManifestSource()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-publication-reset-manifest-");
        try
        {
            var request = Path.Combine(directory.FullName, "request.json");
            var result = Path.Combine(directory.FullName, "result.json");
            var manifest = Path.Combine(directory.FullName, "manifest.json");
            var source = Path.Combine(directory.FullName, "compiler-manifest.input.json");
            var publication = new[] { request, result, manifest };
            using (LinuxPathIdentity.AcquirePublicationSet(
                       publication,
                       TimeSpan.FromSeconds(5)))
            {
            }
            foreach (var path in publication)
            {
                File.WriteAllText(path, path);
            }
            File.WriteAllText(source, "derived");

            var reset = new ResetPublishedVerification
            {
                BuildEngine = new RecordingBuildEngine(),
                RequestPath = request,
                ResultPath = result,
                ManifestPath = manifest,
                CompilerManifestSourcePath = source
            };

            Assert.That(reset.Execute(), Is.True);
            Assert.That(File.Exists(source), Is.False);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public void PublicationResetRejectsPartialOwnershipWithoutDeletingMembers()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-publication-reset-partial-");
        try
        {
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
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public void PublicationResetFinishesCleaningAnInterruptedMarkerSequence()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-publication-reset-interrupted-");
        try
        {
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

            File.Delete(set[1]);
            File.Delete(LinuxPathIdentity.PublicationMarkerPath(set[1]));

            Assert.That(
                (Action)(() => LinuxPathIdentity.ResetPublicationSet(
                    set,
                    TimeSpan.FromSeconds(5))),
                Throws.Nothing);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(set.All(path => !File.Exists(path)), Is.True);
                Assert.That(
                    set.All(path => !File.Exists(
                        LinuxPathIdentity.PublicationMarkerPath(path))),
                    Is.True);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public async System.Threading.Tasks.Task InvalidationCancellationInterruptsPublicationLockWait()
    {
        var directory = Directory.CreateTempSubdirectory(
            "sharpproof-invalidation-cancel-");
        try
        {
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
        finally
        {
            directory.Delete(recursive: true);
        }
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

    private sealed class ChunkedTextReader(string initialText, int chunkSize)
        : TextReader
    {
        private int position;

        public override System.Threading.Tasks.Task<int> ReadAsync(
            char[] buffer,
            int index,
            int count)
        {
            if (position >= initialText.Length)
            {
                return System.Threading.Tasks.Task.FromResult(0);
            }

            var copied = Math.Min(
                Math.Min(count, chunkSize),
                initialText.Length - position);
            initialText.CopyTo(position, buffer, index, copied);
            position += copied;
            return System.Threading.Tasks.Task.FromResult(copied);
        }
    }
}
