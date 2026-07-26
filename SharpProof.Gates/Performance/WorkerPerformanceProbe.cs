using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.Attributes;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Performance;

internal sealed record WorkerPerformanceMeasurements(
    double[] CancellationLatencies,
    double ForcedTerminationMilliseconds);

internal static class WorkerPerformanceProbe {
    private const int CooperativeProjectWallMilliseconds = 100;
    private const int ForcedTerminationProbeHeadroomMilliseconds = 50;
    private const int LauncherWorkloadMethods = 384;

    internal static async Task<WorkerPerformanceMeasurements> MeasureAsync(
        string repositoryRoot,
        AcceptancePerformanceContract contract,
        CancellationToken cancellationToken) {
        using var workspace = WorkerProbeWorkspace.Create();
        await VerifyCooperativeLauncherCancellationAsync(
                repositoryRoot,
                workspace,
                contract,
                cancellationToken)
            .ConfigureAwait(false);
        var cancellationLatencies = await MeasureWorkerCancellationAsync(
                workspace,
                contract.Samples,
                cancellationToken)
            .ConfigureAwait(false);
        var forcedTermination = await MeasureForcedTerminationAsync(
                repositoryRoot,
                workspace,
                contract,
                cancellationToken)
            .ConfigureAwait(false);
        return new WorkerPerformanceMeasurements(
            cancellationLatencies,
            forcedTermination);
    }

    private static async Task<double[]> MeasureWorkerCancellationAsync(
        WorkerProbeWorkspace workspace,
        int samples,
        CancellationToken outerCancellationToken) {
        var latencies = new double[samples];
        for (var index = 0; index < samples; index++) {
            outerCancellationToken.ThrowIfCancellationRequested();
            var backend = new CancellationProbeBackend();
            using var worker = new SharpProofWorker(backend);
            using var cancellation = new CancellationTokenSource();
            var verification = worker.VerifyAsync(
                workspace.CreateCancellationRequest(),
                cancellation.Token);
            await backend.Entered.WaitAsync(outerCancellationToken)
                .ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();
            await cancellation.CancelAsync().ConfigureAwait(false);
            try {
                _ = await verification.ConfigureAwait(false);
                throw new InvalidOperationException(
                    "The worker ignored a canceled verification request.");
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested) {
            }
            stopwatch.Stop();
            latencies[index] = stopwatch.Elapsed.TotalMilliseconds;
        }
        return latencies;
    }

    private static async Task VerifyCooperativeLauncherCancellationAsync(
        string repositoryRoot,
        WorkerProbeWorkspace workspace,
        AcceptancePerformanceContract contract,
        CancellationToken cancellationToken) {
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
            checked((int)contract.ForcedTerminationMilliseconds));
        var result = await WaitForExitAsync(
                process,
                contract.ForcedTerminationMilliseconds + 10_000,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                "The real worker did not complete its project-timeout path " +
                "through the launcher. Exit code: " +
                result.ExitCode.ToString(CultureInfo.InvariantCulture) +
                Environment.NewLine +
                result.StandardError);

        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(
                    workspace.ResultPath("cooperative"),
                    cancellationToken)
                .ConfigureAwait(false)) ??
            throw new InvalidOperationException(
                "The real worker produced no timeout response.");
        if (response.Errors.Length != 0 ||
            !response.Records.Any(static record =>
                record.Reason == WorkerVerificationReason.ProjectTimeout))
            throw new InvalidOperationException(
                "The real worker did not report a cooperative project timeout.");
    }

    private static async Task<double> MeasureForcedTerminationAsync(
        string repositoryRoot,
        WorkerProbeWorkspace workspace,
        AcceptancePerformanceContract contract,
        CancellationToken cancellationToken) {
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
        try {
            var workerProcessId = await WaitForWorkerReadyAsync(
                    readyPath,
                    process,
                    boundary.Token)
                .ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            await process.WaitForExitAsync(boundary.Token)
                .ConfigureAwait(false);
            await WaitForProcessExitAsync(
                    workerProcessId,
                    boundary.Token)
                .ConfigureAwait(false);
            stopwatch.Stop();
            var output = await standardOutput.ConfigureAwait(false);
            var error = await standardError.ConfigureAwait(false);
            if (process.ExitCode != 124)
                throw new InvalidOperationException(
                    "The launcher did not force-terminate an uncooperative " +
                    "worker. Exit code: " +
                    process.ExitCode.ToString(CultureInfo.InvariantCulture) +
                    Environment.NewLine +
                    output +
                    Environment.NewLine +
                    error);
            return stopwatch.Elapsed.TotalMilliseconds;
        }
        catch {
            TryKill(process);
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
        int terminationGraceMilliseconds) {
        var launcherPath = FindBuiltAssembly(
            repositoryRoot,
            "SharpProof.Worker.Launcher");
        var startInfo = new ProcessStartInfo {
            FileName = ResolveDotNetHost(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workspace.DirectoryPath
        };
        AddArgument(startInfo, launcherPath);
        AddArgument(startInfo, "verify");
        AddOption(startInfo, "worker", workerPath);
        AddOption(startInfo, "request", workspace.RequestPath(runName));
        AddOption(startInfo, "result", workspace.ResultPath(runName));
        AddOption(
            startInfo,
            "project-directory",
            workspace.DirectoryPath);
        AddOption(startInfo, "assembly-name", "SharpProofPerformanceProbe");
        AddOption(startInfo, "sources", workspace.SourceListPath);
        AddOption(startInfo, "references", workspace.ReferenceListPath);
        AddOption(startInfo, "constants", workspace.ConstantListPath);
        AddOption(startInfo, "target-framework", "net8.0");
        AddOption(startInfo, "language-version", "12.0");
        AddOption(startInfo, "nullable", "enable");
        AddOption(startInfo, "checked-overflow", false);
        AddOption(startInfo, "optimize", true);
        AddOption(startInfo, "allow-unsafe", false);
        AddOption(startInfo, "deterministic", true);
        AddOption(startInfo, "output-type", "Library");
        AddOption(startInfo, "platform-target", "AnyCPU");
        AddOption(startInfo, "prefer-32-bit", false);
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
            "process-memory-bytes",
            WorkerBudgets.DefaultProcessMemoryLimitBytes);
        AddOption(startInfo, "max-worker-processes", 1);
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
        CancellationToken cancellationToken) {
        using var boundary = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        boundary.CancelAfter(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        var standardOutput = process.StandardOutput.ReadToEndAsync(
            boundary.Token);
        var standardError = process.StandardError.ReadToEndAsync(
            boundary.Token);
        try {
            await process.WaitForExitAsync(boundary.Token)
                .ConfigureAwait(false);
        }
        catch {
            TryKill(process);
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
        CancellationToken cancellationToken) {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            if (launcher.HasExited)
                throw new InvalidOperationException(
                    "The launcher exited before the worker probe became ready.");
            try {
                if (File.Exists(readyPath)) {
                    var text = await File.ReadAllTextAsync(
                            readyPath,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (int.TryParse(
                            text,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var processId))
                        return processId;
                }
            }
            catch (IOException) {
            }
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitForProcessExitAsync(
        int processId,
        CancellationToken cancellationToken) {
        Process? worker = null;
        try {
            worker = Process.GetProcessById(processId);
            if (!worker.HasExited)
                await worker.WaitForExitAsync(cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (ArgumentException) {
        }
        finally {
            worker?.Dispose();
        }
    }

    private static string FindBuiltAssembly(
        string repositoryRoot,
        string projectName) {
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
                     .Distinct(StringComparer.OrdinalIgnoreCase)) {
            var path = Path.Combine(
                repositoryRoot,
                projectName,
                "bin",
                candidate!,
                "net8.0",
                projectName + ".dll");
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException(
            "The required worker executable was not built.",
            projectName + ".dll");
    }

    private static string ResolveDotNetHost() {
        var configured = Environment.GetEnvironmentVariable(
            "DOTNET_HOST_PATH");
        return string.IsNullOrWhiteSpace(configured)
            ? "dotnet"
            : configured;
    }

    private static void AddOption(
        ProcessStartInfo startInfo,
        string name,
        object value) {
        AddArgument(startInfo, "--" + name);
        AddArgument(
            startInfo,
            Convert.ToString(value, CultureInfo.InvariantCulture) ??
            string.Empty);
    }

    private static void AddArgument(
        ProcessStartInfo startInfo,
        string value) =>
        startInfo.ArgumentList.Add(value);

    private static void TryKill(Process process) {
        try {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) {
        }
    }

    private sealed class CancellationProbeBackend : ISmtBackend {
        private readonly TaskCompletionSource<bool> _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Entered => _entered.Task;

        public async Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken) {
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

    private sealed class WorkerProbeWorkspace : IDisposable {
        private static readonly UTF8Encoding Utf8WithoutBom =
            new(encoderShouldEmitUTF8Identifier: false);

        private WorkerProbeWorkspace(
            string directoryPath,
            string cancellationSourcePath,
            string sourceListPath,
            string referenceListPath,
            string constantListPath,
            string[] references) {
            DirectoryPath = directoryPath;
            CancellationSourcePath = cancellationSourcePath;
            SourceListPath = sourceListPath;
            ReferenceListPath = referenceListPath;
            ConstantListPath = constantListPath;
            References = references;
        }

        internal string DirectoryPath { get; }
        internal string CancellationSourcePath { get; }
        internal string SourceListPath { get; }
        internal string ReferenceListPath { get; }
        internal string ConstantListPath { get; }
        internal string[] References { get; }

        internal static WorkerProbeWorkspace Create() {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.Gates.Performance",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var cancellationSource = Path.Combine(
                directory,
                "CancellationSubject.cs");
            var launcherSource = Path.Combine(
                directory,
                "LauncherSubject.cs");
            File.WriteAllText(
                cancellationSource,
                """
                using SharpProof.Attributes;

                public static class CancellationSubject {
                    public static long Identity(long value) {
                        Contract.Ensures(Contract.Result<long>() == value);
                        return value;
                    }
                }
                """,
                Utf8WithoutBom);
            File.WriteAllText(
                launcherSource,
                CreateLauncherSource(),
                Utf8WithoutBom);

            var references = GetReferences();
            var sourceList = Path.Combine(directory, "sources.rsp");
            var referenceList = Path.Combine(directory, "references.rsp");
            var constantList = Path.Combine(directory, "constants.rsp");
            File.WriteAllLines(
                sourceList,
                [launcherSource],
                Utf8WithoutBom);
            File.WriteAllLines(
                referenceList,
                references,
                Utf8WithoutBom);
            File.WriteAllLines(
                constantList,
                [Contract.ConditionalSymbol],
                Utf8WithoutBom);
            return new WorkerProbeWorkspace(
                directory,
                cancellationSource,
                sourceList,
                referenceList,
                constantList,
                references);
        }

        internal WorkerVerifyRequest CreateCancellationRequest() =>
            new() {
                ProjectDirectory = DirectoryPath,
                AssemblyName = "SharpProofCancellationPerformanceProbe",
                SourceFiles = [CancellationSourcePath],
                ReferenceAssemblies = References,
                DefineConstants = [Contract.ConditionalSymbol],
                Compilation = CreateCompilationOptions(),
                Budgets = new WorkerBudgets {
                    MethodWallTimeMilliseconds = 30_000,
                    ProjectWallTimeMilliseconds = 30_000,
                    MaxParallelism = 1,
                    MaxWorkerProcesses = 1
                },
                Cache = new WorkerCacheOptions {
                    Enabled = false,
                    Directory = Path.Combine(DirectoryPath, "cache")
                }
            };

        private static WorkerCompilationOptions
            CreateCompilationOptions() =>
            new() {
                TargetFramework = "net8.0",
                LanguageVersion = "12.0",
                NullableContext = WorkerNullableContext.Enabled,
                Optimization = WorkerOptimizationLevel.Release,
                CheckOverflow = false,
                AllowUnsafe = false,
                Deterministic = true,
                OutputKind =
                    WorkerOutputKind.DynamicallyLinkedLibrary,
                Platform = WorkerPlatform.AnyCpu
            };

        internal string RequestPath(string runName) =>
            Path.Combine(DirectoryPath, runName + ".request.json");

        internal string ResultPath(string runName) =>
            Path.Combine(DirectoryPath, runName + ".result.json");

        internal string CreateUncooperativeWorker(string launcherPath) {
            var path = Path.Combine(
                DirectoryPath,
                "UncooperativeWorker.dll");
            var syntaxTree = CSharpSyntaxTree.ParseText(
                """
                using System;
                using System.Globalization;
                using System.IO;
                using System.Threading;

                internal static class Program {
                    private static int Main(string[] args) {
                        var requestIndex = Array.IndexOf(args, "--request");
                        if (requestIndex < 0 || requestIndex + 1 >= args.Length)
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
            var emit = compilation.Emit(path);
            if (!emit.Success)
                throw new InvalidOperationException(
                    "The uncooperative worker probe could not be compiled: " +
                    string.Join(
                        Environment.NewLine,
                        emit.Diagnostics.Select(static diagnostic =>
                            diagnostic.ToString())));
            var launcherRuntimeConfig = Path.ChangeExtension(
                launcherPath,
                ".runtimeconfig.json");
            File.Copy(
                launcherRuntimeConfig,
                Path.ChangeExtension(path, ".runtimeconfig.json"),
                overwrite: true);
            return path;
        }

        public void Dispose() {
            var resolved = Path.GetFullPath(DirectoryPath);
            var expectedRoot = Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof.Gates.Performance"));
            if (!resolved.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected probe directory.");
            if (Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }

        private static string CreateLauncherSource() {
            var source = new StringBuilder();
            source.AppendLine("using SharpProof.Attributes;");
            source.AppendLine("public static class LauncherSubject {");
            for (var index = 0;
                 index < LauncherWorkloadMethods;
                 index++) {
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

        private static string[] GetReferences() {
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
