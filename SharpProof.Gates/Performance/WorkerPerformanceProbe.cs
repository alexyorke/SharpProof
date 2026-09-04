using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Performance;

internal sealed record WorkerPerformanceMeasurements(
    double[] CancellationLatencies,
    double ForcedTerminationMilliseconds);

internal static class WorkerPerformanceProbe
{
    private const int CooperativeProjectWallMilliseconds = 100;
    private const int CooperativeTerminationGraceMilliseconds = 10_000;
    // Shared CI can observe exit after the kernel deadline; product grace is unchanged.
    private const int ForcedTerminationProbeHeadroomMilliseconds = 300;
    private const int LauncherWorkloadMethods = 384;

    internal static async Task<WorkerPerformanceMeasurements> MeasureAsync(
        string repositoryRoot,
        AcceptancePerformanceContract contract,
        CancellationToken cancellationToken)
    {
        using var workspace = WorkerProbeWorkspace.Create();
        await VerifyCooperativeLauncherCancellationAsync(
                repositoryRoot,
                workspace,
                cancellationToken)
            .ConfigureAwait(false);
        var cancellationLatencies = await MeasureWorkerCancellationAsync(
                workspace,
                contract.Samples,
                cancellationToken)
            .ConfigureAwait(false);
        var forcedTermination = await MeasureForcedTerminationCoreAsync(
                repositoryRoot,
                workspace,
                contract,
                cancellationToken)
            .ConfigureAwait(false);
        return new WorkerPerformanceMeasurements(
            cancellationLatencies,
            forcedTermination);
    }

    internal static async Task<double> MeasureForcedTerminationAsync(
        string repositoryRoot,
        AcceptancePerformanceContract contract,
        CancellationToken cancellationToken = default)
    {
        using var workspace = WorkerProbeWorkspace.Create();
        return await MeasureForcedTerminationCoreAsync(
                repositoryRoot,
                workspace,
                contract,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<double[]> MeasureWorkerCancellationAsync(
        WorkerProbeWorkspace workspace,
        int samples,
        CancellationToken outerCancellationToken)
    {
        var latencies = new double[samples];
        for (var index = 0; index < samples; index++)
        {
            outerCancellationToken.ThrowIfCancellationRequested();
            var backend = new CancellationProbeBackend();
            using var worker = new SharpProofWorker(backend);
            using var cancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    outerCancellationToken);
            var verification = worker.VerifyAsync(
                workspace.CreateCancellationRequest(),
                cancellation.Token);
            await backend.Entered.WaitAsync(outerCancellationToken)
                .ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();
            var response = await CancelAndAwaitWorkerAsync(
                    verification,
                    cancellation,
                    outerCancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            if (!IsCompleteCancellation(response))
            {
                throw new InvalidOperationException(
                    "The worker did not return a complete typed cancellation.");
            }

            latencies[index] = stopwatch.Elapsed.TotalMilliseconds;
        }
        return latencies;
    }

    internal static async Task<WorkerVerifyResponse> CancelAndAwaitWorkerAsync(
        Task<WorkerVerifyResponse> verification,
        CancellationTokenSource cancellation,
        CancellationToken outerCancellationToken)
    {
        await cancellation.CancelAsync()
            .WaitAsync(outerCancellationToken)
            .ConfigureAwait(false);
        return await verification
            .WaitAsync(outerCancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsCompleteCancellation(WorkerVerifyResponse response)
    {
        return response.RunStatus == WorkerRunStatus.Canceled &&
        response.FailureReason == WorkerRunFailureReason.None &&
        response.Manifest.Callables.Length > 0 &&
        response.Manifest.Claims.Length > 0 &&
        response.CallableResults.All(static result =>
            result is
            {
                Coverage: WorkerCallableCoverage.Incomplete,
                Reason: WorkerCallableCoverageReason.Canceled
            }) &&
        response.ClaimResults.All(static result =>
            result is
            {
                Outcome: WorkerClaimOutcome.Unknown,
                Reason: WorkerClaimReason.Canceled
            }) &&
        IsValidCleanResponse(response);
    }

    private static async Task VerifyCooperativeLauncherCancellationAsync(
        string repositoryRoot,
        WorkerProbeWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var workerPath = FindBuiltAssembly(
            repositoryRoot,
            "SharpProof.Worker");
        using var process = StartLauncher(
            repositoryRoot,
            workspace,
            workerPath,
            "cooperative",
            CooperativeProjectWallMilliseconds,
            CooperativeProjectWallMilliseconds,
            CooperativeTerminationGraceMilliseconds);
        var result = await WaitForExitAsync(
                process,
                CooperativeProjectWallMilliseconds +
                CooperativeTerminationGraceMilliseconds +
                10_000,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 124)
        {
            throw new InvalidOperationException(
                "The real worker did not complete its project-timeout path " +
                "through the launcher. Exit code: " +
                result.ExitCode.ToString(CultureInfo.InvariantCulture) +
                Environment.NewLine +
                result.StandardError);
        }

        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(
                    workspace.ResultPath("cooperative"),
                    cancellationToken)
                .ConfigureAwait(false)) ??
            throw new InvalidOperationException(
                "The real worker produced no timeout response.");
        if (!IsCompleteProjectTimeout(response))
        {
            throw new InvalidOperationException(
                "The real worker did not report a cooperative project timeout.");
        }
    }

    internal static bool IsCompleteProjectTimeout(
        WorkerVerifyResponse response)
    {
        return response.RunStatus == WorkerRunStatus.TimedOut &&
            (response.ClaimResults.Any(static result =>
                result.Reason == WorkerClaimReason.ProjectTimeout) ||
            response.CallableResults.Any(static result =>
                result.Reason ==
                WorkerCallableCoverageReason.ProjectTimeout)) &&
            IsValidCleanResponse(response);
    }

    private static bool IsValidCleanResponse(WorkerVerifyResponse response)
    {
        return response.Errors.Length == 0 &&
            WorkerProtocolJson.Validate(response).IsValid;
    }

    private static async Task<double> MeasureForcedTerminationCoreAsync(
        string repositoryRoot,
        WorkerProbeWorkspace workspace,
        AcceptancePerformanceContract contract,
        CancellationToken cancellationToken)
    {
        var probeWorker = workspace.CreateUncooperativeWorker(
            FindBuiltAssembly(
                repositoryRoot,
                "SharpProof.Worker.Launcher"));
        var requestPath = workspace.RequestPath("forced");
        var readyPath = requestPath + ".ready";
        var probeGraceMilliseconds = Math.Max(
            1,
            checked((int)contract.ForcedTerminationMilliseconds) -
            ForcedTerminationProbeHeadroomMilliseconds);
        using var process = StartLauncher(
            repositoryRoot,
            workspace,
            probeWorker,
            "forced",
            projectWallMilliseconds: 1,
            methodWallMilliseconds: 1,
            probeGraceMilliseconds);
        using var boundary = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        boundary.CancelAfter(
            TimeSpan.FromMilliseconds(
                contract.ForcedTerminationMilliseconds + 10_000));
        var standardOutput = process.StandardOutput.ReadToEndAsync(
            boundary.Token);
        var standardError = process.StandardError.ReadToEndAsync(
            boundary.Token);
        try
        {
            var workerProcessId = await WaitForWorkerReadyAsync(
                    readyPath,
                    process,
                    standardError,
                    boundary.Token)
                .ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            var waitLimit = checked(
                (int)contract.ForcedTerminationMilliseconds + 10_000);
            // Begin observing the worker before waiting for the launcher. If
            // the worker exits first, reopening only after launcher exit can
            // accidentally inspect a process that reused its numeric PID.
            var workerExit = Task.Run(
                () => WaitForProcessExit(workerProcessId, waitLimit),
                boundary.Token);
#pragma warning disable CA1849 // The deadline probe intentionally uses kernel waits.
            if (!process.WaitForExit(waitLimit))
            {
                throw new TimeoutException(
                    "The launcher did not reach its hard deadline.");
            }

            await workerExit.WaitAsync(boundary.Token).ConfigureAwait(false);
#pragma warning restore CA1849
            stopwatch.Stop();
            var output = await standardOutput.ConfigureAwait(false);
            var error = await standardError.ConfigureAwait(false);
            if (process.ExitCode != 124)
            {
                throw new InvalidOperationException(
                    "The launcher did not force-terminate an uncooperative " +
                    "worker. Exit code: " +
                    process.ExitCode.ToString(CultureInfo.InvariantCulture) +
                    Environment.NewLine +
                    output +
                    Environment.NewLine +
                    error);
            }

            return stopwatch.Elapsed.TotalMilliseconds;
        }
        catch
        {
            GateProcess.KillTree(process);
            throw;
        }
    }

    private static Process StartLauncher(
        string repositoryRoot,
        WorkerProbeWorkspace workspace,
        string workerPath,
        string runName,
        int projectWallMilliseconds,
        int methodWallMilliseconds,
        int terminationGraceMilliseconds)
    {
        var launcherPath = FindBuiltAssembly(
            repositoryRoot,
            "SharpProof.Worker.Launcher");
        var startInfo = GateProcess.CreateCaptured(
            ResolveDotNetHost(),
            workspace.DirectoryPath);
        AddArgument(startInfo, launcherPath);
        AddArgument(startInfo, "verify");
        AddOption(startInfo, "worker", workerPath);
        AddOption(startInfo, "request", workspace.RequestPath(runName));
        AddOption(startInfo, "result", workspace.ResultPath(runName));
        AddOption(startInfo, "compiler-manifest", workspace.LauncherManifestPath);
        AddOption(startInfo, "verify-policy", "advisory");
        AddOption(startInfo, "assumption-policy", "allow");
        AddOption(
            startInfo,
            "query-rlimit",
            WorkerBudgets.DefaultQueryRlimit);
        AddOption(
            startInfo,
            "method-rlimit",
            WorkerBudgets.DefaultMethodRlimit);
        AddOption(
            startInfo,
            "method-wall-ms",
            methodWallMilliseconds);
        AddOption(
            startInfo,
            "project-wall-ms",
            projectWallMilliseconds);
        AddOption(startInfo, "max-parallelism", 1);
        AddOption(
            startInfo,
            "max-expression-depth",
            WorkerBudgets.DefaultMaximumExpressionDepth);
        AddOption(
            startInfo,
            "termination-grace-ms",
            terminationGraceMilliseconds);
        AddOption(startInfo, "cache-enabled", false);
        AddOption(
            startInfo,
            "cache-maximum-bytes",
            WorkerCacheOptions.DefaultMaximumBytes);
        return Process.Start(startInfo) ??
               throw new InvalidOperationException(
                   "The worker launcher could not be started.");
    }

    private static async Task<ProcessResult> WaitForExitAsync(
        Process process,
        double timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using var boundary = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        boundary.CancelAfter(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        var standardOutput = process.StandardOutput.ReadToEndAsync(
            boundary.Token);
        var standardError = process.StandardError.ReadToEndAsync(
            boundary.Token);
        try
        {
            await process.WaitForExitAsync(boundary.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            GateProcess.KillTree(process);
            throw;
        }
        return new ProcessResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    private static async Task<int> WaitForWorkerReadyAsync(
        string readyPath,
        Process launcher,
        Task<string> standardError,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (launcher.HasExited)
            {
                throw new InvalidOperationException(
                    "The launcher exited before the worker probe became ready." +
                    Environment.NewLine + await standardError.ConfigureAwait(false));
            }

            try
            {
                if (File.Exists(readyPath))
                {
                    var text = await File.ReadAllTextAsync(
                            readyPath,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (int.TryParse(
                            text,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var processId))
                    {
                        return processId;
                    }
                }
            }
            catch (IOException)
            {
            }
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void WaitForProcessExit(
        int processId,
        int timeoutMilliseconds)
    {
        Process? worker = null;
        try
        {
            worker = Process.GetProcessById(processId);
#pragma warning disable CA1849 // The deadline probe intentionally uses kernel waits.
            if (!worker.HasExited &&
                !worker.WaitForExit(timeoutMilliseconds))
            {
                throw new TimeoutException(
                    "The worker process tree did not terminate.");
            }
#pragma warning restore CA1849
        }
        catch (ArgumentException)
        {
        }
        finally
        {
            worker?.Dispose();
        }
    }

    private static string FindBuiltAssembly(
        string repositoryRoot,
        string projectName)
    {
        var configuration = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)).Parent?.Name;
        var candidates = new[] {
            configuration,
            "Release",
            "Debug"
        };
        foreach (var candidate in candidates
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(
                repositoryRoot,
                projectName,
                "bin",
                candidate!,
                "net9.0",
                projectName + ".dll");
            if (File.Exists(path))
            {
                return path;
            }
        }
        throw new FileNotFoundException(
            "The required worker executable was not built.",
            projectName + ".dll");
    }

    private static string ResolveDotNetHost()
    {
        var configured = Environment.GetEnvironmentVariable(
            "DOTNET_HOST_PATH");
        return string.IsNullOrWhiteSpace(configured)
            ? "dotnet"
            : configured;
    }

    private static void AddOption(
        ProcessStartInfo startInfo,
        string name,
        object value)
    {
        AddArgument(startInfo, "--" + name);
        AddArgument(
            startInfo,
            Convert.ToString(value, CultureInfo.InvariantCulture) ??
            string.Empty);
    }

    private static void AddArgument(
        ProcessStartInfo startInfo,
        string value)
    {
        startInfo.ArgumentList.Add(value);
    }

    private sealed class CancellationProbeBackend : ISmtBackend
    {
        private readonly TaskCompletionSource<bool> _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Entered => _entered.Task;

        public async Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            _entered.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
            return BackendCheckResult.Unknown(
                BackendFailureReason.InfrastructureFailure);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class WorkerProbeWorkspace : IDisposable
    {
        private static readonly UTF8Encoding Utf8WithoutBom =
            new(encoderShouldEmitUTF8Identifier: false);

        private WorkerProbeWorkspace(
            string directoryPath,
            WorkerFileReference cancellationManifestReference)
        {
            DirectoryPath = directoryPath;
            _cancellationManifestReference = cancellationManifestReference;
        }

        internal string DirectoryPath
        {
            get;
        }
        private string WorkerDirectoryPath =>
            Path.Combine(DirectoryPath, "worker");
        private string IoDirectoryPath =>
            Path.Combine(DirectoryPath, "io");
        internal string LauncherManifestPath =>
            Path.Combine(IoDirectoryPath, "launcher.compiler-manifest.json");
        private readonly WorkerFileReference _cancellationManifestReference;

        internal static WorkerProbeWorkspace Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.Gates.Performance",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var workerDirectory = Path.Combine(directory, "worker");
            var ioDirectory = Path.Combine(directory, "io");
            Directory.CreateDirectory(workerDirectory);
            Directory.CreateDirectory(ioDirectory);
            var cancellationSource = Path.Combine(
                ioDirectory,
                "CancellationSubject.cs");
            var launcherSource = Path.Combine(
                ioDirectory,
                "LauncherSubject.cs");
            var cancellationSourceText =
                """
                using SharpProof.Attributes;

                public static class CancellationSubject {
                    public static long Identity(long value) {
                        Contract.Ensures(Contract.Result<long>() == value);
                        return value;
                    }
                }
                """;
            var launcherSourceText = CreateLauncherSource();
            File.WriteAllText(
                cancellationSource,
                cancellationSourceText,
                Utf8WithoutBom);
            File.WriteAllText(
                launcherSource,
                launcherSourceText,
                Utf8WithoutBom);

            var references = GetReferences();
            var launcherManifestPath = Path.Combine(
                ioDirectory,
                "launcher.compiler-manifest.json");
            var cancellationManifestPath = Path.Combine(
                directory,
                "cancellation.compiler-manifest.json");
            WriteCompilerManifest(
                launcherManifestPath,
                "SharpProofPerformanceProbe",
                launcherSource,
                launcherSourceText,
                references);
            WriteCompilerManifest(
                cancellationManifestPath,
                "SharpProofCancellationPerformanceProbe",
                cancellationSource,
                cancellationSourceText,
                references);
            var workspace = new WorkerProbeWorkspace(
                directory,
                Reference(cancellationManifestPath));
            return workspace;
        }

        internal WorkerVerifyRequest CreateCancellationRequest()
        {
            return new()
            {
                CompilerManifest = new WorkerFileReference
                {
                    Path = _cancellationManifestReference.Path,
                    Sha256 = _cancellationManifestReference.Sha256
                },
                Budgets = new WorkerBudgets
                {
                    MethodWallTimeMilliseconds = 30_000,
                    ProjectWallTimeMilliseconds = 30_000,
                    MaxParallelism = 1
                },
                Cache = new WorkerCacheOptions
                {
                    Enabled = false,
                    Directory = Path.Combine(DirectoryPath, "cache")
                }
            };
        }

        private static WorkerFileReference Reference(string path)
        {
            return new()
            {
                Path = path,
                Sha256 = LowerSha(File.ReadAllBytes(path))
            };
        }

        private static void WriteCompilerManifest(
            string artifactPath, string assemblyName, string sourcePath,
            string source, IEnumerable<string> referencePaths)
        {
            var tree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp12),
                sourcePath);
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [tree],
                referencePaths.Select(static path =>
                    MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    nullableContextOptions: NullableContextOptions.Enable,
                    deterministic: true,
                    concurrentBuild: false));
            var discovery = new ClaimManifestBuilder(compilation).Build();
            var artifact = CompilerManifestArtifactProducer.Create(
                compilation,
                Path.GetDirectoryName(sourcePath)!,
                "net9.0",
                WorkerFeatureSet.All,
                discovery,
                WorkerBudgets.DefaultMaximumExpressionDepth,
                CancellationToken.None);
            File.WriteAllText(
                artifactPath,
                CompilerManifestArtifactJson.SerializeValidated(artifact),
                Utf8WithoutBom);
        }

        private static string LowerSha(byte[] bytes)
        {
            return HashEncoding.ComputeSha256Hex(bytes);
        }

        internal string RequestPath(string runName)
        {
            return Path.Combine(IoDirectoryPath, runName + ".request.json");
        }

        internal string ResultPath(string runName)
        {
            return Path.Combine(IoDirectoryPath, runName + ".result.json");
        }

        internal string CreateUncooperativeWorker(string launcherPath)
        {
            var path = Path.Combine(
                WorkerDirectoryPath,
                "UncooperativeWorker.dll");
            var syntaxTree = CSharpSyntaxTree.ParseText(
                """
                using System;
                using System.Globalization;
                using System.IO;
                using System.Threading;

                [assembly: System.Reflection.AssemblyProduct("SharpProof.Worker")]
                [assembly: System.Reflection.AssemblyInformationalVersion("performance-probe")]

                internal static class Program {
                    private static int Main(string[] args) {
                        var requestIndex = Array.IndexOf(args, "--request");
                        if (requestIndex < 0 ||
                            requestIndex + 1 >= args.Length)
                            return 2;
                        if (!string.Equals(
                                Console.ReadLine(),
                                "SharpProof.Start/1",
                                StringComparison.Ordinal))
                            return 2;
                        File.WriteAllText(
                            args[requestIndex + 1] + ".ready",
                            Environment.ProcessId.ToString(
                                CultureInfo.InvariantCulture));
                        Thread.Sleep(Timeout.Infinite);
                        return 0;
                    }
                }
                """,
                CSharpParseOptions.Default.WithLanguageVersion(
                    LanguageVersion.CSharp12));
            var trustedPlatformAssemblies =
                (string?)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES") ??
                throw new InvalidOperationException(
                    "Trusted platform assemblies are unavailable.");
            var compilation = CSharpCompilation.Create(
                "UncooperativeWorker",
                [syntaxTree],
                trustedPlatformAssemblies
                    .Split(Path.PathSeparator)
                    .Select(static reference =>
                        MetadataReference.CreateFromFile(reference)),
                new CSharpCompilationOptions(
                    OutputKind.ConsoleApplication,
                    optimizationLevel: OptimizationLevel.Release));
            using var resources = compilation.CreateDefaultWin32Resources(
                versionResource: true, noManifest: true,
                manifestContents: null, iconInIcoFormat: null);
            using var output = File.Create(path);
            var emit = compilation.Emit(output, win32Resources: resources);
            if (!emit.Success)
            {
                throw new InvalidOperationException(
                    "The uncooperative worker probe could not be compiled: " +
                    string.Join(
                        Environment.NewLine,
                        emit.Diagnostics.Select(static diagnostic =>
                            diagnostic.ToString())));
            }

            var launcherRuntimeConfig = Path.ChangeExtension(
                launcherPath,
                ".runtimeconfig.json");
            File.Copy(
                launcherRuntimeConfig,
                Path.ChangeExtension(path, ".runtimeconfig.json"),
                overwrite: true);
            var targetFramework = AppContext.TargetFrameworkName ??
                throw new InvalidOperationException(
                    "The performance probe target framework is unavailable.");
            File.WriteAllText(
                Path.ChangeExtension(path, ".deps.json"),
                $$"""
                {
                  "runtimeTarget": {
                    "name": "{{targetFramework}}",
                    "signature": ""
                  },
                  "compilationOptions": {},
                  "targets": {
                    "{{targetFramework}}": {
                      "UncooperativeWorker/1.0.0": {
                        "runtime": {
                          "UncooperativeWorker.dll": {}
                        }
                      }
                    }
                  },
                  "libraries": {
                    "UncooperativeWorker/1.0.0": {
                      "type": "project",
                      "serviceable": false,
                      "sha512": ""
                    }
                  }
                }
                """,
                Utf8WithoutBom);
            return path;
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(DirectoryPath);
            var expectedRoot = Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof.Gates.Performance"));
            if (!resolved.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected probe directory.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }

        private static string CreateLauncherSource()
        {
            var source = new StringBuilder();
            source.AppendLine("using SharpProof.Attributes;");
            source.AppendLine("public static class LauncherSubject {");
            for (var index = 0;
                 index < LauncherWorkloadMethods;
                 index++)
            {
                source.Append("    public static long M")
                    .Append(index.ToString(CultureInfo.InvariantCulture))
                    .AppendLine("(long value) {");
                source.AppendLine(
                    "        Contract.Ensures(Contract.Result<long>() == value);");
                source.AppendLine("        return value;");
                source.AppendLine("    }");
            }
            source.AppendLine("}");
            return source.ToString();
        }

        private static string[] GetReferences()
        {
            var trustedPlatformAssemblies =
                (string?)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES") ??
                throw new InvalidOperationException(
                    "Trusted platform assemblies are unavailable.");
            var names = new HashSet<string>(
                [
                    "System.Private.CoreLib.dll",
                    "System.Runtime.dll",
                    "netstandard.dll"
                ],
                StringComparer.OrdinalIgnoreCase);
            return [.. trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Where(path => names.Contains(Path.GetFileName(path)))
                .Append(typeof(Contract).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path, StringComparer.Ordinal)];
        }
    }
}
