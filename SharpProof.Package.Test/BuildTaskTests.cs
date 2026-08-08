using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NUnit.Framework;
using SharpProof.BuildTasks;
using SharpProof.Worker;

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

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public void LogErrorEvent(BuildErrorEventArgs e)
        {
            Errors.Add(e);
        }

        public void LogWarningEvent(BuildWarningEventArgs e) { }

        public void LogMessageEvent(BuildMessageEventArgs e) { }

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
