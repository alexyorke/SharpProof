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

    [TestCase("declared")]
    [TestCase("declared-leaf")]
    [TestCase("launch")]
    [Platform("Linux")]
    public void RuntimePublicationRejectsBeforeRewritingInvocationArguments(string workerIdentity)
    {
        using var directory = new TempDirectory("sharpproof-publication-runtime-");
        var runtime = Path.Combine(directory.FullName, "runtime");
        Directory.CreateDirectory(runtime);
        var worker = Path.Combine(runtime, "worker.dll");
        var companion = Path.Combine(runtime, "worker.deps.json");
        File.WriteAllText(worker, "preserved worker");
        File.WriteAllText(companion, "preserved companion");
        var declared = worker;
        if (workerIdentity == "declared-leaf")
        {
            var alias = Path.Combine(directory.FullName, "alias");
            Directory.CreateDirectory(alias);
            declared = Path.Combine(alias, "worker.dll");
            File.CreateSymbolicLink(declared, worker);
        }
        var arguments = new List<string> { workerIdentity == "launch" ? worker : "verify",
            "--request", Path.Combine(directory.FullName, "request.json"),
            "--result", Path.Combine(directory.FullName, "result.json"),
            "--publish-request", Path.Combine(directory.FullName, "published-request.json"),
            "--publish-result", companion,
            "--publish-compiler-manifest", Path.Combine(directory.FullName, "manifest.json") };
        if (workerIdentity != "launch")
        {
            arguments.AddRange(["--worker", declared]);
        }
        var invocation = arguments.ToArray();
        Action prepare = () => { VerificationPublication.Prepare(invocation, directory.FullName); };
        Assert.That(prepare, Throws.ArgumentException);
        Assert.That(invocation, Is.EqualTo(arguments));
        Assert.That(File.ReadAllText(worker), Is.EqualTo("preserved worker"));
        Assert.That(File.ReadAllText(companion), Is.EqualTo("preserved companion"));
    }

    [Test]
    [Platform("Linux")]
    public void PublicationAliasesThroughDirectoriesRejectBeforeChangingStableFiles()
    {
        using var directory = new TempDirectory("sharpproof-publication-alias-");
        var stable = Path.Combine(directory.FullName, "stable");
        Directory.CreateDirectory(stable);
        var alias = Path.Combine(directory.FullName, "alias");
        Directory.CreateSymbolicLink(alias, stable);
        var request = Path.Combine(stable, "request.json");
        File.WriteAllText(request, "baseline");
        string[] arguments = ["verify", "--request", Path.Combine(directory.FullName, "private-request.json"),
            "--result", Path.Combine(directory.FullName, "private-result.json"), "--publish-request", request,
            "--publish-result", Path.Combine(alias, "request.json"), "--publish-compiler-manifest", Path.Combine(stable, "manifest.json")];
        Action prepare = () => { VerificationPublication.Prepare(arguments, directory.FullName); };
        Assert.That(prepare, Throws.ArgumentException);
        Assert.That(File.ReadAllText(request), Is.EqualTo("baseline"));
        Assert.That(File.Exists(Path.Combine(stable, "manifest.json")), Is.False);
    }

    [TestCase("publish")]
    [TestCase("invalidate")]
    [TestCase("reset")]
    [Platform("Linux")]
    public async System.Threading.Tasks.Task PublicationValidationKeepsCooperatingWritersAndDeletesOutsideTheLease(string competingAction)
    {
        using var directory = new TempDirectory("sharpproof-publication-lease-");
        var stable = Path.Combine(directory.FullName, "stable");
        Directory.CreateDirectory(stable);
        var request = Path.Combine(stable, "request.json");
        var result = Path.Combine(stable, "result.json");
        var manifest = Path.Combine(stable, "manifest.json");
        var sarif = Path.Combine(stable, "result.sarif");
        var first = Prepare("first", 'a');
        var second = Prepare("second", 'b');
        using var promoted = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var competingStarted = new ManualResetEventSlim();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstRun = System.Threading.Tasks.Task.Run(() => first.Publish(deadline.Token, () =>
        {
            promoted.Set();
            Assert.That(release.Wait(TimeSpan.FromSeconds(10)), Is.True);
        }));
        System.Threading.Tasks.Task? competing = null;
        try
        {
            var reachedPromotion = System.Threading.Tasks.Task.Run(() => promoted.Wait(TimeSpan.FromSeconds(10)));
            var ready = await System.Threading.Tasks.Task.WhenAny(firstRun, reachedPromotion);
            if (ready == firstRun)
            {
                await firstRun;
            }
            Assert.That(await reachedPromotion, Is.True);
            competing = System.Threading.Tasks.Task.Run(() =>
            {
                competingStarted.Set();
                if (competingAction == "publish")
                {
                    second.Publish(deadline.Token);
                }
                else if (competingAction == "reset")
                {
                    var task = new ResetPublishedVerification
                    {
                        BuildEngine = new RecordingBuildEngine(),
                        RequestPath = request,
                        ResultPath = result,
                        ManifestPath = manifest,
                        SarifPath = sarif
                    };
                    Assert.That(task.Execute(), Is.True);
                }
                else
                {
                    var task = new InvalidatePublishedResult
                    {
                        BuildEngine = new RecordingBuildEngine(),
                        ProjectDirectory = directory.FullName,
                        RequestPath = request,
                        ResultPath = result,
                        ManifestPath = manifest,
                        SarifPath = sarif,
                        WorkerPath = Path.Combine(directory.FullName, "runtime", "worker.dll"),
                        LauncherPath = Path.Combine(directory.FullName, "runtime", "launcher.dll"),
                        WorkerProtocolPath = Path.Combine(directory.FullName, "runtime", "protocol.dll")
                    };
                    Assert.That(task.Execute(), Is.True);
                }
            });
            Assert.That(competingStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(competing.Wait(100), Is.False, "A competing operation must wait until the first invocation validates.");
            Assert.That(WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(result))!.InputHash, Is.EqualTo(new string('a', 64)));
        }
        finally
        {
            release.Set();
        }
        await firstRun;
        if (competing != null)
        { await competing; }
        if (competingAction == "publish")
        {
            ValidatePublishedVerificationResult.ValidateFiles(request, result, manifest, sarif,
                Path.Combine(directory.FullName, "second", "result.json"));
            Assert.That(WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(result))!.InputHash, Is.EqualTo(new string('b', 64)));
        }
        else
        {
            Assert.That(File.Exists(result), Is.False);
            Assert.That(File.Exists(sarif), Is.False);
            Assert.That(File.Exists(request), Is.EqualTo(competingAction == "invalidate"));
            Assert.That(File.Exists(manifest), Is.EqualTo(competingAction == "invalidate"));
        }

        VerificationPublication Prepare(string name, char inputIdentity)
        {
            return PreparePublication(directory.FullName, name, inputIdentity, [request, result, manifest, sarif], out _);
        }
    }

    private static VerificationPublication PreparePublication(string root, string name, char inputIdentity,
        string[] stablePaths, out string[] arguments)
    {
        var invocation = Path.Combine(root, name);
        Directory.CreateDirectory(invocation);
        arguments = ["verify", "--request", Path.Combine(invocation, "request.json"), "--result", Path.Combine(invocation, "result.json"),
                "--publish-request", stablePaths[0], "--publish-result", stablePaths[1], "--publish-compiler-manifest", stablePaths[2], "--publish-sarif", stablePaths[3]];
        var plan = VerificationPublication.Prepare(arguments, root)!;
        Assert.That(arguments[2], Is.EqualTo(Path.Combine(invocation, "request.json")));
        Assert.That(arguments[4], Is.EqualTo(Path.Combine(invocation, "result.json")));
        var preparedManifest = arguments[10];
        Directory.CreateDirectory(Path.GetDirectoryName(preparedManifest)!);
        File.WriteAllText(preparedManifest, "{}");
        var inputManifest = Path.Combine(invocation, "input-manifest.json");
        File.WriteAllText(inputManifest, "{}");
        var boundRequest = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference
            { Path = inputManifest, Sha256 = WorkerProtocolJson.ComputeFileSha256(inputManifest) }
        };
        var declarations = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(declarations);
        var response = new WorkerVerifyResponse
        {
            InputHash = new string(inputIdentity, 64),
            Manifest = declarations,
            RequestHash = WorkerProtocolJson.ComputeRequestHash(boundRequest),
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Miss,
                Budgets = boundRequest.Budgets,
                Versions = new WorkerVersionSummary { WorkerVersion = "publication-test", ApiSpecVersion = "publication-test" }
            }
        };
        File.WriteAllText(arguments[2], WorkerProtocolJson.SerializeRequest(boundRequest));
        File.WriteAllText(arguments[4], WorkerProtocolJson.SerializeResponse(response));
        boundRequest.CompilerManifest.Path = preparedManifest;
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(boundRequest);
        File.WriteAllText(arguments[6], WorkerProtocolJson.SerializeRequest(boundRequest));
        File.WriteAllText(arguments[8], WorkerProtocolJson.SerializeResponse(response));
        File.WriteAllText(arguments[12], "{\"version\":\"2.1.0\",\"runs\":[{}]}");
        return plan;
    }

    [TestCase("policy")]
    [TestCase("budget")]
    [TestCase("private-hash")]
    [TestCase("private-budget")]
    [Platform("Linux")]
    public void PreparedPublicationCannotSubstituteItsPrivateInvocation(string change)
    {
        using var directory = new TempDirectory("sharpproof-publication-binding-");
        var stableDirectory = Path.Combine(directory.FullName, "stable");
        Directory.CreateDirectory(stableDirectory);
        string[] stable = [Path.Combine(stableDirectory, "request.json"), Path.Combine(stableDirectory, "result.json"),
            Path.Combine(stableDirectory, "manifest.json"), Path.Combine(stableDirectory, "result.sarif")];
        var plan = PreparePublication(directory.FullName, "private", 'a', stable, out var arguments);
        foreach (var path in stable)
        {
            File.WriteAllText(path, "baseline");
        }
        var request = WorkerProtocolJson.DeserializeRequest(File.ReadAllText(arguments[6]))!;
        var response = WorkerProtocolJson.DeserializeResponse(File.ReadAllText(arguments[8]))!;
        var privateResponse = WorkerProtocolJson.DeserializeResponse(File.ReadAllText(arguments[4]))!;
        if (change == "policy")
        {
            request.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
        }
        else if (change == "budget")
        {
            request.Budgets.MethodRlimit++;
        }
        else if (change == "private-hash")
        {
            privateResponse.RequestHash = new string('0', 64);
        }
        else
        {
            response.Summary.Budgets.MethodRlimit++;
            privateResponse.Summary.Budgets.MethodRlimit++;
        }
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
        File.WriteAllText(arguments[6], WorkerProtocolJson.SerializeRequest(request));
        File.WriteAllText(arguments[8], WorkerProtocolJson.SerializeResponse(response));
        File.WriteAllText(arguments[4], WorkerProtocolJson.SerializeResponse(privateResponse));
        Action publish = () => plan.Publish(CancellationToken.None);
        Assert.That(publish, Throws.TypeOf<InvalidDataException>());
        foreach (var path in stable)
        {
            Assert.That(File.ReadAllText(path), Is.EqualTo("baseline"));
        }
    }

    [Test]
    [Platform("Linux")]
    public async System.Threading.Tasks.Task CanceledPublicationWaitDoesNotDisturbItsOwnerOrPublish()
    {
        using var directory = new TempDirectory("sharpproof-publication-cancel-");
        var path = Path.Combine(directory.FullName, "result.json");
        using var held = await PublicationLease.AcquireAsync([path], CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = PublicationLease.AcquireAsync([path], cancellation.Token);
        await cancellation.CancelAsync();
        Func<System.Threading.Tasks.Task> canceled = async () => { using var acquired = await waiting; };
        await Assert.ThatAsync(canceled, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(File.Exists(path), Is.False);
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
    public async System.Threading.Tasks.Task ActiveVerifierCancellationStopsItsProcess()
    {
        using var directory = new TempDirectory("sharpproof-active-cancel-");
        var helper = CreateTimedProcessAssembly(directory.FullName,
            "using System; using System.IO; using System.Threading; File.WriteAllText(\"ready.pid\", Environment.ProcessId.ToString()); Thread.Sleep(Timeout.Infinite);");
        var marker = Path.Combine(directory.FullName, "ready.pid");
        using var task = CreateVerifier(directory, helper, 10_000);
        var execution = System.Threading.Tasks.Task.Run(task.Execute);
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(marker) && deadline.Elapsed < TimeSpan.FromSeconds(5) && !execution.IsCompleted)
            {
                await System.Threading.Tasks.Task.Delay(10);
            }
            Assert.That(File.Exists(marker), Is.True, "The process must start before active cancellation.");
            var processId = int.Parse(await File.ReadAllTextAsync(marker), CultureInfo.InvariantCulture);
            task.Cancel();
            Assert.That(await execution.WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.EqualTo(-1));
                Assert.That(task.HasStructuredError, Is.False);
                Assert.That(IsProcessRunning(processId), Is.False);
            }
        }
        finally
        {
            task.Cancel();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
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
    public void VerifierTaskDoesNotWaitForOutputHoldingDescendants()
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
            var engine = new RecordingBuildEngine();
            using var task = CreateVerifier(directory, helper, 2000, 50, engine);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Assert.That(task.Execute(), Is.True);
            stopwatch.Stop();
            Assert.That(File.Exists(pidPath), Is.True);
            descendantId = int.Parse(
                File.ReadAllText(pidPath),
                CultureInfo.InvariantCulture);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(task.ExitCode, Is.Zero);
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(6)));
                Assert.That(
                    engine.Messages.Select(static message => message.Message),
                    Has.Some.Contains("descendant holding its output"));
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
    public void InvalidationDeletesPublishedOutputsAndPreservesEveryInput()
    {
        using var directory = new TempDirectory("sharpproof-invalidation-");
        Directory.CreateDirectory(Path.Combine(directory.FullName, "runtime"));
        string[] names = ["result.json", "result.sarif", "request.json", "manifest.json",
            "invocation-request.json", "invocation-result.json", "invocation-manifest.json",
            "compiler.dll", "runtime/worker.dll", "runtime/protocol.dll"];
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(directory.FullName, name), name);
        }
        var engine = new RecordingBuildEngine();
        var task = new InvalidatePublishedResult
        {
            BuildEngine = engine,
            ProjectDirectory = directory.FullName,
            ResultPath = "result.json",
            SarifPath = "result.sarif",
            RequestPath = "request.json",
            ManifestPath = "manifest.json",
            InvocationRequestPath = "invocation-request.json",
            InvocationResultPath = "invocation-result.json",
            InvocationManifestPath = "invocation-manifest.json",
            WorkerPath = "runtime/worker.dll",
            LauncherPath = "runtime/worker.dll",
            WorkerProtocolPath = "runtime/protocol.dll",
            CachePath = "cache",
            CompilerOutputPaths = [new TaskItem("compiler.dll")]
        };

        Assert.That(task.Execute(), Is.True);
        Assert.That(task.Execute(), Is.True, "Already absent outputs are harmless.");
        Assert.That(engine.Errors, Is.Empty);
        foreach (var name in names)
        {
            var path = Path.Combine(directory.FullName, name);
            if (name is "result.json" or "result.sarif")
            {
                Assert.That(File.Exists(path), Is.False);
            }
            else
            {
                Assert.That(File.ReadAllText(path), Is.EqualTo(name));
            }
        }
    }

    [TestCase("duplicate", "output paths must be distinct")]
    [TestCase("input", "must not alias input paths")]
    [TestCase("runtime", "must not be inside the worker runtime")]
    [TestCase("cache", "cache, and worker paths must be distinct")]
    [TestCase("compiler", "must not alias compiler-owned outputs")]
    public void InvalidationPreflightsAllCollisionsBeforeDeletingAnyOutput(
        string collision, string expectedError)
    {
        using var directory = new TempDirectory("sharpproof-invalidation-collision-");
        Directory.CreateDirectory(Path.Combine(directory.FullName, "runtime"));
        File.WriteAllText(Path.Combine(directory.FullName, "result.json"), "result");
        File.WriteAllText(Path.Combine(directory.FullName, "result.sarif"), "sarif");
        File.WriteAllText(Path.Combine(directory.FullName, "runtime/worker.dll"), "worker");
        var engine = new RecordingBuildEngine();
        var task = new InvalidatePublishedResult
        {
            BuildEngine = engine,
            ProjectDirectory = directory.FullName,
            ResultPath = "result.json",
            SarifPath = "result.sarif",
            WorkerPath = "runtime/worker.dll",
            LauncherPath = "runtime/worker.dll",
            WorkerProtocolPath = "runtime/protocol.dll"
        };
        switch (collision)
        {
            case "duplicate":
                task.SarifPath = task.ResultPath;
                break;
            case "input":
                task.RequestPath = task.ResultPath;
                break;
            case "runtime":
                task.ManifestPath = "runtime/manifest.json";
                break;
            case "cache":
                task.CachePath = "result.json";
                break;
            case "compiler":
                task.CompilerOutputPaths = [new TaskItem("result.json")];
                break;
        }

        Assert.That(task.Execute(), Is.False);
        Assert.That(engine.Errors.Select(static item => item.Message), Has.Some.Contains(expectedError));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(Path.Combine(directory.FullName, "result.json")), Is.EqualTo("result"));
            Assert.That(File.ReadAllText(Path.Combine(directory.FullName, "result.sarif")), Is.EqualTo("sarif"));
            Assert.That(File.ReadAllText(Path.Combine(directory.FullName, "runtime/worker.dll")), Is.EqualTo("worker"));
        }
    }

    [Test]
    public void ResetRemovesOnlyTheConfiguredPublicationSet()
    {
        using var directory = new TempDirectory("sharpproof-reset-publication-");
        string[] names = ["request.json", "result.json", "manifest.json", "result.sarif", "unrelated.txt"];
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(directory.FullName, name), name);
        }
        var engine = new RecordingBuildEngine();
        var task = new ResetPublishedVerification
        {
            BuildEngine = engine,
            ProjectDirectory = directory.FullName,
            RequestPath = "request.json",
            ResultPath = "result.json",
            ManifestPath = "manifest.json",
            SarifPath = "result.sarif"
        };
        Assert.That(task.Execute(), Is.True);
        Assert.That(task.Execute(), Is.True);
        Assert.That(engine.Errors, Is.Empty);
        Assert.That(File.ReadAllText(Path.Combine(directory.FullName, "unrelated.txt")), Is.EqualTo("unrelated.txt"));
        Assert.That(names.Take(4).Select(name => File.Exists(Path.Combine(directory.FullName, name))), Is.All.False);
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

    internal sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public List<BuildWarningEventArgs> Warnings { get; } = [];
        public List<BuildMessageEventArgs> Messages { get; } = [];
        internal System.Threading.Tasks.TaskCompletionSource WorkerReported { get; } = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

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
            if (e.Message?.Contains("SharpProof summary", StringComparison.Ordinal) == true)
            {
                WorkerReported.TrySetResult();
            }
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
