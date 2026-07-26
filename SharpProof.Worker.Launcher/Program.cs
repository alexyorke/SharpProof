using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static class Program {
    internal static async Task<int> Main(string[] args) {
        if (!LauncherArguments.TryParse(args, out var arguments)) {
            Console.Error.WriteLine(
                "Usage: SharpProof.Worker.Launcher verify --worker <path> --request <path> --result <path> --project-directory <path> --assembly-name <name> --sources <path-list> --references <path-list> --constants <path-list> --target-framework <tfm> --language-version <version> --nullable <mode> --checked-overflow <bool> --optimize <bool> --allow-unsafe <bool> --deterministic <bool> --output-type <kind> --platform-target <platform> --prefer-32-bit <bool> [budget options]");
            return 2;
        }

        WorkerVerifyRequest request;
        try {
            request = arguments.CreateRequest();
            var validation = WorkerProtocolJson.Validate(request);
            if (!validation.IsValid) {
                foreach (var error in validation.Errors)
                    Console.Error.WriteLine(error.Code + ": " + error.Message);
                return 2;
            }
            await WriteAtomicAsync(
                arguments.RequestPath,
                WorkerProtocolJson.SerializeRequest(request))
                .ConfigureAwait(false);
            if (File.Exists(arguments.ResultPath))
                File.Delete(arguments.ResultPath);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            FormatException or
            OverflowException) {
            Console.Error.WriteLine(
                "SharpProof launcher input is invalid: " +
                exception.GetType().Name);
            return 2;
        }

        int exitCode;
        try {
            exitCode = await RunWorkerAsync(arguments, request)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            PlatformNotSupportedException) {
            Console.Error.WriteLine(
                "SharpProof worker containment could not be established.");
            return 125;
        }
        if (exitCode != 0) {
            Console.Error.WriteLine(
                "SharpProof worker failed closed with exit code " +
                exitCode.ToString(CultureInfo.InvariantCulture) + ".");
            return exitCode;
        }
        return ValidateAndReport(arguments.ResultPath);
    }

    private static async Task<int> RunWorkerAsync(
        LauncherArguments arguments,
        WorkerVerifyRequest request) {
        var startInfo = new ProcessStartInfo {
            FileName = Environment.ProcessPath ??
                       throw new InvalidOperationException(
                           "The dotnet host path is unavailable."),
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

        using var job = WindowsJob.CreateRequired(
            request.Budgets.ProcessMemoryLimitBytes,
            request.Budgets.MaxWorkerProcesses);
        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException(
                                "The SharpProof worker could not be started.");
        if (!job.TryAssign(process)) {
            Terminate(process);
            return 125;
        }
        using var hardBoundary = new CancellationTokenSource(
            checked(
                request.Budgets.ProjectWallTimeMilliseconds +
                arguments.TerminationGraceMilliseconds));
        try {
            await process.WaitForExitAsync(hardBoundary.Token)
                .ConfigureAwait(false);
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

    private static int ValidateAndReport(string resultPath) {
        WorkerVerifyResponse? response;
        try {
            response = WorkerProtocolJson.DeserializeResponse(
                File.ReadAllText(resultPath));
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            System.Text.Json.JsonException) {
            Console.Error.WriteLine(
                "SharpProof worker result is unavailable or malformed.");
            return 3;
        }
        if (response == null ||
            !string.Equals(
                response.ProtocolVersion,
                WorkerProtocolVersions.Current,
                StringComparison.Ordinal) ||
            response.InputHash.Length != 64 ||
            response.InputHash.Any(static character =>
                !Uri.IsHexDigit(character))) {
            Console.Error.WriteLine(
                "SharpProof worker result failed protocol validation.");
            return 3;
        }
        WorkerProtocolJson.Canonicalize(response);
        if (response.Errors.Length != 0) {
            foreach (var error in response.Errors)
                Console.Error.WriteLine(
                    "SharpProof " + error.Code + ": " + error.Message);
            return 3;
        }

        var refuted = false;
        foreach (var record in response.Records) {
            if (!IsValid(record)) {
                Console.Error.WriteLine(
                    "SharpProof worker record failed validation.");
                return 3;
            }
            Console.WriteLine(
                "SharpProof " + record.Status + " " +
                record.CallableId + " contract " +
                record.ContractOrdinal.ToString(
                    CultureInfo.InvariantCulture) +
                (record.Reason == WorkerVerificationReason.None
                    ? string.Empty
                    : " (" + record.Reason + ")"));
            refuted |= record.Status == WorkerVerificationStatus.Refuted;
        }
        return refuted ? 5 : 0;
    }

    private static bool IsValid(WorkerVerificationRecord record) =>
        !string.IsNullOrWhiteSpace(record.CallableId) &&
        record.ContractOrdinal >= 0 &&
        record.SourceStart >= 0 &&
        record.ProofCore != null &&
        record.Model != null &&
        (record.Status == WorkerVerificationStatus.Unknown
            ? record.Reason != WorkerVerificationReason.None
            : record.Reason == WorkerVerificationReason.None);

    private static async Task WriteAtomicAsync(
        string path,
        string content) {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath) ??
            throw new InvalidOperationException(
                "The output path has no directory."));
        var temporary = fullPath + "." +
                        Guid.NewGuid().ToString("N") + ".tmp";
        try {
            await File.WriteAllTextAsync(
                temporary,
                content,
                new UTF8Encoding(false)).ConfigureAwait(false);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal sealed class LauncherArguments {
    private readonly IReadOnlyDictionary<string, string> _values;

    private LauncherArguments(IReadOnlyDictionary<string, string> values) =>
        _values = values;

    internal string WorkerPath => FullPath("worker");
    internal string RequestPath => FullPath("request");
    internal string ResultPath => FullPath("result");
    internal int TerminationGraceMilliseconds =>
        Integer(
            "termination-grace-ms",
            WorkerLauncherDefaults.TerminationGraceMilliseconds);

    internal static bool TryParse(
        string[] args,
        out LauncherArguments arguments) {
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
        string[] required = [
            "worker",
            "request",
            "result",
            "project-directory",
            "assembly-name",
            "sources",
            "references",
            "constants",
            "target-framework",
            "language-version",
            "nullable",
            "checked-overflow",
            "optimize",
            "allow-unsafe",
            "deterministic",
            "output-type",
            "platform-target",
            "prefer-32-bit"
        ];
        if (required.Any(key =>
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
            Compilation = new WorkerCompilationOptions {
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
                Platform = Platform(
                    "platform-target",
                    Boolean("prefer-32-bit"))
            },
            Budgets = new WorkerBudgets {
                QueryRlimit = Unsigned(
                    "query-rlimit",
                    WorkerBudgets.DefaultQueryRlimit),
                MethodRlimit = Unsigned(
                    "method-rlimit",
                    WorkerBudgets.DefaultMethodRlimit),
                MethodWallTimeMilliseconds = Integer(
                    "method-wall-ms",
                    WorkerBudgets.DefaultMethodWallTimeMilliseconds),
                ProjectWallTimeMilliseconds = Integer(
                    "project-wall-ms",
                    WorkerBudgets.DefaultProjectWallTimeMilliseconds),
                MaxParallelism = Integer(
                    "max-parallelism",
                    WorkerBudgets.MaximumParallelism),
                MaximumExpressionDepth = Integer(
                    "max-expression-depth",
                    WorkerBudgets.DefaultMaximumExpressionDepth),
                ProcessMemoryLimitBytes = Long(
                    "process-memory-bytes",
                    WorkerBudgets.DefaultProcessMemoryLimitBytes),
                MaxWorkerProcesses = Integer(
                    "max-worker-processes",
                    WorkerBudgets.MaximumParallelism)
            },
            Cache = new WorkerCacheOptions {
                Enabled = Boolean("cache-enabled", true),
                Directory = Optional("cache-directory"),
                MaximumBytes = Long(
                    "cache-maximum-bytes",
                    WorkerCacheOptions.DefaultMaximumBytes)
            }
        };

    private string[] ReadPathList(string key) =>
        [.. ReadList(key).Select(Path.GetFullPath)];

    private string[] ReadList(string key) =>
        [.. File.ReadAllLines(FullPath(key)).Where(static line => !string.IsNullOrWhiteSpace(line))];

    private string FullPath(string key) =>
        Path.GetFullPath(Required(key));

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

    private int Integer(string key, int fallback) =>
        _values.TryGetValue(key, out var value)
            ? int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)
            : fallback;

    private uint Unsigned(string key, uint fallback) =>
        _values.TryGetValue(key, out var value)
            ? uint.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)
            : fallback;

    private long Long(string key, long fallback) =>
        _values.TryGetValue(key, out var value)
            ? long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)
            : fallback;

    private bool Boolean(string key, bool fallback) =>
        _values.TryGetValue(key, out var value)
            ? bool.Parse(value)
            : fallback;

    private bool Boolean(string key) =>
        bool.Parse(Required(key));

    private WorkerNullableContext NullableContext(string key) =>
        Required(key).ToLowerInvariant() switch {
            "disable" => WorkerNullableContext.Disabled,
            "warnings" => WorkerNullableContext.Warnings,
            "annotations" => WorkerNullableContext.Annotations,
            "enable" => WorkerNullableContext.Enabled,
            _ => throw new ArgumentException(
                "The nullable context is invalid.",
                key)
        };

    private WorkerOutputKind OutputKind(string key) =>
        Required(key).ToLowerInvariant() switch {
            "exe" => WorkerOutputKind.ConsoleApplication,
            "winexe" => WorkerOutputKind.WindowsApplication,
            "library" => WorkerOutputKind.DynamicallyLinkedLibrary,
            "module" => WorkerOutputKind.NetModule,
            "winmdobj" => WorkerOutputKind.WindowsRuntimeMetadata,
            "appcontainerexe" =>
                WorkerOutputKind.WindowsRuntimeApplication,
            _ => throw new ArgumentException(
                "The output type is invalid.",
                key)
        };

    private WorkerPlatform Platform(string key, bool prefer32Bit) {
        var value = Required(key);
        if (value.Equals("AnyCPU", StringComparison.OrdinalIgnoreCase))
            return prefer32Bit
                ? WorkerPlatform.AnyCpu32BitPreferred
                : WorkerPlatform.AnyCpu;
        if (prefer32Bit)
            throw new ArgumentException(
                "Prefer32Bit is valid only for AnyCPU.",
                key);
        return value.ToLowerInvariant() switch {
            "x86" => WorkerPlatform.X86,
            "x64" => WorkerPlatform.X64,
            "arm" => WorkerPlatform.Arm,
            "arm64" => WorkerPlatform.Arm64,
            "itanium" => WorkerPlatform.Itanium,
            _ => throw new ArgumentException(
                "The platform target is invalid.",
                key)
        };
    }
}

internal sealed partial class WindowsJob : IDisposable {
    private IntPtr _handle;

    private WindowsJob(IntPtr handle) => _handle = handle;

    internal static WindowsJob CreateRequired(
        long memoryLimitBytes,
        int activeProcessLimit) {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "The SharpProof worker requires Windows Job Objects.");
        var handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                "A SharpProof worker Job Object could not be created.");
        var job = new WindowsJob(handle);
        var information = new NativeMethods.JobObjectExtendedLimitInformation {
            BasicLimitInformation = {
                LimitFlags =
                    NativeMethods.JobObjectLimitFlags.KillOnJobClose |
                    NativeMethods.JobObjectLimitFlags.JobMemory |
                    NativeMethods.JobObjectLimitFlags.ActiveProcess,
                ActiveProcessLimit = checked((uint)activeProcessLimit)
            },
            JobMemoryLimit = checked((nuint)memoryLimitBytes)
        };
        var size = Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(information, pointer, false);
            if (!NativeMethods.SetInformationJobObject(
                    handle,
                    9,
                    pointer,
                    checked((uint)size))) {
                job.Dispose();
                throw new InvalidOperationException(
                    "The SharpProof worker Job Object could not be configured.");
            }
            return job;
        }
        finally {
            Marshal.FreeHGlobal(pointer);
        }
    }

    internal bool TryAssign(Process process) =>
        _handle != IntPtr.Zero &&
        NativeMethods.AssignProcessToJobObject(
            _handle,
            process.Handle);

    public void Dispose() {
        var handle = Interlocked.Exchange(
            ref _handle,
            IntPtr.Zero);
        if (handle != IntPtr.Zero)
            NativeMethods.CloseHandle(handle);
    }

    private static partial class NativeMethods {
        [Flags]
        internal enum JobObjectLimitFlags : uint {
            ActiveProcess = 0x00000008,
            JobMemory = 0x00000200,
            KillOnJobClose = 0x00002000
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BasicLimitInformation {
            internal long PerProcessUserTimeLimit;
            internal long PerJobUserTimeLimit;
            internal JobObjectLimitFlags LimitFlags;
            internal nuint MinimumWorkingSetSize;
            internal nuint MaximumWorkingSetSize;
            internal uint ActiveProcessLimit;
            internal nuint Affinity;
            internal uint PriorityClass;
            internal uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IoCounters {
            internal ulong ReadOperationCount;
            internal ulong WriteOperationCount;
            internal ulong OtherOperationCount;
            internal ulong ReadTransferCount;
            internal ulong WriteTransferCount;
            internal ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JobObjectExtendedLimitInformation {
            internal BasicLimitInformation BasicLimitInformation;
            internal IoCounters IoInfo;
            internal nuint ProcessMemoryLimit;
            internal nuint JobMemoryLimit;
            internal nuint PeakProcessMemoryUsed;
            internal nuint PeakJobMemoryUsed;
        }

        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW",
            SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr CreateJobObject(
            IntPtr jobAttributes,
            string? name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetInformationJobObject(
            IntPtr job,
            int informationClass,
            IntPtr information,
            uint informationLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AssignProcessToJobObject(
            IntPtr job,
            IntPtr process);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr handle);
    }
}
