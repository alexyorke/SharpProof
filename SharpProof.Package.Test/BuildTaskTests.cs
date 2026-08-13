using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
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
            Assert.That(engine.Errors, Is.Not.Empty);
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
        var task = new RunVerifier
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
        var task = new RunVerifier { BuildEngine = engine };

        task.LogStandardError(
            "source.cs(12,3): warning SP0047: incomplete" + Environment.NewLine +
            "SharpProof: warning SP0048: assumptions" + Environment.NewLine +
            "source.cs(x,3): warning SP0047: malformed location" + Environment.NewLine +
            "worker stderr");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                engine.Warnings.Select(static warning => warning.Code),
                Is.EqualTo((string[])["SP0047", "SP0048", "SP0047"]));
            Assert.That(engine.Warnings[0].File, Is.EqualTo("source.cs"));
            Assert.That(engine.Warnings[0].LineNumber, Is.EqualTo(12));
            Assert.That(engine.Warnings[0].ColumnNumber, Is.EqualTo(3));
            Assert.That(engine.Warnings[2].LineNumber, Is.Zero);
            Assert.That(engine.Warnings[2].ColumnNumber, Is.Zero);
            Assert.That(
                engine.Messages.Select(static message => message.Message),
                Does.Contain("worker stderr"));
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
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public void VerifierTaskCapturesDotNetOutputAndErrors()
    {
        var outputEngine = new RecordingBuildEngine();
        var outputTask = new RunVerifier
        {
            BuildEngine = outputEngine,
            Executable = "dotnet",
            WorkingDirectory = TestContext.CurrentContext.WorkDirectory,
            Arguments = [new TaskItem("--info")]
        };
        var errorEngine = new RecordingBuildEngine();
        var errorTask = new RunVerifier
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
    public async System.Threading.Tasks.Task ActiveVerifierTaskCancellationStopsTheProcess()
    {
        var directory = Directory.CreateTempSubdirectory("sharpproof-cancel-");
        try
        {
            var helper = CreateTimedProcessAssembly(directory.FullName);
            var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments =
                [
                    new TaskItem(helper)
                ]
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
                Assert.That(task.ExitCode, Is.Not.Zero);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string CreateTimedProcessAssembly(string directory)
    {
        var assemblyPath = Path.Combine(directory, "TimedProcess.dll");
        var syntaxTree = CSharpSyntaxTree.ParseText(
            "using System.Threading; Thread.Sleep(3000);");
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
}
