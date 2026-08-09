using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NUnit.Framework;
using SharpProof.BuildTasks;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class BuildTaskTests
{
    [TestCase("plain", "\"plain\"")]
    [TestCase("with space", "\"with space\"")]
    [TestCase("ends\\", "\"ends\\\\\"")]
    [TestCase("a\\\"b", "\"a\\\\\\\"b\"")]
    public void VerifierArgumentsUseWindowsProcessQuoting(
        string argument,
        string expected)
    {
        Assert.That(RunVerifier.QuoteArgument(argument), Is.EqualTo(expected));
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
                Does.Contain("worker stderr"));
        }));
    }

    [Test]
    [Platform("Win")]
    public async System.Threading.Tasks.Task ActiveVerifierTaskCancellationStopsTheProcess()
    {
        var directory = Directory.CreateTempSubdirectory("sharpproof-cancel-");
        try
        {
            var eventName = "Local\\SharpProof.BuildTask.Cancel." +
                Guid.NewGuid().ToString("N");
            using var startEvent = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                eventName);
            var task = new RunVerifier
            {
                BuildEngine = new RecordingBuildEngine(),
                Executable = "dotnet",
                WorkingDirectory = directory.FullName,
                Arguments =
                [
                    new TaskItem(typeof(SharpProofWorker).Assembly.Location),
                    new TaskItem("verify"),
                    new TaskItem("--request"),
                    new TaskItem(Path.Combine(directory.FullName, "request.json")),
                    new TaskItem("--result"),
                    new TaskItem(Path.Combine(directory.FullName, "result.json")),
                    new TaskItem("--start-event"),
                    new TaskItem(eventName)
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
                System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5)));
            if (!ReferenceEquals(completed, execution))
            {
                startEvent.Set();
                await execution.WaitAsync(TimeSpan.FromSeconds(5));
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(completed, Is.SameAs(execution));
                Assert.That(await execution, Is.True);
                Assert.That(task.ExitCode, Is.Not.Zero);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    [Platform("Win")]
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
            using (WindowsPathIdentity.AcquirePublicationSet(
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
    [Platform("Win")]
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
            using (WindowsPathIdentity.AcquirePublicationSet(
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
                using var held = WindowsPathIdentity.AcquirePublicationSet(
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
