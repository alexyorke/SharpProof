using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpProof.Worker.Protocol;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace SharpProof.Worker.Launcher;

internal static class Program {
    internal static async Task<int> Main(string[] args) {
        if (!LauncherArguments.TryParse(args, out var arguments)) {
            Console.Error.WriteLine(
                "Usage: SharpProof.Worker.Launcher verify --worker <path> --request <path> --result <path> --project-directory <path> --assembly-name <name> --sources <path-list> --references <path-list> --constants <path-list> --target-framework <tfm> --language-version <version> --nullable <mode> --checked-overflow <bool> --optimize <bool> --allow-unsafe <bool> --deterministic <bool> --output-type <kind> --platform-target <platform> --prefer-32-bit <bool> --features <features> --verify-policy <policy> --assumption-policy <policy> [--publish-request <path> --publish-result <path>] [budget options]");
            return 2;
        }

        WorkerVerifyRequest request;
        try {
            request = arguments.CreateRequest();
            var validation = WorkerProtocolJson.Validate(request);
            if (!validation.IsValid) {
                WriteErrors(validation.Errors, string.Empty);
                return 2;
            }
            await WriteAtomicAsync(
                arguments.RequestPath,
                WorkerProtocolJson.SerializeRequest(request)).ConfigureAwait(false);
            DeleteIfExists(arguments.ResultPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or FormatException or OverflowException) {
            Console.Error.WriteLine(
                "SharpProof launcher input is invalid: " +
                exception.GetType().Name);
            return 2;
        }

        int exitCode;
        try {
            exitCode = await RunWorkerAsync(arguments, request).ConfigureAwait(false);
        }
        catch (PlatformNotSupportedException exception) {
            Console.Error.WriteLine(exception.Message);
            exitCode = 125;
            await WriteLauncherFailureAsync(
                arguments.ResultPath,
                request,
                WorkerRunStatus.Failed,
                WorkerRunFailureReason.ContainmentFailure,
                "containment.unsupported",
                exception.Message).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            System.ComponentModel.Win32Exception) {
            Console.Error.WriteLine(
                "SharpProof worker containment could not be established.");
            exitCode = 125;
            await WriteLauncherFailureAsync(
                arguments.ResultPath,
                request,
                WorkerRunStatus.Failed,
                WorkerRunFailureReason.ContainmentFailure,
                "containment.unavailable",
                "Required worker containment could not be established.")
                .ConfigureAwait(false);
        }
        if (!File.Exists(arguments.ResultPath)) {
            var containmentFailure = exitCode == 125;
            await WriteLauncherFailureAsync(
                arguments.ResultPath,
                request,
                exitCode == 124
                    ? WorkerRunStatus.TimedOut
                    : WorkerRunStatus.Failed,
                exitCode == 124
                    ? WorkerRunFailureReason.None
                    : containmentFailure
                        ? WorkerRunFailureReason.ContainmentFailure
                        : WorkerRunFailureReason.MalformedResult,
                containmentFailure
                    ? "containment.unavailable"
                    : "worker.no_result",
                containmentFailure
                    ? "Required worker containment could not be established."
                    : "The worker exited without a result.").ConfigureAwait(false);
        }
        var resultExitCode = ValidateAndReport(
            arguments.ResultPath,
            request,
            out var validResponse);
        if (validResponse) {
            try {
                await PublishPairAsync(arguments)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException) {
                Console.Error.WriteLine(
                    "SharpProof worker result could not be published.");
                return 3;
            }
        }
        if (exitCode == 0) return resultExitCode;
        if (validResponse && resultExitCode != 0) return resultExitCode;
        Console.Error.WriteLine("SharpProof worker failed closed with exit code " +
            exitCode.ToString(CultureInfo.InvariantCulture) + ".");
        return exitCode;
    }

    private static async Task<int> RunWorkerAsync(
        LauncherArguments arguments,
        WorkerVerifyRequest request) {
        var startInfo = new ProcessStartInfo {
            FileName = Environment.ProcessPath ??
                throw new InvalidOperationException("The dotnet host path is unavailable."),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = request.ProjectDirectory
        };
        startInfo.ArgumentList.Add(arguments.WorkerPath);
        startInfo.ArgumentList.Add("verify");
        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(arguments.RequestPath);
        startInfo.ArgumentList.Add("--result");
        startInfo.ArgumentList.Add(arguments.ResultPath);

        using var job = WindowsJob.CreateRequired(request.Budgets.ProcessMemoryLimitBytes,
            request.Budgets.MaxWorkerProcesses);
        var startEventName = "Local\\SharpProof.Worker." + Guid.NewGuid().ToString("N");
        using var startEvent = new EventWaitHandle(false, EventResetMode.ManualReset,
            startEventName);
        startInfo.ArgumentList.Add("--start-event");
        startInfo.ArgumentList.Add(startEventName);
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The SharpProof worker could not be started.");
        if (!job.TryAssign(process)) {
            Terminate(process);
            return 125;
        }
        var hardLimit = checked(request.Budgets.ProjectWallTimeMilliseconds +
            arguments.TerminationGraceMilliseconds);
        using var hardBoundary = new CancellationTokenSource(hardLimit);
        startEvent.Set();
        try {
            await process.WaitForExitAsync(hardBoundary.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException) {
            Terminate(process);
            return 124;
        }
    }

    private static void Terminate(Process process) {
        try {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) {
        }
    }

    internal static int ValidateAndReport(
        string resultPath,
        WorkerVerifyRequest request,
        out bool validResponse) {
        validResponse = false;
        WorkerVerifyResponse? response;
        try {
            response = WorkerProtocolJson.DeserializeResponse(
                File.ReadAllText(resultPath));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException) {
            Console.Error.WriteLine(
                "SharpProof worker result is unavailable or malformed.");
            return 3;
        }
        var validation = WorkerProtocolJson.Validate(response);
        if (!validation.IsValid) {
            WriteErrors(validation.Errors, "SharpProof ");
            return 3;
        }
        validResponse = true;
        ArgumentNullException.ThrowIfNull(response);
        WorkerProtocolJson.Canonicalize(response);
        WriteErrors(response.Errors, "SharpProof ");

        var manifestClaims = response.Manifest.Claims.ToDictionary(
            static claim => claim.ClaimId, StringComparer.Ordinal);
        var refuted = response.ClaimResults.Any(static result =>
            result.Outcome == WorkerClaimOutcome.Refuted);
        foreach (var result in response.ClaimResults) {
            var claim = manifestClaims[result.ClaimId];
            Console.WriteLine(
                "SharpProof " + result.Outcome + " " +
                claim.CallableId + " claim " + result.ClaimId +
                (result.Reason == WorkerClaimReason.None
                    ? string.Empty
                    : " (" + result.Reason + ")"));
        }
        var incomplete = response.CallableResults.Where(static result =>
            result.Coverage == WorkerCallableCoverage.Incomplete).ToArray();
        if (incomplete.Length != 0)
            ReportDiagnostic(
                response.Manifest.Callables.First(callable => callable.CallableId ==
                    incomplete[0].CallableId).Location,
                Severity(request.VerifyPolicy),
                "SP0047",
                FormattableString.Invariant(
                    $"Selected analysis is incomplete: callables={incomplete.Length}, unknown-claims={response.ClaimResults.Count(static result => result.Outcome == WorkerClaimOutcome.Unknown)}."));
        var incompleteError = incomplete.Length != 0 &&
            request.VerifyPolicy == WorkerVerifyPolicy.RequireProven;
        var assumptionError = ReportAssumptions(request.AssumptionPolicy, response);
        PrintSummary(response);
        if (response.RunStatus != WorkerRunStatus.Complete) {
            Console.Error.WriteLine("SharpProof worker run " + response.RunStatus +
                " (" + response.FailureReason + ").");
            return response.RunStatus switch {
                WorkerRunStatus.TimedOut => 124,
                WorkerRunStatus.Canceled => 4,
                _ => 3
            };
        }
        if (response.Errors.Length != 0) return 3;
        return refuted ? 5 : incompleteError || assumptionError ? 6 : 0;
    }

    private static bool ReportAssumptions(
        WorkerAssumptionPolicy policy,
        WorkerVerifyResponse response) {
        var assumptions = response.Summary.Assumptions;
        var declared = assumptions.User + assumptions.Trusted;
        if (declared == 0) return false;
        ReportDiagnostic(response.Manifest.Callables[0].Location, Severity(policy), "SP0048",
            FormattableString.Invariant(
                $"User assumption/trusted evidence declared: total={declared}, user={assumptions.User}, trusted={assumptions.Trusted}."));
        return policy == WorkerAssumptionPolicy.Error;
    }

    private static string Severity<T>(T policy) where T : struct, Enum =>
        Convert.ToInt32(policy, CultureInfo.InvariantCulture) switch {
            1 => "info",
            2 => "warning",
            3 => "error",
            _ => throw new InvalidOperationException("The policy was not validated.")
        };

    private static void ReportDiagnostic(
        WorkerSourceLocation location,
        string severity,
        string id,
        string message) {
        var prefix = string.IsNullOrWhiteSpace(location.Path)
            ? "SharpProof"
            : location.Path + FormattableString.Invariant(
                $"({location.Line},{location.Column})");
        var diagnostic = prefix + ": " + severity + " " + id + ": " + message;
        (severity == "info" ? Console.Out : Console.Error).WriteLine(diagnostic);
    }

    private static void PrintSummary(WorkerVerifyResponse response) =>
        Console.WriteLine("SharpProof summary " + JsonSerializer.Serialize(
            new { response.RunStatus, response.FailureReason, response.Summary },
            WorkerProtocolJson.Options));

    private static async Task WriteAtomicAsync(string path, string content) {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath) ??
            throw new InvalidOperationException("The output path has no directory."));
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false))
                .ConfigureAwait(false);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task PublishAsync(
        string sourcePath, string? destinationPath) {
        if (destinationPath == null ||
            string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourcePath))
            return;
        var content = await File.ReadAllTextAsync(sourcePath).ConfigureAwait(false);
        await WriteAtomicAsync(destinationPath, content)
            .ConfigureAwait(false);
    }

    private static Task PublishPairAsync(LauncherArguments arguments) {
        if (arguments.PublishRequestPath == null ||
            arguments.PublishResultPath == null) {
            PublishAsync(
                arguments.RequestPath,
                arguments.PublishRequestPath).GetAwaiter().GetResult();
            PublishAsync(
                arguments.ResultPath,
                arguments.PublishResultPath).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        var identity = Path.GetFullPath(arguments.PublishRequestPath)
            .ToUpperInvariant() + "\0" +
            Path.GetFullPath(arguments.PublishResultPath).ToUpperInvariant();
        var name = "Local\\SharpProof.Publish." +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        using var publication = new Mutex(false, name);
        var ownsPublication = false;
        try {
            try {
                ownsPublication = publication.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException) {
                ownsPublication = true;
            }
            if (!ownsPublication)
                throw new IOException(
                    "Timed out waiting to publish SharpProof results.");
            var previousRequest = File.Exists(arguments.PublishRequestPath)
                ? File.ReadAllText(arguments.PublishRequestPath)
                : null;
            try {
                PublishAsync(
                    arguments.RequestPath,
                    arguments.PublishRequestPath).GetAwaiter().GetResult();
                PublishAsync(
                    arguments.ResultPath,
                    arguments.PublishResultPath).GetAwaiter().GetResult();
            }
            catch {
                if (previousRequest == null)
                    DeleteIfExists(arguments.PublishRequestPath);
                else
                    WriteAtomicAsync(arguments.PublishRequestPath, previousRequest)
                        .GetAwaiter().GetResult();
                throw;
            }
        }
        finally {
            if (ownsPublication) publication.ReleaseMutex();
        }
        return Task.CompletedTask;
    }

    private static Task WriteLauncherFailureAsync(
        string path,
        WorkerVerifyRequest request,
        WorkerRunStatus status,
        WorkerRunFailureReason reason,
        string code,
        string message) {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse {
            InputHash =
                "e3b0c44298fc1c149afbf4c8996fb924" +
                "27ae41e4649b934ca495991b7852b855",
            Manifest = manifest,
            RunStatus = status,
            FailureReason = reason,
            Summary = new WorkerVerificationSummary {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary {
                    WorkerVersion = "launcher",
                    ApiSpecVersion = "unavailable"
                },
                Budgets = request.Budgets
            },
            Errors = status == WorkerRunStatus.Failed
                ? [new WorkerProtocolError { Code = code, Message = message }]
                : []
        };
        WorkerProtocolJson.Canonicalize(response);
        return WriteAtomicAsync(path, WorkerProtocolJson.SerializeResponse(response));
    }

    private static void DeleteIfExists(string? path) {
        if (path != null && File.Exists(path)) File.Delete(path);
    }

    private static void WriteErrors(
        IEnumerable<WorkerProtocolError> errors, string prefix) {
        foreach (var error in errors)
            Console.Error.WriteLine(prefix + error.Code + ": " + error.Message);
    }
}

internal sealed class LauncherArguments {
    private static readonly string[] s_required = [
        "worker", "request", "result", "project-directory", "assembly-name",
        "sources", "references", "constants", "target-framework", "language-version",
        "nullable", "checked-overflow", "optimize", "allow-unsafe", "deterministic",
        "output-type", "platform-target", "prefer-32-bit", "features",
        "verify-policy", "assumption-policy"
    ];
    private readonly IReadOnlyDictionary<string, string> _values;

    private LauncherArguments(IReadOnlyDictionary<string, string> values) =>
        _values = values;

    internal string WorkerPath => FullPath("worker");
    internal string RequestPath => FullPath("request");
    internal string ResultPath => FullPath("result");
    internal string? PublishRequestPath => OptionalFullPath("publish-request");
    internal string? PublishResultPath => OptionalFullPath("publish-result");
    internal int TerminationGraceMilliseconds =>
        Number("termination-grace-ms",
            WorkerLauncherDefaults.TerminationGraceMilliseconds);

    internal static bool TryParse(string[] args, out LauncherArguments arguments) {
        arguments = null!;
        if (args.Length < 3 ||
            !string.Equals(args[0], "verify", StringComparison.Ordinal) ||
            args.Length % 2 == 0)
            return false;
        var values = new Dictionary<string, string>(
            StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2) {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(key.Substring(2), args[index + 1]))
                return false;
        }
        if (s_required.Any(key =>
                !values.TryGetValue(key, out var value) ||
                string.IsNullOrWhiteSpace(value)))
            return false;
        arguments = new LauncherArguments(values);
        return true;
    }

    internal WorkerVerifyRequest CreateRequest() =>
        new() {
            ProjectDirectory = FullPath("project-directory"),
            AssemblyName = Required("assembly-name"),
            SourceFiles = ReadPathList("sources"),
            ReferenceAssemblies = ReadPathList("references"),
            DefineConstants = ReadList("constants"),
            Features = Policy<WorkerFeatureSet>("features"),
            VerifyPolicy = Policy<WorkerVerifyPolicy>("verify-policy"),
            AssumptionPolicy = Policy<WorkerAssumptionPolicy>("assumption-policy"),
            Compilation = CreateCompilation(),
            Budgets = CreateBudgets(),
            Cache = CreateCache()
        };

    private WorkerCompilationOptions CreateCompilation() => new() {
        TargetFramework = Required("target-framework"),
        LanguageVersion = Required("language-version"),
        NullableContext = NullableContext("nullable"),
        CheckOverflow = Boolean("checked-overflow"),
        Optimization = Boolean("optimize")
            ? WorkerOptimizationLevel.Release
            : WorkerOptimizationLevel.Debug,
        AllowUnsafe = Boolean("allow-unsafe"),
        Deterministic = Boolean("deterministic"),
        OutputKind = OutputKind("output-type"),
        Platform = Platform("platform-target", Boolean("prefer-32-bit"))
    };

    private WorkerBudgets CreateBudgets() => new() {
        QueryRlimit = Number("query-rlimit", WorkerBudgets.DefaultQueryRlimit),
        MethodRlimit = Number("method-rlimit", WorkerBudgets.DefaultMethodRlimit),
        MethodWallTimeMilliseconds = Number(
            "method-wall-ms", WorkerBudgets.DefaultMethodWallTimeMilliseconds),
        ProjectWallTimeMilliseconds = Number(
            "project-wall-ms", WorkerBudgets.DefaultProjectWallTimeMilliseconds),
        MaxParallelism = Number("max-parallelism", WorkerBudgets.MaximumParallelism),
        MaximumExpressionDepth = Number(
            "max-expression-depth", WorkerBudgets.DefaultMaximumExpressionDepth),
        ProcessMemoryLimitBytes = Number(
            "process-memory-bytes", WorkerBudgets.DefaultProcessMemoryLimitBytes),
        MaxWorkerProcesses = Number(
            "max-worker-processes", WorkerBudgets.MaximumParallelism)
    };

    private WorkerCacheOptions CreateCache() => new() {
        Enabled = Boolean("cache-enabled", true),
        Directory = Optional("cache-directory"),
        MaximumBytes = Number(
            "cache-maximum-bytes", WorkerCacheOptions.DefaultMaximumBytes)
    };

    private T Policy<T>(string key) where T : struct, Enum =>
        Enum.Parse<T>(Required(key).Replace("-", string.Empty, StringComparison.Ordinal),
            ignoreCase: true);

    private string[] ReadPathList(string key) =>
        [.. ReadList(key).Select(Path.GetFullPath)];

    private string[] ReadList(string key) =>
        [.. File.ReadAllLines(FullPath(key))
            .Where(static line => !string.IsNullOrWhiteSpace(line))];

    private string FullPath(string key) => Path.GetFullPath(Required(key));

    private string? OptionalFullPath(string key) {
        var value = Optional(key);
        return value == null ? null : Path.GetFullPath(value);
    }

    private string Required(string key) =>
        _values.TryGetValue(key, out var value)
            ? value
            : throw new ArgumentException(
                "A required launcher argument is missing.",
                key);

    private string? Optional(string key) =>
        _values.TryGetValue(key, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private T Number<T>(string key, T fallback) where T : struct, INumberBase<T> =>
        _values.TryGetValue(key, out var value)
            ? T.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)
            : fallback;

    private bool Boolean(string key, bool? fallback = null) =>
        _values.TryGetValue(key, out var value)
            ? bool.Parse(value)
            : fallback ?? bool.Parse(Required(key));

    private WorkerNullableContext NullableContext(string key) =>
        Required(key).ToUpperInvariant() switch {
            "DISABLE" => WorkerNullableContext.Disabled,
            "WARNINGS" => WorkerNullableContext.Warnings,
            "ANNOTATIONS" => WorkerNullableContext.Annotations,
            "ENABLE" => WorkerNullableContext.Enabled,
            _ => Invalid<WorkerNullableContext>(
                "The nullable context is invalid.", key)
        };

    private WorkerOutputKind OutputKind(string key) =>
        Required(key).ToUpperInvariant() switch {
            "EXE" => WorkerOutputKind.ConsoleApplication,
            "WINEXE" => WorkerOutputKind.WindowsApplication,
            "LIBRARY" => WorkerOutputKind.DynamicallyLinkedLibrary,
            "MODULE" => WorkerOutputKind.NetModule,
            "WINMDOBJ" => WorkerOutputKind.WindowsRuntimeMetadata,
            "APPCONTAINEREXE" =>
                WorkerOutputKind.WindowsRuntimeApplication,
            _ => Invalid<WorkerOutputKind>("The output type is invalid.", key)
        };

    private WorkerPlatform Platform(string key, bool prefer32Bit) {
        var value = Required(key);
        if (value.Equals("AnyCPU", StringComparison.OrdinalIgnoreCase))
            return prefer32Bit
                ? WorkerPlatform.AnyCpu32BitPreferred
                : WorkerPlatform.AnyCpu;
        if (prefer32Bit)
            throw new ArgumentException("Prefer32Bit is valid only for AnyCPU.", key);
        return value.ToUpperInvariant() switch {
            "X86" => WorkerPlatform.X86,
            "X64" => WorkerPlatform.X64,
            "ARM" => WorkerPlatform.Arm,
            "ARM64" => WorkerPlatform.Arm64,
            "ITANIUM" => WorkerPlatform.Itanium,
            _ => Invalid<WorkerPlatform>("The platform target is invalid.", key)
        };
    }

    private static T Invalid<T>(string message, string key) =>
        throw new ArgumentException(message, key);
}

internal sealed partial class WindowsJob : IDisposable {
    private IntPtr _handle;

    private WindowsJob(IntPtr handle) => _handle = handle;

    internal static WindowsJob CreateRequired(
        long memoryLimitBytes, int activeProcessLimit) {
        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException(
                "The SharpProof verifier requires Windows x64.");
        var handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                "A SharpProof worker Job Object could not be created.");
        var job = new WindowsJob(handle);
        var information = new NativeMethods.JobObjectExtendedLimitInformation {
            LimitFlags = NativeMethods.JobObjectLimitFlags.KillOnJobClose |
                NativeMethods.JobObjectLimitFlags.JobMemory |
                NativeMethods.JobObjectLimitFlags.ActiveProcess,
            ActiveProcessLimit = checked((uint)activeProcessLimit),
            JobMemoryLimit = checked((nuint)memoryLimitBytes)
        };
        var size = checked((uint)
            Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>());
        if (NativeMethods.SetInformationJobObject(handle, 9, ref information, size))
            return job;
        job.Dispose();
        throw new InvalidOperationException(
            "The SharpProof worker Job Object could not be configured.");
    }

    internal bool TryAssign(Process process) =>
        _handle != IntPtr.Zero &&
        NativeMethods.AssignProcessToJobObject(_handle, process.Handle);

    public void Dispose() {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
            NativeMethods.CloseHandle(handle);
    }

    private static partial class NativeMethods {
        [Flags]
        internal enum JobObjectLimitFlags : uint {
            ActiveProcess = 0x00000008, JobMemory = 0x00000200,
            KillOnJobClose = 0x00002000
        }

        // JOB_OBJECT_EXTENDED_LIMIT_INFORMATION is 144 bytes on the required x64
        // host. Only the three fields configured by the launcher need names.
        [StructLayout(LayoutKind.Explicit, Size = 144)]
        internal struct JobObjectExtendedLimitInformation {
            [FieldOffset(16)]
            internal JobObjectLimitFlags LimitFlags;
            [FieldOffset(40)]
            internal uint ActiveProcessLimit;
            [FieldOffset(120)]
            internal nuint JobMemoryLimit;
        }

        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW",
            SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetInformationJobObject(
            IntPtr job, int informationClass,
            ref JobObjectExtendedLimitInformation information, uint informationLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr handle);
    }
}
