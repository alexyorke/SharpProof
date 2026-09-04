using System.Text.Json;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SharpProof.BuildTasks;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Worker;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class LauncherArgumentTests
{
    private const string SarifProjectDirectory = "/source";
    private const string ValidInputHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static void RequireLinuxX64()
    {
        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Assert.Ignore("The verifier process boundary is supported on Linux x64.");
        }
    }

    [Test]
    [NonParallelizable]
    public void LinuxWorkerReceivesTheExactStartupRelease()
    {
        RequireLinuxX64();

        using var process = LinuxWorkerProcess.Start(
            "/bin/sh",
            ["-c", "read line; test \"$line\" = \"SharpProof.Start/1\""],
            TestContext.CurrentContext.WorkDirectory);
        var completion = process.WaitForExit(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(6));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completion.Kind, Is.EqualTo(LinuxWorkerCompletionKind.Exited));
            Assert.That(completion.ExitCode, Is.Zero);
        }
    }

    [Test]
    public void LinuxWorkerTimeoutTerminatesTheDirectChild()
    {
        RequireLinuxX64();

        using var process = LinuxWorkerProcess.Start(
            "/bin/sh",
            ["-c", "trap '' TERM; while :; do sleep 1; done"],
            TestContext.CurrentContext.WorkDirectory);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var completion = process.WaitForExit(
            TimeSpan.FromMilliseconds(1_000),
            TimeSpan.FromMilliseconds(1_100));
        stopwatch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completion.Kind, Is.EqualTo(LinuxWorkerCompletionKind.TimedOut));
            Assert.That(completion.ExitCode, Is.EqualTo(124));
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromMilliseconds(1_800)),
                "The final deadline must not restart the full 1.1-second cleanup budget.");
        }
    }

    [Test]
    public void LinuxWorkerCooperatesWithTerminationInsideTheSameDeadline()
    {
        RequireLinuxX64();

        using var process = LinuxWorkerProcess.Start(
            "/bin/sh",
            ["-c", "trap 'exit 0' TERM; while :; do sleep 0.05; done"],
            TestContext.CurrentContext.WorkDirectory);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var completion = process.WaitForExit(
            TimeSpan.FromMilliseconds(1_000),
            TimeSpan.FromMilliseconds(1_100));
        stopwatch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completion.Kind, Is.EqualTo(LinuxWorkerCompletionKind.TimedOut));
            Assert.That(completion.ExitCode, Is.EqualTo(124));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(1_300)));
        }
    }

    [Test]
    public void LinuxWorkerCancellationDoesNotWaitForTheDeadline()
    {
        RequireLinuxX64();

        using var process = LinuxWorkerProcess.Start(
            "/bin/sh",
            ["-c", "while :; do sleep 1; done"],
            TestContext.CurrentContext.WorkDirectory);
        using var cancellation = new CancellationTokenSource(100);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.That(
            (Action)(() => process.WaitForExit(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(6),
                cancellation.Token)),
            Throws.TypeOf<OperationCanceledException>());
        stopwatch.Stop();
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void LinuxWorkerDeadlineBoundariesAreExact()
    {
        RequireLinuxX64();

        using var process = LinuxWorkerProcess.Start(
            "/bin/sh",
            ["-c", "read -r _"],
            TestContext.CurrentContext.WorkDirectory);
        Assert.That(
            (Action)(() => process.WaitForExit(
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1))),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            (Action)(() => process.WaitForExit(
                TimeSpan.FromMilliseconds(2),
                TimeSpan.FromMilliseconds(1))),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        var initialCompletion = process.WaitForExit(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        Assert.That(
            initialCompletion.Kind,
            Is.EqualTo(LinuxWorkerCompletionKind.Exited));
        var completion = process.WaitForExit(
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(1));
        Assert.That(completion.Kind, Is.EqualTo(LinuxWorkerCompletionKind.Exited));
    }

    [Test]
    [NonParallelizable]
    public void LinuxWorkerMinimumGraceDoesNotRestartCleanupBudget()
    {
        RequireLinuxX64();

        var process = LinuxWorkerProcess.Start(
            "/bin/sh",
            ["-c", "trap '' TERM; while :; do sleep 1; done"],
            TestContext.CurrentContext.WorkDirectory);
        Exception? failure = null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            try
            {
                _ = process.WaitForExit(
                    TimeSpan.FromMilliseconds(1),
                    TimeSpan.FromMilliseconds(1));
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }
        }
        finally
        {
            process.Dispose();
            stopwatch.Stop();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.Null.Or.TypeOf<InvalidOperationException>());
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromMilliseconds(300)),
                "The minimum grace and disposal must share the original final deadline.");
        }
    }

    [Test]
    public void LinuxWorkerDeadlinePreservesAnExitObservedBeforeTermination()
    {
        RequireLinuxX64();

        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/sh",
                UseShellExecute = false,
                ArgumentList = { "-c", "exit 17" }
            })!;
        process.WaitForExit();

        var completion = LinuxWorkerProcess.CompleteAtDeadline(
            process,
            System.Diagnostics.Stopwatch.StartNew(),
            TimeSpan.FromSeconds(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                completion.Kind,
                Is.EqualTo(LinuxWorkerCompletionKind.Exited));
            Assert.That(completion.ExitCode, Is.EqualTo(17));
        }
    }

    [Test]
    public void UnknownOptionIsRejected()
    {
        string[] arguments = [
            .. ValidArguments(),
            "--project-wall-milliseconds",
            "100"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out _),
            Is.False);
    }

    [Test]
    [NonParallelizable]
    public async Task UnsupportedPreflightReturnsControlledContainmentExit()
    {
        var originalError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            var exitCode = await Program.RunMain(
                ValidArguments(),
                static _ => string.Empty,
                validatePreflight: static _ =>
                    throw new PlatformNotSupportedException(
                        "SharpProof containment is unsupported."));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(125));
                Assert.That(
                    error.ToString(),
                    Does.Contain("SharpProof containment is unsupported."));
            }
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Test]
    public void SarifRequiresTheAtomicPublicationTriple()
    {
        string[] arguments = [
            .. ValidArguments(),
            "--publish-sarif", "result.sarif"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out _),
            Is.False);
    }

    [Test]
    [Platform("Linux")]
    public void CompletePublicationAcceptsSarif()
    {
        string[] arguments = [
            .. ValidArguments(),
            "--publish-request", "published-request.json",
            "--publish-result", "published-result.json",
            "--publish-compiler-manifest", "published-manifest.json",
            "--publish-sarif", "result.sarif"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);
        Assert.That(
            parsed.PublishSarifPath,
            Is.EqualTo(Path.GetFullPath("result.sarif")));
    }

    [Test]
    [Platform("Linux")]
    public void ParsedPathsAndTerminationGraceAreNormalized()
    {
        string[] arguments = [
            .. ValidArguments(),
            "--publish-request", "published-request.json",
            "--publish-result", "published-result.json",
            "--publish-compiler-manifest", "published-manifest.json",
            "--publish-sarif", "published-result.sarif",
            "--termination-grace-ms", "321"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.WorkerPath, Is.EqualTo(Path.GetFullPath("worker.dll")));
            Assert.That(parsed.RequestPath, Is.EqualTo(Path.GetFullPath("request.json")));
            Assert.That(parsed.ResultPath, Is.EqualTo(Path.GetFullPath("result.json")));
            Assert.That(
                parsed.CompilerManifestPath,
                Is.EqualTo(Path.GetFullPath("compiler-manifest.json")));
            Assert.That(
                parsed.PublishRequestPath,
                Is.EqualTo(Path.GetFullPath("published-request.json")));
            Assert.That(
                parsed.PublishResultPath,
                Is.EqualTo(Path.GetFullPath("published-result.json")));
            Assert.That(
                parsed.PublishCompilerManifestPath,
                Is.EqualTo(Path.GetFullPath("published-manifest.json")));
            Assert.That(
                parsed.PublishSarifPath,
                Is.EqualTo(Path.GetFullPath("published-result.sarif")));
            Assert.That(parsed.TerminationGraceMilliseconds, Is.EqualTo(321));
        }
    }

    [Test]
    public void MinimalArgumentsProjectEveryRequestDefault()
    {
        Assert.That(
            LauncherArguments.TryParse(ValidArguments(), out var parsed),
            Is.True);
        var compilerManifest = new WorkerFileReference
        {
            Path = "compiler-manifest.json",
            Sha256 = new('a', 64)
        };

        var request = parsed.ProjectRequest(compilerManifest);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.CompilerManifest, Is.SameAs(compilerManifest));
            Assert.That(request.VerifyPolicy, Is.EqualTo(WorkerVerifyPolicy.Advisory));
            Assert.That(request.AssumptionPolicy, Is.EqualTo(WorkerAssumptionPolicy.Allow));
            Assert.That(request.Budgets.QueryRlimit, Is.EqualTo(WorkerBudgets.DefaultQueryRlimit));
            Assert.That(request.Budgets.MethodRlimit, Is.EqualTo(WorkerBudgets.DefaultMethodRlimit));
            Assert.That(request.Budgets.MethodWallTimeMilliseconds,
                Is.EqualTo(WorkerBudgets.DefaultMethodWallTimeMilliseconds));
            Assert.That(request.Budgets.ProjectWallTimeMilliseconds,
                Is.EqualTo(WorkerBudgets.DefaultProjectWallTimeMilliseconds));
            Assert.That(request.Budgets.MaxParallelism, Is.EqualTo(WorkerBudgets.MaximumParallelism));
            Assert.That(request.Budgets.MaximumExpressionDepth,
                Is.EqualTo(WorkerBudgets.DefaultMaximumExpressionDepth));
            Assert.That(request.Cache.Enabled, Is.True);
            Assert.That(request.Cache.Directory, Is.Null);
            Assert.That(request.Cache.MaximumBytes,
                Is.EqualTo(WorkerCacheOptions.DefaultMaximumBytes));
        }
    }

    [Test]
    public void CustomArgumentsProjectEveryRequestValueExactly()
    {
        string[] arguments = [
            .. ValidArguments(),
            "--query-rlimit", "101",
            "--method-rlimit", "102",
            "--method-wall-ms", "103",
            "--project-wall-ms", "104",
            "--max-parallelism", "2",
            "--max-expression-depth", "105",
            "--cache-enabled", "false",
            "--cache-directory", "relative-cache",
            "--cache-maximum-bytes", "107"
        ];
        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);

        var request = parsed.ProjectRequest(new WorkerFileReference());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Budgets.QueryRlimit, Is.EqualTo(101));
            Assert.That(request.Budgets.MethodRlimit, Is.EqualTo(102));
            Assert.That(request.Budgets.MethodWallTimeMilliseconds, Is.EqualTo(103));
            Assert.That(request.Budgets.ProjectWallTimeMilliseconds, Is.EqualTo(104));
            Assert.That(request.Budgets.MaxParallelism, Is.EqualTo(2));
            Assert.That(request.Budgets.MaximumExpressionDepth, Is.EqualTo(105));
            Assert.That(request.Cache.Enabled, Is.False);
            Assert.That(request.Cache.Directory, Is.EqualTo("relative-cache"));
            Assert.That(request.Cache.MaximumBytes, Is.EqualTo(107));
        }
    }

    [Test]
    public void CachePathResolutionUsesTheManifestProjectDirectory()
    {
        var projectDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "cache-project");
        var defaultPath = Path.Combine(
            projectDirectory,
            "obj",
            "SharpProof",
            "cache");
        var relativePath = Path.Combine("nested", "cache");
        var absolutePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "absolute-cache");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                WorkerCachePath.Resolve(null, projectDirectory),
                Is.EqualTo(Path.GetFullPath(defaultPath)));
            Assert.That(
                WorkerCachePath.Resolve(" ", projectDirectory),
                Is.EqualTo(Path.GetFullPath(defaultPath)));
            Assert.That(
                WorkerCachePath.Resolve(relativePath, projectDirectory),
                Is.EqualTo(Path.GetFullPath(
                    Path.Combine(projectDirectory, relativePath))));
            Assert.That(
                WorkerCachePath.Resolve(absolutePath, projectDirectory),
                Is.EqualTo(Path.GetFullPath(absolutePath)));
        }
    }

    [Test]
    [Platform("Linux")]
    public void RequestProjectionRejectsDirectoryResultBeforeManifestRead()
    {
        using var root = new TempDirectory("sharpproof-directory-result-");
        var workerDirectory = Path.Combine(root.FullName, "worker");
        var ioDirectory = Path.Combine(root.FullName, "io");
        var resultDirectory = Path.Combine(ioDirectory, "result.json");
        Directory.CreateDirectory(workerDirectory);
        Directory.CreateDirectory(resultDirectory);

        var arguments = ProjectionArguments(
            worker: Path.Combine(workerDirectory, "worker.dll"),
            request: Path.Combine(ioDirectory, "request.json"),
            result: resultDirectory,
            compilerManifest: Path.Combine(
                ioDirectory,
                "missing-compiler-manifest.json"));
        AssertRequestProjectionRejects(arguments);
    }

    [Test]
    [Platform("Linux")]
    public void RequestProjectionRejectsSymbolicLinkPathBeforeManifestRead()
    {
        var root = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "symbolic-link-path-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        var alias = Path.Combine(root, "alias");
        Directory.CreateDirectory(target);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(alias, target);
            }
            catch (IOException exception)
            {
                Assert.Ignore("The test host cannot create directory links: " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                Assert.Ignore("The test host cannot create directory links: " + exception.Message);
            }
            catch (PlatformNotSupportedException exception)
            {
                Assert.Ignore("The test host does not support directory links: " + exception.Message);
            }

            var arguments = ProjectionArguments(
                worker: Path.Combine(root, "worker.dll"),
                request: Path.Combine(root, "request.json"),
                result: Path.Combine(alias, "result.json"),
                compilerManifest: Path.Combine(root, "missing.json"));
            Assert.That(
                LauncherArguments.TryParse(arguments, out var parsed),
                Is.True);
            Assert.That(
                (Action)(() => parsed.ValidateDistinctPaths(null)),
                Throws.TypeOf<ArgumentException>());
        }
        finally
        {
            if (Directory.Exists(alias))
            {
                Directory.Delete(alias);
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    [Platform("Linux")]
    public void RequestProjectionRejectsCollidingIoPathsBeforeManifestRead()
    {
        var arguments = ProjectionArguments(
            result: Path.Combine(".", "request.json"));
        AssertRequestProjectionRejects(arguments);
    }

    [Test]
    [Platform("Linux")]
    public void RequestProjectionRejectsCachePathCollisionBeforeManifestRead()
    {
        var requestPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "request.json");
        var arguments = ProjectionArguments(
            request: requestPath,
            result: Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "result.json"),
            cacheDirectory: requestPath);
        AssertRequestProjectionRejects(arguments);
    }

    [Test]
    [Platform("Linux")]
    public void DisabledCachePathDoesNotParticipateInIoTopology()
    {
        using var outputRoot = new TempDirectory("sharpproof-disabled-cache-");
        var requestPath = Path.Combine(
            outputRoot.FullName,
            "disabled-cache-request.json");
        var arguments = ProjectionArguments(
            request: requestPath,
            result: Path.Combine(
                outputRoot.FullName,
                "disabled-cache-result.json"),
            compilerManifest: Path.Combine(
                outputRoot.FullName,
                "missing-compiler-manifest.json"),
            cacheDirectory: requestPath,
            cacheEnabled: false);
        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);

        Assert.That(
            (Action)(() => parsed.CreateRequest(out _, out _)),
            Throws.TypeOf<FileNotFoundException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Linux")]
    public void RequestProjectionRejectsNestedCachePathsBeforeManifestRead(
        bool cacheBelowResult)
    {
        var root = TestContext.CurrentContext.WorkDirectory;
        var result = Path.Combine(root, "nested-cache-result.json");
        var cache = cacheBelowResult
            ? Path.Combine(result, "cache")
            : Path.Combine(root, "cache-root");
        if (!cacheBelowResult)
        {
            result = Path.Combine(cache, "result.json");
        }
        var arguments = ProjectionArguments(
            request: Path.Combine(root, "nested-cache-request.json"),
            result: result,
            cacheDirectory: cache,
            cacheEnabled: true);
        AssertRequestProjectionRejects(arguments);
    }

    [Test]
    [Platform("Linux")]
    public void RequestProjectionRejectsWorkerTreeOutputBeforeManifestRead()
    {
        var worker = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "worker-tree-worker.dll");
        var arguments = ProjectionArguments(
            worker: worker,
            request: Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "worker-tree-request.json"),
            result: Path.Combine(
                Path.GetDirectoryName(worker)!,
                "worker-tree-output.json"));
        AssertRequestProjectionRejects(arguments);
    }

    [Test]
    [Platform("Linux")]
    public void RequestProjectionRejectsWorkerPathCollisionBeforeManifestRead()
    {
        var arguments = ProjectionArguments(
            worker: "request.json");
        AssertRequestProjectionRejects(arguments);
    }

    [Test]
    public void MissingWorkerWithoutDllSuffixIsRejectedBeforeHashing()
    {
        var worker = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "missing-worker-" + Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<FileNotFoundException>((Action)(() =>
            Program.ComputeExpectedInputHash(
                worker,
                new WorkerVerifyRequest(),
                [])));
        Assert.That(exception!.Message, Does.Contain("must be a .dll"));
    }

    [TestCase("worker.deps.json")]
    [TestCase("worker.runtimeconfig.json")]
    [Platform("Linux")]
    public void RequestProjectionRejectsWorkerRuntimeCompanionCollisionBeforeManifestRead(
        string resultPath)
    {
        var arguments = ProjectionArguments(result: resultPath);
        AssertRequestProjectionRejects(arguments);
    }

    [Test]
    [Platform("Linux")]
    public void RequestProjectionRejectsLauncherRuntimeCollisionBeforeManifestRead()
    {
        var launcher = LauncherArguments.LauncherRuntimePaths[0];
        Assert.That(
            LauncherArguments.LauncherRuntimePaths
                .Skip(3)
                .Select(Path.GetFileName),
            Is.EqualTo(LauncherRuntimeCompanionInventory.FileNames));
        foreach (var resultPath in LauncherArguments.LauncherRuntimePaths)
        {
            var arguments = ProjectionArguments(
                worker: Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof-isolated-worker-" +
                    Guid.NewGuid().ToString("N"),
                    "worker.dll"),
                result: resultPath);
            Assert.That(
                LauncherArguments.TryParse(arguments, out var parsed),
                Is.True,
                resultPath);
            Assert.That(
                (Action)(() => parsed.ValidateDistinctPaths(null)),
                Throws.TypeOf<ArgumentException>(),
                resultPath);
        }
    }

    [Test]
    [Platform("Linux")]
    public void RequestProjectionRejectsLauncherProtocolRuntimeCollisionBeforeManifestRead()
    {
        var launcher = LauncherArguments.LauncherRuntimePaths[0];
        var protocol = Path.Combine(
            Path.GetDirectoryName(launcher)!,
            "SharpProof.Worker.Protocol.dll");
        var arguments = ProjectionArguments(
            worker: Path.Combine(
                Path.GetTempPath(),
                "SharpProof-isolated-worker-" + Guid.NewGuid().ToString("N"),
                "worker.dll"),
            result: protocol);
        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);
        Assert.That(
            (Action)(() => parsed.ValidateDistinctPaths(null)),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    [Platform("Linux")]
    public void RequestProjectionRejectsDiscoveredRuntimeAssetCollisionBeforeManifestRead()
    {
        var worker = typeof(SharpProofWorker).Assembly.Location;
        var testRoot = Path.GetDirectoryName(Path.GetDirectoryName(worker)!)!;
        var testId = Guid.NewGuid().ToString("N");
        var runtimeAsset = Path.Combine(
            testRoot,
            "SharpProof-discovered-runtime-asset-" + testId + ".bin");
        using var snapshot = new WorkerRuntimeClosureSnapshot(
            worker,
            Path.Combine(
                testRoot,
                "SharpProof-snapshot-" + Guid.NewGuid().ToString("N"),
                Path.GetFileName(worker)),
            [runtimeAsset],
            "snapshot",
            Array.Empty<FileStream>());
        var arguments = ProjectionArguments(
            worker: worker,
            request: Path.Combine(
                testRoot,
                "SharpProof-safe-request-" + testId + ".json"),
            result: runtimeAsset,
            compilerManifest: Path.Combine(
                testRoot,
                "SharpProof-safe-missing-manifest-" + testId + ".json"));
        var nonCollidingArguments = arguments.ToArray();
        nonCollidingArguments[6] = Path.Combine(
            testRoot, "SharpProof-safe-result-" + testId + ".json");
        Assert.That(
            LauncherArguments.TryParse(nonCollidingArguments, out var nonColliding),
            Is.True);
        Assert.That(
            (Action)(() => nonColliding.CreateRequest(snapshot, out _, out _)),
            Throws.TypeOf<FileNotFoundException>());
        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);

        Exception? collision = null;
        try
        {
            parsed.CreateRequest(snapshot, out _, out _);
        }
        catch (ArgumentException exception)
        {
            collision = exception;
        }
        catch (FileNotFoundException exception)
        {
            collision = exception;
        }
        Assert.That(collision?.GetType(), Is.EqualTo(typeof(ArgumentException)));
    }

    [TestCase(0)]
    [TestCase(300_001)]
    public void TerminationGraceIsBoundedBeforeWorkerStarts(int graceMilliseconds)
    {
        string[] arguments = [
            .. ValidArguments(),
            "--termination-grace-ms",
            graceMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ];
        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);

        Assert.That(
            (Action)parsed.ValidatePreflight,
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    [NonParallelizable]
    [Platform("Linux")]
    public async Task MainLeavesRequestAndResultSentinelsWhenManifestIsMalformed()
    {
        var directory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var worker = Path.Combine(directory, "worker.dll");
        var request = Path.Combine(directory, "request.json");
        var result = Path.Combine(directory, "result.json");
        var manifest = Path.Combine(directory, "compiler-manifest.json");
        const string requestSentinel = "request sentinel";
        const string resultSentinel = "result sentinel";
        try
        {
            await File.WriteAllTextAsync(request, requestSentinel);
            await File.WriteAllTextAsync(result, resultSentinel);
            await File.WriteAllTextAsync(manifest, "{ malformed manifest");

            var exitCode = await Program.Main([
                "verify",
                "--worker", worker,
                "--request", request,
                "--result", result,
                "--compiler-manifest", manifest,
                "--verify-policy", "advisory",
                "--assumption-policy", "allow"
            ]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(2));
                Assert.That(await File.ReadAllTextAsync(request), Is.EqualTo(requestSentinel));
                Assert.That(await File.ReadAllTextAsync(result), Is.EqualTo(resultSentinel));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    [NonParallelizable]
    [Platform("Linux")]
    public async Task MainFailsClosedWhenWorkerDependencyManifestIsMalformed()
    {
        var sourceWorker = typeof(SharpProofWorker).Assembly.Location;
        var directory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N"));
        var ioDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(ioDirectory);
        var worker = Path.Combine(directory, "worker.dll");
        try
        {
            File.Copy(sourceWorker, worker);
            File.Copy(
                Path.ChangeExtension(sourceWorker, ".runtimeconfig.json"),
                Path.ChangeExtension(worker, ".runtimeconfig.json"));
            await File.WriteAllTextAsync(
                Path.ChangeExtension(worker, ".deps.json"),
                "{ malformed dependency manifest");

            var escaped = false;
            var exitCode = 0;
            try
            {
                exitCode = await Program.Main([
                    "verify",
                    "--worker", worker,
                    "--request", Path.Combine(ioDirectory, "request.json"),
                    "--result", Path.Combine(ioDirectory, "result.json"),
                    "--compiler-manifest", Path.Combine(ioDirectory, "missing.json"),
                    "--verify-policy", "advisory",
                    "--assumption-policy", "allow"
                ]);
            }
            catch (JsonException)
            {
                escaped = true;
            }
            catch (KeyNotFoundException)
            {
                escaped = true;
            }
            catch (InvalidOperationException)
            {
                escaped = true;
            }

            Assert.That(escaped, Is.False);
            Assert.That(exitCode, Is.EqualTo(2));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
            if (Directory.Exists(ioDirectory))
            {
                Directory.Delete(ioDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void CombinedTimeoutOverflowIsRejectedBeforeStartingWorker()
    {
        Assert.That(
            (Action)(() =>
            {
                var projectMilliseconds = int.MaxValue;
                _ = checked(
                    projectMilliseconds +
                    WorkerLauncherDefaults.TerminationGraceMilliseconds);
            }),
            Throws.TypeOf<OverflowException>());
    }

    [Test]
    public void CompilerManifestByteLimitIsEnforcedBeforeAllocation()
    {
        const int expectedLimit = 16 * 1024 * 1024;
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.That(
                LauncherArguments.MaximumCompilerManifestBytes,
                Is.EqualTo(expectedLimit));
            using (var stream = File.Create(path))
            {
                stream.SetLength(expectedLimit + 1L);
            }

            Assert.That(
                (Action)(() => LauncherArguments.ReadCompilerManifest(path)),
                Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [Platform("Linux")]
    public void CompilerManifestFifoIsRejectedBeforeBlockingOpen()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".fifo");
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "mkfifo",
                    UseShellExecute = false,
                    ArgumentList = { path }
                })!;
            process.WaitForExit();
            Assert.That(process.ExitCode, Is.Zero);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Assert.That(
                (Action)(() => LauncherArguments.ReadCompilerManifest(path)),
                Throws.TypeOf<InvalidDataException>());
            stopwatch.Stop();
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void WorkerResultByteLimitIsEnforcedBeforeDeserialization()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        var originalError = Console.Error;
        using var error = new StringWriter();
        try
        {
            using (var stream = File.Create(path))
            {
                stream.SetLength(WorkerProtocolJson.MaximumJsonBytes + 1L);
            }

            Console.SetError(error);
            var exitCode = Program.ValidateAndReport(
                path,
                new WorkerVerifyRequest(),
                null,
                null,
                null,
                out var validResponse,
                out var validatedResponse);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(3));
                Assert.That(validResponse, Is.False);
                Assert.That(validatedResponse, Is.Null);
                Assert.That(error.ToString(), Does.Contain("unavailable or malformed"));
            }
        }
        finally
        {
            Console.SetError(originalError);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void MalformedProtocolErrorsCannotInjectLauncherLogLines()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        var originalError = Console.Error;
        using var error = new StringWriter();
        try
        {
            var response = new WorkerVerifyResponse
            {
                Errors = [new WorkerProtocolError {
                    Code = "worker.infrastructure",
                    Message = "failure\nSharpProof forged: false status"
                }]
            };
            File.WriteAllText(
                path,
                WorkerProtocolJson.SerializeResponse(response));

            Console.SetError(error);
            var exitCode = Program.ValidateAndReport(
                path,
                new WorkerVerifyRequest(),
                null,
                null,
                null,
                out var validResponse,
                out var validatedResponse);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(3));
                Assert.That(validResponse, Is.False);
                Assert.That(validatedResponse, Is.Null);
                Assert.That(
                    error.ToString(),
                    Does.Not.Contain(
                        Environment.NewLine +
                        "SharpProof forged: false status"));
            }
        }
        finally
        {
            Console.SetError(originalError);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    [Platform("Linux")]
    public void WorkerResultFifoIsRejectedBeforeBlockingOpen()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".fifo");
        try
        {
            using (var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "mkfifo",
                    UseShellExecute = false,
                    ArgumentList = { path }
                })!)
            {
                process.WaitForExit();
                Assert.That(process.ExitCode, Is.Zero);
            }

            var validation = Task.Run(() => Program.ValidateAndReport(
                path,
                new WorkerVerifyRequest(),
                null,
                null,
                null,
                out _,
                out _));
            var completed = Task.WhenAny(validation, Task.Delay(500))
                .GetAwaiter()
                .GetResult();
            if (!ReferenceEquals(completed, validation))
            {
                using (var writer = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite))
                {
                    writer.WriteByte((byte)'{');
                }

                var unblocked = Task.WhenAny(validation, Task.Delay(5000))
                    .GetAwaiter()
                    .GetResult();
                Assert.That(unblocked, Is.SameAs(validation));
                _ = validation.Exception;
            }

            Assert.That(
                completed,
                Is.SameAs(validation),
                "Worker-result validation must not wait for a FIFO writer.");
            Assert.That(validation.GetAwaiter().GetResult(), Is.EqualTo(3));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [Platform("Linux")]
    public void DotNetHostMustBeAbsoluteInstalledAndOutsideProject()
    {
        var project = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N"));
        var fakeRoot = Path.Combine(project, "fake-sdk");
        var fakeHost = Path.Combine(fakeRoot, "dotnet");
        Directory.CreateDirectory(Path.Combine(fakeRoot, "host", "fxr"));
        File.WriteAllBytes(fakeHost, []);
        var actualHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
            throw new InvalidOperationException(
                "The test host did not disclose its dotnet host path.");
        try
        {
            Assert.That(
                Program.ValidateDotNetHostPath(actualHost, project),
                Is.EqualTo(Path.GetFullPath(actualHost)));
            Assert.That(
                (Action)(() => _ = Program.ValidateDotNetHostPath(
                    actualHost,
                    Path.GetPathRoot(actualHost)!)),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                (Action)(() => _ = Program.ValidateDotNetHostPath(
                    "dotnet", project)),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                (Action)(() => _ = Program.ValidateDotNetHostPath(
                    fakeHost, project)),
                Throws.TypeOf<InvalidOperationException>());
        }
        finally
        {
            Directory.Delete(project, recursive: true);
        }
    }

    [TestCase(1_000, 1_000)]
    [TestCase(1_000, 100)]
    [TestCase(1_000, 1)]
    public void FinalTimeoutIncludesCleanupTime(
        int projectMilliseconds, int graceMilliseconds)
    {
        Assert.That(
            checked(projectMilliseconds + graceMilliseconds),
            Is.EqualTo(projectMilliseconds + graceMilliseconds));
    }

    [TestCase(1)]
    [TestCase(100)]
    [TestCase(1000)]
    public void BoundResultUsesTheConfiguredTerminationGrace(
        int terminationGraceMilliseconds)
    {
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = "compiler.manifest.json",
                Sha256 = new('c', 64)
            }
        };
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        const string inputHash = ValidInputHash;
        var expectedVersions = new WorkerVersionSummary
        {
            WorkerVersion = "launcher-test",
            ApiSpecVersion = "launcher-test"
        };
        var response = new WorkerVerifyResponse
        {
            RequestHash = WorkerProtocolJson.ComputeRequestHash(request),
            InputHash = inputHash,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Miss,
                Versions = expectedVersions,
                Budgets = request.Budgets,
                ElapsedMilliseconds =
                    WorkerExecutionEnvelope.MaximumElapsedMilliseconds(
                        request, terminationGraceMilliseconds)
            }
        };

        var direct = WorkerProtocolJson.ValidateForRequest(
            response, response.RequestHash, inputHash, manifest, request,
            expectedVersions, terminationGraceMilliseconds);
        Assert.That(direct.IsValid, Is.True,
            string.Join(Environment.NewLine,
                direct.Errors.Select(static error => error.Code)));

        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, WorkerProtocolJson.SerializeResponse(response));
            Assert.That(Program.ValidateAndReport(
                path, request, inputHash, manifest, expectedVersions,
                out var valid, out _, terminationGraceMilliseconds), Is.Not.EqualTo(3));
            Assert.That(valid, Is.True);

            response.Summary.ElapsedMilliseconds++;
            var over = WorkerProtocolJson.ValidateForRequest(
                response, response.RequestHash, inputHash, manifest, request,
                expectedVersions, terminationGraceMilliseconds);
            Assert.That(over.Errors.Select(static error => error.Code),
                Does.Contain("response.elapsed_request_envelope"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestCase("input", "response.input_mismatch")]
    [TestCase("budgets", "response.budgets_mismatch")]
    [TestCase("provenance", "response.versions_mismatch")]
    public void BoundResultValidationRejectsMismatches(
        string mismatch, string expectedError)
    {
        var request = CreateValidRequest();
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        const string inputHash = ValidInputHash;
        var response = new WorkerVerifyResponse
        {
            RequestHash = WorkerProtocolJson.ComputeRequestHash(request),
            InputHash = inputHash,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "test",
                    ApiSpecVersion = "test"
                },
                Budgets = new WorkerBudgets()
            }
        };
        var expectedVersions = new WorkerVersionSummary
        {
            WorkerVersion = response.Summary.Versions.WorkerVersion,
            ApiSpecVersion = response.Summary.Versions.ApiSpecVersion,
            WorkerBinarySha256 =
                response.Summary.Versions.WorkerBinarySha256,
            ApiSpecContentSha256 =
                response.Summary.Versions.ApiSpecContentSha256
        };
        if (mismatch == "input")
        {
            response.InputHash = new('b', 64);
        }
        else if (mismatch == "budgets")
        {
            response.Summary.Budgets.QueryRlimit++;
        }
        else
        {
            response.Summary.Versions.WorkerVersion = "fabricated";
        }

        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        var error = Console.Error;
        using var capture = new StringWriter();
        try
        {
            Console.SetError(capture);
            File.WriteAllText(
                path, WorkerProtocolJson.SerializeResponse(response));
            Assert.That(
                Program.ValidateAndReport(
                    path, request, inputHash, manifest,
                    expectedVersions, out var valid, out _),
                Is.EqualTo(3));
            Assert.That(valid, Is.False);
            Assert.That(capture.ToString(), Does.Contain(expectedError));
        }
        finally
        {
            Console.SetError(error);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void BoundResultReportsUnknownClaimsAndAssumptionsAccountably()
    {
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = "compiler.manifest.json",
                Sha256 = new('c', 64)
            },
            VerifyPolicy = WorkerVerifyPolicy.RequireProven,
            AssumptionPolicy = WorkerAssumptionPolicy.Error
        };
        var manifest = CreateSarifManifest();
        var usedAssumption = UsedUserAssumption();
        var response = new WorkerVerifyResponse
        {
            RequestHash = WorkerProtocolJson.ComputeRequestHash(request),
            InputHash = new('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [
                new WorkerCallableResult {
                    CallableId = "C.M",
                    Coverage = WorkerCallableCoverage.Incomplete,
                    Reason = WorkerCallableCoverageReason.SemanticUnknown,
                    Assumptions = [
                        new WorkerAssumptionEvidence {
                            Id = "assumption-1",
                            Kind = WorkerAssumptionKind.UserAssume
                        }
                    ]
                },
                new WorkerCallableResult {
                    CallableId = "C.Unsupported",
                    Coverage = WorkerCallableCoverage.Incomplete,
                    Reason = WorkerCallableCoverageReason.UnsupportedCallable
                }
            ],
            ClaimResults = [
                new WorkerClaimResult {
                    ClaimId = "claim-1",
                    Outcome = WorkerClaimOutcome.Unknown,
                    Reason = WorkerClaimReason.UnsupportedExpression,
                    Assumptions = [usedAssumption]
                },
                new WorkerClaimResult {
                    ClaimId = "claim-2",
                    Outcome = WorkerClaimOutcome.Unknown,
                    Reason = WorkerClaimReason.UnsupportedCallable,
                    EffectCertainty = WorkerEffectEvidenceCertainty.Unavailable
                }
            ],
            Summary = new WorkerVerificationSummary
            {
                CallableCount = 2,
                ClaimCount = 2,
                OutcomeCounts = [
                    new WorkerClaimOutcomeCount {
                        Outcome = WorkerClaimOutcome.Unknown,
                        Count = 2
                    }
                ],
                ReasonCounts = [
                    new WorkerClaimReasonCount {
                        Reason = WorkerClaimReason.UnsupportedExpression,
                        Count = 1
                    },
                    new WorkerClaimReasonCount {
                        Reason = WorkerClaimReason.UnsupportedCallable,
                        Count = 1
                    }
                ],
                Assumptions = new WorkerAssumptionSummary
                {
                    Total = 1,
                    Used = 1,
                    User = 1
                },
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "launcher-test",
                    ApiSpecVersion = "launcher-test"
                },
                Budgets = new WorkerBudgets()
            }
        };
        const string inputHash = ValidInputHash;
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        var output = Console.Out;
        var error = Console.Error;
        using var outputCapture = new StringWriter();
        using var errorCapture = new StringWriter();
        try
        {
            Console.SetOut(outputCapture);
            Console.SetError(errorCapture);
            File.WriteAllText(
                path,
                WorkerProtocolJson.SerializeResponse(response));

            var exitCode = Program.ValidateAndReport(
                path,
                request,
                inputHash,
                manifest,
                response.Summary.Versions,
                out var valid,
                out _);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(valid, Is.True, errorCapture.ToString());
                Assert.That(exitCode, Is.EqualTo(6));
                Assert.That(
                    outputCapture.ToString(),
                    Does.Contain(
                        "SharpProof Unknown C.M Postcondition claim claim-1 " +
                        "(UnsupportedExpression)"));
                Assert.That(errorCapture.ToString(), Does.Contain("SP0047"));
                Assert.That(errorCapture.ToString(), Does.Contain("SP0048"));
            }
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(error);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static WorkerVerifyRequest CreateValidRequest()
    {
        return new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = "compiler.manifest.json",
                Sha256 = new('c', 64)
            }
        };
    }

    [Test]
    public void SarifProjectionIsDeterministicAndIncludesTypedResults()
    {
        var manifest = CreateSarifManifest();
        var response = new WorkerVerifyResponse
        {
            InputHash = new('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [
                new WorkerCallableResult {
                    CallableId = "C.M",
                    Coverage = WorkerCallableCoverage.Complete,
                    Reason = WorkerCallableCoverageReason.None,
                    Assumptions = [UsedUserAssumption()]
                },
                new WorkerCallableResult {
                    CallableId = "C.Unsupported",
                    Coverage = WorkerCallableCoverage.Incomplete,
                    Reason = WorkerCallableCoverageReason.UnsupportedCallable
                }
            ],
            ClaimResults = [
                new WorkerClaimResult {
                    ClaimId = "claim-1",
                    Outcome = WorkerClaimOutcome.Refuted,
                    Reason = WorkerClaimReason.None,
                    Model = [
                        new WorkerModelValue {
                            Variable = "value",
                            Kind = "Int64",
                            Value = "0"
                        }
                    ],
                    Assumptions = [UsedUserAssumption()]
                }
            ],
            Summary = new WorkerVerificationSummary
            {
                CallableCount = 2,
                ClaimCount = 1,
                CacheStatus = WorkerCacheStatus.Disabled,
                Assumptions = new WorkerAssumptionSummary
                {
                    Total = 1,
                    Used = 1,
                    User = 1
                },
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "1.0.0-test",
                    ApiSpecVersion = "test"
                }
            }
        };
        var request = new WorkerVerifyRequest
        {
            VerifyPolicy = WorkerVerifyPolicy.RequireProven,
            AssumptionPolicy = WorkerAssumptionPolicy.Error
        };

        var first = SarifProjection.Serialize(
            request, response, SarifProjectDirectory);
        var second = SarifProjection.Serialize(
            request, response, SarifProjectDirectory);

        Assert.That(second, Is.EqualTo(first));
        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        var run = root.GetProperty("runs")[0];
        var results = run.GetProperty("results");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                root.GetProperty("$schema").GetString(),
                Does.EndWith("sarif-2.1.0.json"));
            Assert.That(
                root.GetProperty("version").GetString(),
                Is.EqualTo("2.1.0"));
            Assert.That(
                run.GetProperty("invocations")[0]
                    .GetProperty("executionSuccessful").GetBoolean(),
                Is.True);
            Assert.That(results.GetArrayLength(), Is.EqualTo(2));
            Assert.That(
                results[0].GetProperty("ruleId").GetString(),
                Is.EqualTo("SharpProof.Refuted"));
            Assert.That(
                results[0].GetProperty("kind").GetString(),
                Is.EqualTo("fail"));
            Assert.That(
                results[0].GetProperty("level").GetString(),
                Is.EqualTo("error"));
            Assert.That(
                results[0].GetProperty("partialFingerprints")
                    .GetProperty("sharpProofSemanticId/v1").GetString(),
                Is.EqualTo("claim-1"));
            var physicalLocation = results[0].GetProperty("locations")[0]
                .GetProperty("physicalLocation");
            Assert.That(
                physicalLocation.GetProperty("artifactLocation")
                    .GetProperty("uri").GetString(),
                Is.EqualTo("file:///C:/source/Subject.cs"));
            Assert.That(
                physicalLocation.GetProperty("region")
                    .GetProperty("startLine").GetInt32(),
                Is.EqualTo(2));
            Assert.That(
                physicalLocation.GetProperty("region")
                    .GetProperty("startColumn").GetInt32(),
                Is.EqualTo(5));
            Assert.That(
                results[1].GetProperty("ruleId").GetString(),
                Is.EqualTo("SP0047"));
            Assert.That(
                results[1].GetProperty("level").GetString(),
                Is.EqualTo("error"));
            var assumption = run.GetProperty("invocations")[0]
                .GetProperty("toolExecutionNotifications")[0];
            Assert.That(
                assumption.GetProperty("descriptor")
                    .GetProperty("id").GetString(),
                Is.EqualTo("SP0048"));
            Assert.That(
                assumption.GetProperty("level").GetString(),
                Is.EqualTo("error"));
        }
    }

    [Test]
    public void SarifProjectionPreservesVacuityAndEffectCertainty()
    {
        var manifest = CreateSarifManifest();
        manifest.Callables = [manifest.Callables[0]];
        manifest.Claims = [manifest.Claims[0]];
        manifest.Callables[0].Assumptions = [];
        var claim = manifest.Claims.Single();
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            InputHash = new('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [
                new WorkerCallableResult {
                    CallableId = claim.CallableId,
                    Coverage = WorkerCallableCoverage.Complete,
                    Reason = WorkerCallableCoverageReason.None
                }
            ],
            ClaimResults = [
                new WorkerClaimResult {
                    ClaimId = claim.ClaimId,
                    Outcome = WorkerClaimOutcome.Proven,
                    Reason = WorkerClaimReason.None,
                    Vacuity = WorkerVacuityKind.NoModeledNormalReturn
                }
            ]
        };
        var request = new WorkerVerifyRequest();

        using var vacuity = JsonDocument.Parse(
            SarifProjection.Serialize(
                request, response, SarifProjectDirectory));
        Assert.That(
            ResultEvidence(vacuity).GetProperty("vacuity").GetString(),
            Is.EqualTo("NoModeledNormalReturn"));

        manifest.Callables[0].SelectedFeatures = [
            WorkerSelectedFeature.Effects
        ];
        manifest.Callables[0].SelectionReasons = [
            WorkerSelectionReason.ExplicitAnnotation
        ];
        claim.Kind = WorkerClaimKind.Effect;
        claim.Evidence = WorkerClaimEvidence.Attribute;
        claim.EffectContractKind = WorkerEffectContractKind.DoesNotThrow;
        WorkerProtocolJson.SealManifest(manifest);
        response.ClaimResults[0].Vacuity = WorkerVacuityKind.None;
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.CompleteMayEffectSummary;

        using var certainty = JsonDocument.Parse(
            SarifProjection.Serialize(
                request, response, SarifProjectDirectory));
        Assert.That(
            ResultEvidence(certainty).GetProperty("effectCertainty").GetString(),
            Is.EqualTo("CompleteMayEffectSummary"));

        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Refuted;
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.DefiniteViolation;
        response.ClaimResults[0].EffectWitness =
            new WorkerEffectViolationWitness
            {
                Kind = "explicit-throw",
                Detail = "T:System.InvalidOperationException",
                Effects = WorkerEffectSet.Throws,
                ExactExceptionTypeHierarchy = [
                    "System.Private.CoreLib:T:System.InvalidOperationException",
                    "System.Private.CoreLib:T:System.Exception"
                ],
                Location = new WorkerSourceLocation
                {
                    Path = "witness.cs",
                    Start = 20,
                    Length = 5,
                    Line = 9,
                    Column = 7
                }
            };
        using var refuted = JsonDocument.Parse(
            SarifProjection.Serialize(
                request, response, SarifProjectDirectory));
        var projected = refuted.RootElement.GetProperty("runs")[0]
            .GetProperty("results")[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                projected.GetProperty("ruleId").GetString(),
                Is.EqualTo("SharpProof.Refuted"));
            Assert.That(
                projected.GetProperty("message").GetProperty("text")
                    .GetString(),
                Does.Contain("concrete explicit-throw")
                    .And.Contain("witness.cs:9:7"));
            Assert.That(
                projected.GetProperty("locations")[0]
                    .GetProperty("physicalLocation")
                    .GetProperty("region")
                    .GetProperty("startLine").GetInt32(),
                Is.EqualTo(9));
            Assert.That(
                ResultEvidence(refuted).GetProperty("effectWitness")
                    .GetProperty("kind").GetString(),
                Is.EqualTo("explicit-throw"));
        }

        static JsonElement ResultEvidence(JsonDocument document)
        {
            return document.RootElement.GetProperty("runs")[0]
                .GetProperty("results")[0]
                .GetProperty("properties")
                .GetProperty("result");
        }
    }

    [TestCase(
        WorkerClaimOutcome.Proven, WorkerVerifyPolicy.Advisory,
        "pass", "none")]
    [TestCase(
        WorkerClaimOutcome.Refuted, WorkerVerifyPolicy.Advisory,
        "fail", "error")]
    [TestCase(
        WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.Advisory,
        "review", "note")]
    [TestCase(
        WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.WarnOnUnknown,
        "review", "warning")]
    [TestCase(
        WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.RequireProven,
        "review", "error")]
    public void SarifClaimPresentationFollowsOutcomeAndPolicy(
        WorkerClaimOutcome outcome, WorkerVerifyPolicy policy,
        string expectedKind, string expectedLevel)
    {
        var response = new WorkerVerifyResponse
        {
            Manifest = CreateSarifManifest(),
            ClaimResults = [
                new WorkerClaimResult {
                    ClaimId = "claim-1",
                    Outcome = outcome,
                    Reason = outcome == WorkerClaimOutcome.Unknown
                        ? WorkerClaimReason.UnsupportedExpression
                        : WorkerClaimReason.None
                }
            ],
            Summary = new WorkerVerificationSummary
            {
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "1.0.0-test"
                }
            }
        };
        using var document = JsonDocument.Parse(
            SarifProjection.Serialize(
                new WorkerVerifyRequest { VerifyPolicy = policy },
                response,
                SarifProjectDirectory));
        var result = document.RootElement.GetProperty("runs")[0]
            .GetProperty("results")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.GetProperty("ruleId").GetString(),
                Is.EqualTo("SharpProof." + outcome));
            Assert.That(
                result.GetProperty("kind").GetString(),
                Is.EqualTo(expectedKind));
            Assert.That(
                result.GetProperty("level").GetString(),
                Is.EqualTo(expectedLevel));
        }
    }

    [TestCase(WorkerVerifyPolicy.Advisory, "info")]
    [TestCase(WorkerVerifyPolicy.WarnOnUnknown, "warning")]
    [TestCase(WorkerVerifyPolicy.RequireProven, "error")]
    public void VerifyPolicyPresentationUsesNamedMappings(
        WorkerVerifyPolicy policy, string expected)
    {
        Assert.That(LauncherPresentation.Level(policy, "info"), Is.EqualTo(expected));
    }

    [TestCase(WorkerAssumptionPolicy.Allow, "info")]
    [TestCase(WorkerAssumptionPolicy.Warn, "warning")]
    [TestCase(WorkerAssumptionPolicy.Error, "error")]
    public void AssumptionPolicyPresentationUsesNamedMappings(
        WorkerAssumptionPolicy policy, string expected)
    {
        Assert.That(LauncherPresentation.Level(policy, "info"), Is.EqualTo(expected));
    }

    [TestCase("advisory", WorkerVerifyPolicy.Advisory)]
    [TestCase("warn-on-unknown", WorkerVerifyPolicy.WarnOnUnknown)]
    [TestCase("require-proven", WorkerVerifyPolicy.RequireProven)]
    public void VerifyPolicyParsingUsesNames(
        string value, WorkerVerifyPolicy expected)
    {
        Assert.That(LauncherPresentation.ParseVerifyPolicy(value), Is.EqualTo(expected));
    }

    [TestCase("allow", WorkerAssumptionPolicy.Allow)]
    [TestCase("warn", WorkerAssumptionPolicy.Warn)]
    [TestCase("error", WorkerAssumptionPolicy.Error)]
    public void AssumptionPolicyParsingUsesNames(
        string value, WorkerAssumptionPolicy expected)
    {
        Assert.That(LauncherPresentation.ParseAssumptionPolicy(value), Is.EqualTo(expected));
    }

    [Test]
    public void NumericPolicyAliasesAreRejected()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(
                (Action)(() => LauncherPresentation.ParseVerifyPolicy("1")));
            Assert.Throws<ArgumentException>(
                (Action)(() => LauncherPresentation.ParseAssumptionPolicy("1")));
        }
    }

    [Test]
    public void UnknownClaimAndEffectKindsAreRejectedExhaustively()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() => LauncherPresentation.ClaimKind(
                    new WorkerClaimManifestEntry
                    {
                        Kind = (WorkerClaimKind)int.MaxValue
                    })));
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() => LauncherPresentation.ClaimKind(
                    new WorkerClaimManifestEntry
                    {
                        Kind = WorkerClaimKind.Effect,
                        EffectContractKind =
                            (WorkerEffectContractKind)int.MaxValue
                    })));
        }
    }

    [Test]
    public void UnknownPresentationPolicyIsRejectedExhaustively()
    {
        Assert.Throws<InvalidOperationException>(
            (Action)(() => LauncherPresentation.Level(
                (WorkerVerifyPolicy)int.MaxValue,
                "info")));
    }

    [Test]
    public void ContainmentFailurePresentationPreservesDedicatedExitCode()
    {
        Assert.That(
            LauncherPresentation.ExitCode(
                WorkerRunStatus.Failed,
                WorkerRunFailureReason.ContainmentFailure),
            Is.EqualTo(125));
        Assert.That(
            LauncherPresentation.ExitCode(
                WorkerRunStatus.Failed,
                WorkerRunFailureReason.InfrastructureFailure),
            Is.EqualTo(3));
    }

    [Test]
    public void SarifProjectionPreservesInfrastructureFailure()
    {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            InputHash = new('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Failed,
            FailureReason = WorkerRunFailureReason.InfrastructureFailure,
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Disabled,
                Assumptions = new WorkerAssumptionSummary
                {
                    Total = 1,
                    User = 1
                },
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "launcher",
                    ApiSpecVersion = "unavailable"
                }
            },
            Errors = [
                new WorkerProtocolError {
                    Code = "infrastructure.test",
                    Message = "Deliberate failure."
                }
            ]
        };

        using var document = JsonDocument.Parse(
            SarifProjection.Serialize(
                new WorkerVerifyRequest
                {
                    AssumptionPolicy = WorkerAssumptionPolicy.Warn
                },
                response,
                SarifProjectDirectory));
        var invocation = document.RootElement.GetProperty("runs")[0]
            .GetProperty("invocations")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                invocation.GetProperty("executionSuccessful").GetBoolean(),
                Is.False);
            Assert.That(
                invocation.GetProperty("properties")
                    .GetProperty("runStatus").GetString(),
                Is.EqualTo("Failed"));
            Assert.That(
                invocation.GetProperty("toolExecutionNotifications")[0]
                    .GetProperty("descriptor").GetProperty("id").GetString(),
                Is.EqualTo("infrastructure.test"));
            Assert.That(
                invocation.GetProperty("toolExecutionNotifications")[1]
                    .GetProperty("descriptor").GetProperty("id").GetString(),
                Is.EqualTo("SP0048"));
        }
    }

    private static WorkerClaimManifest CreateSarifManifest()
    {
        var location = new WorkerSourceLocation
        {
            Path = @"C:\source\Subject.cs",
            Start = 10,
            Length = 4,
            Line = 2,
            Column = 5
        };
        var manifest = new WorkerClaimManifest
        {
            Callables = [
                new WorkerCallableManifestEntry {
                    CallableId = "C.M",
                    SelectedFeatures = [WorkerSelectedFeature.Contracts],
                    SelectionReasons = [
                        WorkerSelectionReason.DiscoveredPostcondition
                    ],
                    Location = location,
                    ClaimIds = ["claim-1"],
                    Assumptions = [
                        new WorkerAssumptionEvidence {
                            Id = "assumption-1",
                            Kind = WorkerAssumptionKind.UserAssume
                        }
                    ]
                },
                new WorkerCallableManifestEntry {
                    CallableId = "C.Unsupported",
                    SelectedFeatures = [WorkerSelectedFeature.Effects],
                    SelectionReasons = [
                        WorkerSelectionReason.ExplicitAnnotation
                    ],
                    Location = location,
                    ClaimIds = ["claim-2"]
                }
            ],
            Claims = [
                new WorkerClaimManifestEntry {
                    ClaimId = "claim-1",
                    CallableId = "C.M",
                    Kind = WorkerClaimKind.Postcondition,
                    Evidence = WorkerClaimEvidence.DirectClause,
                    Location = location
                },
                new WorkerClaimManifestEntry {
                    ClaimId = "claim-2",
                    CallableId = "C.Unsupported",
                    Kind = WorkerClaimKind.Effect,
                    Evidence = WorkerClaimEvidence.Attribute,
                    EffectContractKind = WorkerEffectContractKind.EnforcePure,
                    Location = location
                }
            ]
        };
        WorkerProtocolJson.SealManifest(manifest);
        return manifest;
    }

    private static WorkerAssumptionEvidence UsedUserAssumption()
    {
        return new()
        {
            Id = "assumption-1",
            Kind = WorkerAssumptionKind.UserAssume,
            Used = true
        };
    }

    private static void AssertRequestProjectionRejects(string[] arguments)
    {
        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);
        Assert.That(
            (Action)(() => parsed.CreateRequest(out _, out _)),
            Throws.TypeOf<ArgumentException>());
    }

    private static string[] ProjectionArguments(
        string worker = "worker.dll",
        string request = "request.json",
        string result = "result.json",
        string compilerManifest = "missing-compiler-manifest.json",
        string? cacheDirectory = null,
        bool? cacheEnabled = null)
    {
        string[] cacheArguments = cacheDirectory == null
            ? []
            : cacheEnabled.HasValue
                ? [
                    "--cache-enabled",
                    cacheEnabled.Value ? "true" : "false",
                    "--cache-directory", cacheDirectory
                ]
                : ["--cache-directory", cacheDirectory];
        return [
            "verify",
            "--worker", worker,
            "--request", request,
            "--result", result,
            "--compiler-manifest", compilerManifest,
            ..cacheArguments,
            "--verify-policy", "advisory",
            "--assumption-policy", "allow"
        ];
    }

    private static string[] ValidArguments()
    {
        return [
        "verify",
        "--worker", "worker.dll",
        "--request", "request.json",
        "--result", "result.json",
        "--compiler-manifest", "compiler-manifest.json",
        "--verify-policy", "advisory",
        "--assumption-policy", "allow"
    ];
    }
}
