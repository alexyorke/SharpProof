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
            protocolVersion = WorkerProtocolVersions.Current,
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
                protocolVersion = WorkerProtocolVersions.Current,
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
                    protocolVersion = WorkerProtocolVersions.Current,
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

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Linux")]
    public void PublishedResultValidatorBindsResultToPrivateInvocation(
        bool publishedMatchesInvocation)
    {
        using var directory = new TempDirectory(
            "sharpproof-result-invocation-binding-");
        var manifestPath = Path.Combine(
            directory.FullName,
            "compiler-manifest.json");
        var requestPath = Path.Combine(directory.FullName, "request.json");
        var resultPath = Path.Combine(directory.FullName, "result.json");
        var invocationResultPath = Path.Combine(
            directory.FullName,
            "invocation-result.json");
        var manifestBytes = "{}"u8.ToArray();
        File.WriteAllBytes(manifestPath, manifestBytes);
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = manifestPath,
                Sha256 = WorkerProtocolJson.ComputeSha256(manifestBytes)
            }
        };
        var responseManifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(responseManifest);

        WorkerVerifyResponse CreateResponse(char inputHashCharacter)
        {
            return new WorkerVerifyResponse
            {
                RequestHash = WorkerProtocolJson.ComputeRequestHash(request),
                InputHash = new string(inputHashCharacter, 64),
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
        }

        var invocationResponse = CreateResponse('a');
        var publishedResponse = CreateResponse(
            publishedMatchesInvocation ? 'a' : 'b');
        Assert.That(
            WorkerProtocolJson.Validate(request).IsValid,
            Is.True);
        Assert.That(
            WorkerProtocolJson.Validate(invocationResponse).IsValid,
            Is.True);
        Assert.That(
            WorkerProtocolJson.Validate(publishedResponse).IsValid,
            Is.True);
        File.WriteAllText(
            requestPath,
            WorkerProtocolJson.SerializeRequest(request));
        File.WriteAllText(
            invocationResultPath,
            WorkerProtocolJson.SerializeResponse(invocationResponse));
        File.WriteAllText(
            resultPath,
            WorkerProtocolJson.SerializeResponse(publishedResponse));

        var engine = new RecordingBuildEngine();
        var task = new ValidatePublishedVerificationResult
        {
            BuildEngine = engine,
            ProjectDirectory = directory.FullName,
            RequestPath = requestPath,
            ResultPath = resultPath,
            ManifestPath = manifestPath,
            InvocationResultPath = invocationResultPath
        };

        Assert.That(task.Execute(), Is.EqualTo(publishedMatchesInvocation));
        Assert.That(
            engine.Errors,
            publishedMatchesInvocation ? Is.Empty : Is.Not.Empty);
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

    [TestCase(5, true)]
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
    public void CanceledInvalidationDoesNotMutate()
    {
        var task = new InvalidatePublishedResult
        {
            BuildEngine = new RecordingBuildEngine()
        };

        task.Cancel();

        Assert.That(task.Execute(), Is.False);
    }

    private static string CreateTimedProcessAssembly(
        string directory,
        string source = "using System.Threading; Thread.Sleep(3000);")
    {
        var assemblyPath = Path.Combine(directory, "TimedProcess.dll");
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = TestMetadataReferences.Platform;
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
}
