using System.Reflection;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class CompilerCaptureFreshnessControlTests
{
    private const string PureSource = "using SharpProof.Attributes; public static class Subject { public static int State; [EnforcePure] public static int Target() => 0; }";
    private const string ImpureSource = "using SharpProof.Attributes; public static class Subject { public static int State; [EnforcePure] public static int Target() { State++; return State; } }";

    [TestCase(null, false)]
    [TestCase("invalid", false)]
    [TestCase("0123456789abcdef0123456789abcdef", true)]
    public void CaptureRequestValidationPrecedesPublishedOutputMutation(string? nonce, bool valid)
    {
        using var directory = new TempDirectory("sharpproof-capture-task-");
        var result = Path.Combine(directory.FullName, "result.json");
        var capture = Path.Combine(directory.FullName, "capture.request");
        File.WriteAllText(result, "prior result");
        File.WriteAllText(capture, "prior capture");
        var engine = new BuildTaskTests.RecordingBuildEngine();
        var task = new SharpProof.BuildTasks.InvalidatePublishedResult
        {
            BuildEngine = engine,
            ProjectDirectory = directory.FullName,
            ResultPath = result,
            WorkerPath = "runtime/worker.dll",
            LauncherPath = "runtime/launcher.dll",
            WorkerProtocolPath = "runtime/protocol.dll",
            CompilerCaptureRequestPath = capture,
            CompilerCaptureNonce = nonce,
        };
        Assert.That(task.Execute(), Is.EqualTo(valid));
        Assert.That(File.ReadAllText(capture), Is.EqualTo(valid ? nonce : "prior capture"));
        Assert.That(File.Exists(result), Is.EqualTo(!valid));
        if (!valid)
        {
            Assert.That(File.ReadAllText(result), Is.EqualTo("prior result"));
            Assert.That(engine.Errors, Has.Count.EqualTo(1));
        }
    }

    [TestCase("RunAnalyzers")]
    [TestCase("RunAnalyzersDuringBuild")]
    public async Task FreshBuildRequiresExecutedCollector(string disabledProperty)
    {
        using var consumer = new Consumer(PureSource);
        var build = await consumer.BuildAsync((disabledProperty, "false"));
        Assert.That(build.Code, Is.Not.Zero, build.Output);
        Assert.That(build.Output, Does.Contain("SP0049"));
        Assert.That(File.Exists(consumer.ResultPath), Is.False);
        Assert.That(File.Exists(consumer.StagingPath), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CaptureSidecarRuntimeLinksRejectBeforeAnyMutation(bool completed)
    {
        using var directory = new TempDirectory("sharpproof-capture-link-");
        var runtime = Path.Combine(directory.FullName, "runtime");
        Directory.CreateDirectory(runtime);
        var worker = Path.Combine(runtime, "worker.dll");
        var companion = Path.Combine(runtime, "worker.deps.json");
        File.WriteAllText(worker, "preserved worker");
        File.WriteAllText(companion, "preserved companion");
        var result = Path.Combine(directory.FullName, "result.json");
        var capture = Path.Combine(directory.FullName, "capture.request");
        File.WriteAllText(result, "prior result");
        File.CreateSymbolicLink(completed ? capture + ".completed" : capture, companion);
        var engine = new BuildTaskTests.RecordingBuildEngine();
        var task = new SharpProof.BuildTasks.InvalidatePublishedResult
        {
            BuildEngine = engine,
            ProjectDirectory = directory.FullName,
            ResultPath = result,
            WorkerPath = worker,
            LauncherPath = worker,
            WorkerProtocolPath = companion,
            CompilerCaptureRequestPath = capture,
            CompilerCaptureNonce = "0123456789abcdef0123456789abcdef",
        };
        Assert.That(task.Execute(), Is.False);
        Assert.That(File.ReadAllText(result), Is.EqualTo("prior result"));
        Assert.That(File.ReadAllText(companion), Is.EqualTo("preserved companion"));
        Assert.That(File.ReadAllText(worker), Is.EqualTo("preserved worker"));
        Assert.That(engine.Errors, Has.Count.EqualTo(1));
    }

    [TestCase("RunAnalyzers")]
    [TestCase("RunAnalyzersDuringBuild")]
    public async Task ReenabledCollectorRecoversAfterRejectedChangedBuild(string disabledProperty)
    {
        using var consumer = new Consumer(PureSource);
        var first = await consumer.BuildAsync();
        Assert.That(first.Code, Is.Zero, first.Output);
        Assert.That((await consumer.ResultAsync()).ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        await File.WriteAllTextAsync(consumer.SourcePath, ImpureSource);
        var rejected = await consumer.BuildAsync((disabledProperty, "false"));
        Assert.That(rejected.Code, Is.Not.Zero, rejected.Output);
        Assert.That(rejected.Output, Does.Contain("fresh final compiler manifest"));
        Assert.That(File.Exists(consumer.ResultPath), Is.False);
        Assert.That(File.Exists(consumer.StagingPath), Is.False);

        // Source and the emitted intermediate assembly remain unchanged here.
        // The missing capture must force compilation and collection to resume.
        var recovered = await consumer.BuildAsync();
        Assert.That(recovered.Code, Is.Not.Zero, recovered.Output);
        Assert.That(File.Exists(consumer.StagingPath), Is.True);
        Assert.That((await consumer.ResultAsync()).ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    [Test]
    public async Task UnchangedBuildReusesCaptureAndChangedEnabledBuildCollectsAgain()
    {
        using var consumer = new Consumer(PureSource);
        var first = await consumer.BuildAsync();
        Assert.That(first.Code, Is.Zero, first.Output);
        var firstCompletion = await File.ReadAllTextAsync(consumer.CompletionPath);
        var firstCapture = await File.ReadAllBytesAsync(consumer.StagingPath);

        var unchanged = await consumer.BuildAsync();
        Assert.That(unchanged.Code, Is.Zero, unchanged.Output);
        Assert.That(await File.ReadAllTextAsync(consumer.CompletionPath), Is.EqualTo(firstCompletion));
        Assert.That(await File.ReadAllBytesAsync(consumer.StagingPath), Is.EqualTo(firstCapture));
        Assert.That((await consumer.ResultAsync()).Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));

        await File.WriteAllTextAsync(consumer.SourcePath, PureSource.Replace("=> 0", "=> 1", StringComparison.Ordinal));
        var changed = await consumer.BuildAsync();
        Assert.That(changed.Code, Is.Zero, changed.Output);
        Assert.That(await File.ReadAllTextAsync(consumer.CompletionPath), Is.Not.EqualTo(firstCompletion));
        Assert.That((await consumer.ResultAsync()).ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase("SharpProofVerifyResultFile", "compiler-capture.request")]
    [TestCase("SharpProofCompilerManifestFile", "compiler-capture.request.completed")]
    public async Task PublicationCannotOverwriteCaptureSidecars(string property, string fileName)
    {
        using var consumer = new Consumer(PureSource);
        var first = await consumer.BuildAsync();
        Assert.That(first.Code, Is.Zero, first.Output);
        var sidecar = Path.Combine(Path.GetDirectoryName(consumer.ResultPath)!, fileName);
        var original = await File.ReadAllBytesAsync(sidecar);
        var rejected = await consumer.BuildAsync((property, sidecar));
        Assert.That(rejected.Code, Is.Not.Zero, rejected.Output);
        Assert.That(rejected.Output, Does.Contain("compiler-owned outputs"));
        Assert.That(await File.ReadAllBytesAsync(sidecar), Is.EqualTo(original));
    }

    [Test]
    public async Task FixedPublicationInvocationCannotReuseStaleCaptureAndPreservesCallerHook()
    {
        using var consumer = new Consumer(PureSource);
        var project = await File.ReadAllTextAsync(consumer.ProjectPath);
        project = project.Replace("</Project>", """
              <Target Name="CallerCompilationHook">
                <WriteLinesToFile File="$(MSBuildProjectDirectory)/caller-hook.txt" Lines="called" Overwrite="true" />
              </Target>
            </Project>
            """, StringComparison.Ordinal);
        await File.WriteAllTextAsync(consumer.ProjectPath, project);
        (string Name, string Value)[] properties =
        [
            ("_SharpProofInvocationId", "0123456789abcdef0123456789abcdef"),
            ("_SharpProofCompilerCaptureNonce", "0123456789abcdef0123456789abcdef"),
            ("_SharpProofCompilerCaptureRequestPath", Path.Combine(consumer.Root, "redirected-request")),
            ("TargetsTriggeredByCompilation", "CallerCompilationHook"),
        ];
        var first = await consumer.BuildAsync(properties);
        Assert.That(first.Code, Is.Zero, first.Output);
        Assert.That(File.Exists(Path.Combine(consumer.Root, "caller-hook.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(consumer.Root, "redirected-request")), Is.False);
        await File.WriteAllTextAsync(consumer.SourcePath, ImpureSource);
        var rejected = await consumer.BuildAsync([.. properties, ("RunAnalyzersDuringBuild", "false")]);
        Assert.That(rejected.Code, Is.Not.Zero, rejected.Output);
        Assert.That(rejected.Output, Does.Contain("fresh final compiler manifest"));
        Assert.That(File.Exists(consumer.ResultPath), Is.False);
        Assert.That(File.Exists(consumer.StagingPath), Is.False);
    }

    [Test]
    public async Task RemovedCollectorCannotReusePriorCapture()
    {
        using var consumer = new Consumer(PureSource);
        var first = await consumer.BuildAsync();
        Assert.That(first.Code, Is.Zero, first.Output);
        var project = await File.ReadAllTextAsync(consumer.ProjectPath);
        project = project.Replace("</Project>", """
              <Target Name="RemoveCollectorForFreshnessControl" BeforeTargets="CoreCompile">
                <ItemGroup><Analyzer Remove="$(_SharpProofCompilerCollectorPath)" /></ItemGroup>
              </Target>
            </Project>
            """, StringComparison.Ordinal);
        await File.WriteAllTextAsync(consumer.ProjectPath, project);
        await File.WriteAllTextAsync(consumer.SourcePath, ImpureSource);
        var rejected = await consumer.BuildAsync();
        Assert.That(rejected.Code, Is.Not.Zero, rejected.Output);
        Assert.That(rejected.Output, Does.Contain("fresh final compiler manifest"));
        Assert.That(File.Exists(consumer.ResultPath), Is.False);
        Assert.That(File.Exists(consumer.StagingPath), Is.False);
    }

    private sealed class Consumer : IDisposable
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private readonly Type _type = typeof(WorkerMsBuildIntegrationTests).GetNestedType("ConsumerProject", BindingFlags.NonPublic)!;
        private readonly IDisposable _project;

        internal Consumer(string source)
        {
            _project = (IDisposable)_type.GetMethod("Create", Members)!.Invoke(null, [source, false])!;
        }

        internal string Root => PathProperty("Root");
        internal string ProjectPath => PathProperty("ProjectPath");
        internal string SourcePath => Path.Combine(Root, "Subject.cs");
        internal string ResultPath => PathProperty("ResultPath");
        internal string StagingPath => Path.Combine(Path.GetDirectoryName(ResultPath)!, "compiler-manifest.staging.json");
        internal string CompletionPath => Path.Combine(Path.GetDirectoryName(ResultPath)!, "compiler-capture.request.completed");

        private string PathProperty(string name)
        {
            return (string)_type.GetProperty(name, Members)!.GetValue(_project)!;
        }

        internal async Task<(int Code, string Output)> BuildAsync(params (string Name, string Value)[] properties)
        {
            (string Name, string Value)[] configured = [("SharpProofRunAnalyzersForPackageTests", "true"), .. properties];
            var task = (Task)_type.GetMethod("BuildAsync", Members)!.Invoke(_project, [true, configured])!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            return ((int)result.GetType().GetProperty("ExitCode")!.GetValue(result)!,
                (string)result.GetType().GetProperty("Output")!.GetValue(result)!);
        }

        internal async Task<WorkerVerifyResponse> ResultAsync()
        {
            return WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(ResultPath))!;
        }

        public void Dispose()
        {
            _project.Dispose();
        }
    }
}
