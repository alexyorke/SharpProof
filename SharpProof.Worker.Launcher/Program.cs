using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Worker.Protocol;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace SharpProof.Worker.Launcher;

internal static class Program
{
    private const int TerminationCleanupReserveMilliseconds = 100;

    internal static async Task<int> Main(string[] args)
    {
        if (!LauncherArguments.TryParse(args, out var arguments))
        {
            Console.Error.WriteLine(
                "Usage: SharpProof.Worker.Launcher verify --worker <path> --request <path> --result <path> " +
                "--compiler-manifest <path> --verify-policy <policy> --assumption-policy <policy> " +
                "[--publish-request <path> --publish-result <path> --publish-compiler-manifest <path> " +
                "[--publish-sarif <path>]] [budget options]");
            return 2;
        }

        WorkerVerifyRequest request;
        CompilerManifestArtifact artifact;
        byte[] artifactBytes;
        string expectedInputHash;
        try
        {
            request = arguments.CreateRequest(out artifact, out artifactBytes);
            expectedInputHash = ComputeExpectedInputHash(arguments.WorkerPath, request, artifactBytes);
            var validation = WorkerProtocolJson.Validate(request);
            if (!validation.IsValid)
            {
                WriteErrors(validation.Errors, string.Empty);
                return 2;
            }
            await AtomicFile.WriteUtf8Async(arguments.RequestPath,
                WorkerProtocolJson.SerializeRequest(request)).ConfigureAwait(false);
            DeleteIfExists(arguments.ResultPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or FormatException or OverflowException or
                JsonException)
        {
            Console.Error.WriteLine(
                "SharpProof launcher input is invalid: " +
                exception.GetType().Name + ": " + exception.Message);
            return 2;
        }

        int exitCode;
        try
        {
            exitCode = RunWorker(arguments, request, artifact.Compilation.ProjectDirectory);
        }
        catch (Exception exception) when (ClassifyLauncherFailure(exception) is { } failure)
        {
            exitCode = failure.ExitCode;
            Console.Error.WriteLine(failure.ConsoleMessage);
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                failure.Status, failure.Reason, failure.Code, failure.Message).ConfigureAwait(false);
        }
        if (!File.Exists(arguments.ResultPath))
        {
            LauncherFailure launcherFailure =
                LauncherPresentation.NoResultFailure(exitCode);
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                launcherFailure.Status, launcherFailure.Reason, launcherFailure.Code, launcherFailure.Message).ConfigureAwait(false);
        }
        var resultExitCode = ValidateAndReport(arguments.ResultPath, request, expectedInputHash,
            artifact.Manifest, out var validResponse);
        if (!validResponse)
        {
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                WorkerRunStatus.Failed, WorkerRunFailureReason.MalformedResult, "worker.malformed_result",
                "The worker result was unavailable or malformed.").ConfigureAwait(false);
            resultExitCode = ValidateAndReport(arguments.ResultPath, request, expectedInputHash,
                artifact.Manifest, out validResponse);
        }
        if (validResponse)
        {
            try
            {
                PublishOutputs(arguments, request, artifact, artifactBytes, expectedInputHash);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine(
                    "SharpProof worker result could not be published.");
                return 3;
            }
        }
        if (exitCode == 0)
        {
            return resultExitCode;
        }

        if (validResponse && resultExitCode != 0)
        {
            return resultExitCode;
        }

        Console.Error.WriteLine("SharpProof worker failed closed with exit code " +
            exitCode.ToString(CultureInfo.InvariantCulture) + ".");
        return exitCode;
    }

    private static LauncherFailure? ClassifyLauncherFailure(Exception exception)
    {
        return exception switch
        {
            OverflowException => new(3, WorkerRunStatus.Failed, WorkerRunFailureReason.InvalidRequest,
                "launcher.timeout_overflow", "The combined project timeout and termination grace exceed the supported range.",
                "SharpProof launcher timeout is invalid."),
            PlatformNotSupportedException => new(125, WorkerRunStatus.Failed, WorkerRunFailureReason.ContainmentFailure,
                "containment.unsupported", exception.Message, exception.Message),
            InvalidOperationException or System.ComponentModel.Win32Exception => new(
                125, WorkerRunStatus.Failed, WorkerRunFailureReason.ContainmentFailure,
                "containment.unavailable", "Required worker containment could not be established.",
                "SharpProof worker containment could not be established."),
            _ => null
        };
    }

    internal sealed record LauncherFailure(
        int ExitCode, WorkerRunStatus Status, WorkerRunFailureReason Reason,
        string Code, string Message, string ConsoleMessage);

    private static int RunWorker(
        LauncherArguments arguments, WorkerVerifyRequest request,
        string projectDirectory)
    {
        var hardLimit = ComputeHardLimit(
            request.Budgets.ProjectWallTimeMilliseconds, arguments.TerminationGraceMilliseconds);
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ??
                throw new InvalidOperationException("The dotnet host path is unavailable."),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectDirectory
        };
        foreach (var argument in new[] {
                     arguments.WorkerPath, "verify",
                     "--request", arguments.RequestPath,
                     "--result", arguments.ResultPath
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var job = WindowsJob.CreateRequired(
            request.Budgets.ProcessMemoryLimitBytes, request.Budgets.MaxWorkerProcesses);
        var startEventName = "Local\\SharpProof.Worker." + Guid.NewGuid().ToString("N");
        using var startEvent = new EventWaitHandle(false, EventResetMode.ManualReset,
            startEventName);
        startInfo.ArgumentList.Add("--start-event");
        startInfo.ArgumentList.Add(startEventName);
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The SharpProof worker could not be started.");
        if (!job.TryAssign(process))
        {
            Terminate(process, entireTree: true);
            return 125;
        }
        startEvent.Set();
        if (process.WaitForExit(hardLimit))
        {
            return process.ExitCode;
        }

        Terminate(process);
        return 124;
    }

    internal static int ComputeHardLimit(
        int projectMilliseconds, int terminationGraceMilliseconds)
    {
        return checked(projectMilliseconds + Math.Max(1,
            terminationGraceMilliseconds - TerminationCleanupReserveMilliseconds));
    }

    internal static string ComputeExpectedInputHash(
        string workerPath, WorkerVerifyRequest request, byte[] artifactBytes)
    {
        var version = FileVersionInfo.GetVersionInfo(Path.GetFullPath(workerPath));
        return CompilerArtifactInputHash.Compute(
            request, artifactBytes, RequiredVersion(version.ProductName, "product name"),
            RequiredVersion(version.ProductVersion, "product version"),
            WorkerBinaryIdentity.ComputeSha256(workerPath),
            ApiSpecTable.DefaultTableIdentity, ApiSpecTable.DefaultTableVersion,
            ApiSpecTable.Default.ContentSha256);
    }

    private static string RequiredVersion(string? value, string name)
    {
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException("The worker " + name + " is unavailable.");
    }

    private static void Terminate(Process process, bool entireTree = false)
    {
        try
        {
            process.Kill(entireProcessTree: entireTree);
        }
        catch (InvalidOperationException) { }
    }

    internal static int ValidateAndReport(
        string resultPath, WorkerVerifyRequest request,
        string? expectedInputHash, WorkerClaimManifest? expectedManifest, out bool validResponse)
    {
        validResponse = false;
        WorkerVerifyResponse? response;
        try
        {
            response = WorkerProtocolJson.DeserializeResponse(
                File.ReadAllText(resultPath));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine(
                "SharpProof worker result is unavailable or malformed.");
            return 3;
        }
        var validation = expectedManifest == null || expectedInputHash == null
            ? WorkerProtocolJson.Validate(response)
            : WorkerProtocolJson.ValidateForRequest(
                response, WorkerProtocolJson.ComputeRequestHash(request),
                expectedInputHash, expectedManifest, request.Budgets);
        if (!validation.IsValid)
        {
            WriteErrors(validation.Errors, "SharpProof ");
            WriteErrors(response?.Errors ?? [], "SharpProof worker ");
            return 3;
        }
        validResponse = true;
        ArgumentNullException.ThrowIfNull(response);
        WorkerProtocolJson.Canonicalize(response);
        WriteErrors(response.Errors, "SharpProof ");

        var manifestClaims = response.Manifest.Claims.ToDictionary(static claim => claim.ClaimId, StringComparer.Ordinal);
        var refuted = response.ClaimResults.Any(static result => result.Outcome == WorkerClaimOutcome.Refuted);
        foreach (var result in response.ClaimResults)
        {
            var claim = manifestClaims[result.ClaimId];
            var reason = result.Reason == WorkerClaimReason.None ? string.Empty : " (" + result.Reason + ")";
            Console.WriteLine("SharpProof " + result.Outcome + " " + claim.CallableId + " " +
                LauncherPresentation.ClaimKind(claim) + " claim " + result.ClaimId + reason);
        }
        var incomplete = response.CallableResults
            .Where(static result => result.Coverage == WorkerCallableCoverage.Incomplete).ToArray();
        var unknownClaims = response.ClaimResults.Count(static result => result.Outcome == WorkerClaimOutcome.Unknown);
        if (incomplete.Length != 0)
        {
            ReportDiagnostic(
                response.Manifest.Callables.First(callable => callable.CallableId == incomplete[0].CallableId).Location,
                LauncherPresentation.Level(request.VerifyPolicy, "info"), "SP0047",
                FormattableString.Invariant(
                    $"Selected analysis is incomplete: callables={incomplete.Length}, unknown-claims={unknownClaims}."));
        }

        var incompleteError = incomplete.Length != 0 &&
            request.VerifyPolicy == WorkerVerifyPolicy.RequireProven;
        var assumptionError = ReportAssumptions(request.AssumptionPolicy, response);
        Console.WriteLine("SharpProof summary " + JsonSerializer.Serialize(
            new
            {
                response.RunStatus,
                response.FailureReason,
                response.Summary
            },
            WorkerProtocolJson.Options));
        if (response.RunStatus != WorkerRunStatus.Complete)
        {
            Console.Error.WriteLine("SharpProof worker run " + response.RunStatus +
                " (" + response.FailureReason + ").");
            return LauncherPresentation.ExitCode(response.RunStatus);
        }
        if (response.Errors.Length != 0)
        {
            return 3;
        }

        return refuted ? 5 : incompleteError || assumptionError ? 6 : 0;
    }
    private static bool ReportAssumptions(
        WorkerAssumptionPolicy policy, WorkerVerifyResponse response)
    {
        var assumptions = response.Summary.Assumptions;
        if (assumptions.User + assumptions.Trusted == 0)
        {
            return false;
        }

        var total = assumptions.User + assumptions.Trusted;
        ReportDiagnostic(response.Manifest.Callables[0].Location,
            LauncherPresentation.Level(policy, "info"), "SP0048",
            FormattableString.Invariant(
                $"User assumption/trusted evidence declared: total={total}, user={assumptions.User}, trusted={assumptions.Trusted}."));
        return policy == WorkerAssumptionPolicy.Error;
    }

    private static void ReportDiagnostic(
        WorkerSourceLocation location, string severity, string id, string message)
    {
        var prefix = string.IsNullOrWhiteSpace(location.Path)
            ? "SharpProof"
            : location.Path + FormattableString.Invariant(
                $"({location.Line},{location.Column})");
        var diagnostic = prefix + ": " + severity + " " + id + ": " + message;
        (severity == "info" ? Console.Out : Console.Error).WriteLine(diagnostic);
    }

    private static void PublishOutputs(
        LauncherArguments arguments, WorkerVerifyRequest request,
        CompilerManifestArtifact artifact, byte[] artifactBytes, string expectedInputHash)
    {
        if (arguments.PublishRequestPath == null)
        {
            return;
        }

        using var publication = new Mutex(false, "Local\\SharpProof.Publish");
        var ownsPublication = false;
        try
        {
            try
            {
                ownsPublication = publication.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                ownsPublication = true;
            }
            if (!ownsPublication)
            {
                throw new IOException(
                    "Timed out waiting to publish SharpProof results.");
            }

            DeleteIfExists(arguments.PublishResultPath);
            DeleteIfExists(arguments.PublishSarifPath);
            AtomicFile.WriteBytesAsync(arguments.PublishCompilerManifestPath!, artifactBytes)
                .GetAwaiter().GetResult();
            request.CompilerManifest.Path = arguments.PublishCompilerManifestPath!;
            AtomicFile.WriteUtf8(
                arguments.PublishRequestPath, WorkerProtocolJson.SerializeRequest(request));
            var response = WorkerProtocolJson.DeserializeResponse(
                File.ReadAllText(arguments.ResultPath)) ??
                throw new IOException("The worker response is missing.");
            response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
            if (!WorkerProtocolJson.ValidateForRequest(
                    response, response.RequestHash, expectedInputHash,
                    artifact.Manifest, request.Budgets).IsValid)
            {
                throw new IOException("The worker response binding is invalid.");
            }

            if (arguments.PublishSarifPath != null)
            {
                AtomicFile.WriteUtf8(
                    arguments.PublishSarifPath, SarifProjection.Serialize(request, response));
            }

            AtomicFile.WriteUtf8(
                arguments.PublishResultPath!, WorkerProtocolJson.SerializeResponse(response));
        }
        catch
        {
            DeleteIfExists(arguments.PublishResultPath);
            DeleteIfExists(arguments.PublishSarifPath);
            throw;
        }
        finally
        {
            if (ownsPublication)
            {
                publication.ReleaseMutex();
            }
        }
    }

    private static Task WriteLauncherFailureAsync(
        string path, WorkerVerifyRequest request, CompilerManifestArtifact artifact,
        string expectedInputHash, WorkerRunStatus status,
        WorkerRunFailureReason reason, string code, string message)
    {
        var timeout = status == WorkerRunStatus.TimedOut;
        var response = WorkerResultAssembler.CreateIncomplete(
            expectedInputHash,
            WorkerProtocolJson.ComputeRequestHash(request),
            artifact.Manifest, request.Budgets, status, reason,
            timeout ? WorkerCallableCoverageReason.ProjectTimeout : WorkerCallableCoverageReason.InfrastructureFailure,
            timeout ? WorkerClaimReason.ProjectTimeout : WorkerClaimReason.InfrastructureFailure,
            status == WorkerRunStatus.Failed
                ? [new WorkerProtocolError { Code = code, Message = message }]
                : [],
            new WorkerVersionSummary { WorkerVersion = "launcher", ApiSpecVersion = "unavailable" });
        return AtomicFile.WriteUtf8Async(path, WorkerProtocolJson.SerializeResponse(response));
    }

    private static void DeleteIfExists(string? path)
    {
        if (path != null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void WriteErrors(
        IEnumerable<WorkerProtocolError> errors, string prefix)
    {
        foreach (var error in errors)
        {
            Console.Error.WriteLine(prefix + error.Code + ": " + error.Message);
        }
    }
}

internal static partial class LauncherPresentation
{
    internal static string Level(WorkerVerifyPolicy policy, string advisory)
    {
        return Level((object)policy, advisory);
    }

    internal static string Level(WorkerAssumptionPolicy policy, string advisory)
    {
        return Level((object)policy, advisory);
    }

}

internal sealed class LauncherArguments
{
    private static readonly string[] s_required = [
        "worker", "request", "result", "compiler-manifest", "verify-policy", "assumption-policy"
    ];
    private static readonly string[] s_publication = ["publish-request", "publish-result", "publish-compiler-manifest"];
    private static readonly HashSet<string> s_allowed = [
        .. s_required, .. s_publication, "publish-sarif", "termination-grace-ms",
        "query-rlimit", "method-rlimit", "method-wall-ms", "project-wall-ms",
        "max-parallelism", "max-expression-depth", "process-memory-bytes", "max-worker-processes",
        "cache-enabled", "cache-directory", "cache-maximum-bytes"
    ];
    private readonly IReadOnlyDictionary<string, string> _values;

    private LauncherArguments(IReadOnlyDictionary<string, string> values)
    {
        _values = values;
    }

    internal string WorkerPath => FullPath("worker");
    internal string RequestPath => FullPath("request");
    internal string ResultPath => FullPath("result");
    internal string CompilerManifestPath => FullPath("compiler-manifest");
    internal string? PublishRequestPath => OptionalFullPath("publish-request");
    internal string? PublishResultPath => OptionalFullPath("publish-result");
    internal string? PublishCompilerManifestPath => OptionalFullPath("publish-compiler-manifest");
    internal string? PublishSarifPath => OptionalFullPath("publish-sarif");
    internal int TerminationGraceMilliseconds => Number("termination-grace-ms", WorkerLauncherDefaults.TerminationGraceMilliseconds);

    internal static bool TryParse(string[] args, out LauncherArguments arguments)
    {
        arguments = null!;
        if (args.Length < 3 || !string.Equals(args[0], "verify", StringComparison.Ordinal) || args.Length % 2 == 0)
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }

            key = key.Substring(2);
            if (!s_allowed.Contains(key) || !values.TryAdd(key, args[index + 1]))
            {
                return false;
            }
        }
        if (s_required.Any(key => !values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
        {
            return false;
        }

        var publicationCount = s_publication.Count(key => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value));
        if (publicationCount is not (0 or 3) ||
            values.TryGetValue("publish-sarif", out var sarif) &&
            (string.IsNullOrWhiteSpace(sarif) || publicationCount != 3))
        {
            return false;
        }

        arguments = new LauncherArguments(values);
        return true;
    }

    internal WorkerVerifyRequest CreateRequest(
        out CompilerManifestArtifact artifact, out byte[] artifactBytes)
    {
        ValidateDistinctPaths();
        return new WorkerVerifyRequest
        {
            CompilerManifest = CreateCompilerManifestReference(out artifact, out artifactBytes),
            VerifyPolicy = LauncherPresentation.ParseVerifyPolicy(Required("verify-policy")),
            AssumptionPolicy = LauncherPresentation.ParseAssumptionPolicy(Required("assumption-policy")),
            Budgets = CreateBudgets(),
            Cache = CreateCache()
        };
    }

    private void ValidateDistinctPaths()
    {
        string?[] candidates = [RequestPath, ResultPath, CompilerManifestPath,
            PublishRequestPath, PublishResultPath, PublishCompilerManifestPath,
            PublishSarifPath];
        var paths = candidates.OfType<string>().ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
        {
            throw new ArgumentException("SharpProof I/O paths must be distinct.");
        }
    }

    private WorkerFileReference CreateCompilerManifestReference(
        out CompilerManifestArtifact artifact, out byte[] bytes)
    {
        var path = FullPath("compiler-manifest");
        bytes = File.ReadAllBytes(path);
        artifact = CompilerManifestArtifactJson.Deserialize(new UTF8Encoding(false, true).GetString(bytes));
        return new WorkerFileReference { Path = path, Sha256 = WorkerProtocolJson.ComputeSha256(bytes) };
    }

    private WorkerBudgets CreateBudgets()
    {
        return new()
        {
            QueryRlimit = Number("query-rlimit", WorkerBudgets.DefaultQueryRlimit),
            MethodRlimit = Number("method-rlimit", WorkerBudgets.DefaultMethodRlimit),
            MethodWallTimeMilliseconds = Number("method-wall-ms", WorkerBudgets.DefaultMethodWallTimeMilliseconds),
            ProjectWallTimeMilliseconds = Number("project-wall-ms", WorkerBudgets.DefaultProjectWallTimeMilliseconds),
            MaxParallelism = Number("max-parallelism", WorkerBudgets.MaximumParallelism),
            MaximumExpressionDepth = Number("max-expression-depth", WorkerBudgets.DefaultMaximumExpressionDepth),
            ProcessMemoryLimitBytes = Number("process-memory-bytes", WorkerBudgets.DefaultProcessMemoryLimitBytes),
            MaxWorkerProcesses = Number("max-worker-processes", WorkerBudgets.MaximumParallelism)
        };
    }

    private WorkerCacheOptions CreateCache()
    {
        return new()
        {
            Enabled = Boolean("cache-enabled", true),
            Directory = Optional("cache-directory"),
            MaximumBytes = Number("cache-maximum-bytes", WorkerCacheOptions.DefaultMaximumBytes)
        };
    }

    private string FullPath(string key)
    {
        return Path.GetFullPath(Required(key));
    }

    private string? OptionalFullPath(string key)
    {
        return Optional(key) is { } value ? Path.GetFullPath(value) : null;
    }

    private string Required(string key)
    {
        return _values.TryGetValue(key, out var value) ? value :
        throw new ArgumentException("A required launcher argument is missing.", key);
    }

    private string? Optional(string key)
    {
        return _values.TryGetValue(key, out var value) &&
        !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private T Number<T>(string key, T fallback) where T : struct, INumberBase<T>
    {
        return _values.TryGetValue(key, out var value) ? T.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) : fallback;
    }

    private bool Boolean(string key, bool? fallback = null)
    {
        return _values.TryGetValue(key, out var value)
        ? bool.Parse(value) : fallback ?? bool.Parse(Required(key));
    }
}

internal sealed partial class WindowsJob : IDisposable
{
    private const NativeMethods.JobObjectLimitFlags RequiredLimitFlags = NativeMethods.JobObjectLimitFlags.KillOnJobClose |
        NativeMethods.JobObjectLimitFlags.JobMemory | NativeMethods.JobObjectLimitFlags.ActiveProcess;
    private IntPtr _handle;

    private WindowsJob(IntPtr handle)
    {
        _handle = handle;
    }

    internal static WindowsJob CreateRequired(
        long memoryLimitBytes, int activeProcessLimit)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("The SharpProof verifier requires Windows x64.");
        }

        var handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("A SharpProof worker Job Object could not be created.");
        }

        var job = new WindowsJob(handle);
        var information = new NativeMethods.JobObjectExtendedLimitInformation();
        information.LimitFlags = RequiredLimitFlags;
        information.ActiveProcessLimit = checked((uint)activeProcessLimit);
        information.JobMemoryLimit = checked((nuint)memoryLimitBytes);
        var size = checked((uint)Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>());
        if (NativeMethods.SetInformationJobObject(handle, 9, ref information, size))
        {
            return job;
        }

        job.Dispose();
        throw new InvalidOperationException("The SharpProof worker Job Object could not be configured.");
    }

    internal bool TryAssign(Process process)
    {
        return _handle != IntPtr.Zero && NativeMethods.AssignProcessToJobObject(_handle, process.Handle);
    }

    internal static bool KillsProcessesOnDispose =>
        (RequiredLimitFlags & NativeMethods.JobObjectLimitFlags.KillOnJobClose) != 0;

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static partial class NativeMethods
    {
        [Flags]
        internal enum JobObjectLimitFlags : uint
        {
            ActiveProcess = 0x00000008, JobMemory = 0x00000200,
            KillOnJobClose = 0x00002000
        }

        // JOB_OBJECT_EXTENDED_LIMIT_INFORMATION is 144 bytes on the required x64
        // host. Only the three fields configured by the launcher need names.
        [StructLayout(LayoutKind.Explicit, Size = 144)]
        internal struct JobObjectExtendedLimitInformation
        {
            [FieldOffset(16)] internal JobObjectLimitFlags LimitFlags;
            [FieldOffset(40)] internal uint ActiveProcessLimit;
            [FieldOffset(120)] internal nuint JobMemoryLimit;
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
