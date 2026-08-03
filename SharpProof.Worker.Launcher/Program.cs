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
        WorkerRuntimeClosureSnapshot? runtimeSnapshot = null;
        try
        {
            arguments.ValidatePreflight();
            arguments.ValidateDistinctPaths(runtimeSnapshot);
            runtimeSnapshot = WorkerBinaryIdentity.CreateSnapshot(
                arguments.WorkerPath);
            request = arguments.CreateRequest(
                runtimeSnapshot, out artifact, out artifactBytes);
            expectedInputHash = ComputeExpectedInputHash(
                request,
                artifactBytes,
                runtimeSnapshot);
            var validation = WorkerProtocolJson.Validate(request);
            if (!validation.IsValid)
            {
                runtimeSnapshot.Dispose();
                runtimeSnapshot = null;
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
                InvalidDataException or JsonException or KeyNotFoundException or
                InvalidOperationException)
        {
            runtimeSnapshot?.Dispose();
            runtimeSnapshot = null;
            Console.Error.WriteLine(
                "SharpProof launcher input is invalid: " +
                exception.GetType().Name + ": " + exception.Message);
            return 2;
        }

        int exitCode;
        try
        {
            using (runtimeSnapshot)
            {
                if (WorkerBinaryIdentity.ComputeSha256(
                        runtimeSnapshot.ExecutionWorkerPath) !=
                    runtimeSnapshot.Sha256)
                {
                    throw new InvalidOperationException(
                        "The staged worker runtime closure changed before launch.");
                }

                exitCode = RunWorker(
                    arguments,
                    request,
                    artifact.Compilation.ProjectDirectory,
                    runtimeSnapshot.ExecutionWorkerPath);
            }
        }
        catch (Exception exception) when (ClassifyLauncherFailure(exception) is { } failure)
        {
            exitCode = failure.ExitCode;
            Console.Error.WriteLine(failure.ConsoleMessage);
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                failure.Status, failure.Reason, failure.Code, failure.Message).ConfigureAwait(false);
        }
        if (exitCode == 124)
        {
            DeleteIfExists(arguments.ResultPath);
        }
        if (!File.Exists(arguments.ResultPath))
        {
            LauncherFailure launcherFailure =
                LauncherPresentation.NoResultFailure(exitCode);
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                launcherFailure.Status, launcherFailure.Reason, launcherFailure.Code, launcherFailure.Message).ConfigureAwait(false);
        }
        var resultExitCode = ValidateAndReport(arguments.ResultPath, request, expectedInputHash,
            artifact.Manifest, out var validResponse, out var validatedResponse);
        if (!validResponse)
        {
            await WriteLauncherFailureAsync(arguments.ResultPath, request, artifact, expectedInputHash,
                WorkerRunStatus.Failed, WorkerRunFailureReason.MalformedResult, "worker.malformed_result",
                "The worker result was unavailable or malformed.").ConfigureAwait(false);
            resultExitCode = ValidateAndReport(arguments.ResultPath, request, expectedInputHash,
                artifact.Manifest, out validResponse, out validatedResponse);
        }
        if (validResponse)
        {
            try
            {
                PublishOutputs(arguments, request, artifact, artifactBytes, expectedInputHash,
                    validatedResponse!);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or
                    UnauthorizedAccessException or ArgumentException)
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

        if (validResponse & resultExitCode != 0)
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
        string projectDirectory, string workerPath)
    {
        var hardLimit = ComputeHardLimit(
            request.Budgets.ProjectWallTimeMilliseconds, arguments.TerminationGraceMilliseconds);
        using var job = WindowsJob.CreateRequired(
            request.Budgets.ProcessMemoryLimitBytes, request.Budgets.MaxWorkerProcesses);
        var startEventName = "Local\\SharpProof.Worker." + Guid.NewGuid().ToString("N");
        using var startEvent = new EventWaitHandle(false, EventResetMode.ManualReset,
            startEventName);
        using var process = job.StartSuspended(
            ResolveDotNetHostPath(projectDirectory),
            [workerPath, "verify", "--request", arguments.RequestPath,
                "--result", arguments.ResultPath, "--start-event", startEventName],
            projectDirectory);
        process.Resume();
        startEvent.Set();
        if (process.WaitForExit(hardLimit))
        {
            return process.ExitCode;
        }

        job.Terminate(124);
        if (!SpinWait.SpinUntil(
                job.HasNoActiveProcesses,
                arguments.TerminationGraceMilliseconds))
        {
            throw new InvalidOperationException(
                "The SharpProof worker job did not terminate within its grace period.");
        }
        return 124;
    }

    internal static string ResolveDotNetHostPath(string projectDirectory)
    {
        return ValidateDotNetHostPath(Environment.ProcessPath ??
            throw new InvalidOperationException(
                "The dotnet host path is unavailable."), projectDirectory);
    }

    internal static string ValidateDotNetHostPath(
        string candidate, string projectDirectory)
    {
        var hostPath = Path.GetFullPath(candidate);
        var hostRoot = Path.GetDirectoryName(hostPath) ?? string.Empty;
        var projectRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory)) + Path.DirectorySeparatorChar;
        var hostFileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        if (!Path.IsPathFullyQualified(candidate) |
            !string.Equals(Path.GetFileName(hostPath), hostFileName,
                StringComparison.OrdinalIgnoreCase) |
            !File.Exists(hostPath) |
            !Directory.Exists(Path.Combine(hostRoot, "host", "fxr")) |
            hostPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The current process is not hosted by a trusted absolute .NET installation.");
        }

        return hostPath;
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
        using var snapshot = WorkerBinaryIdentity.CreateSnapshot(workerPath);
        return ComputeExpectedInputHash(
            request,
            artifactBytes,
            snapshot);
    }

    internal static string ComputeExpectedInputHash(
        WorkerVerifyRequest request,
        byte[] artifactBytes,
        WorkerRuntimeClosureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var version = FileVersionInfo.GetVersionInfo(snapshot.ExecutionWorkerPath);
        return CompilerArtifactInputHash.Compute(
            request, artifactBytes, RequiredVersion(version.ProductName, "product name"),
            RequiredVersion(version.ProductVersion, "product version"),
            snapshot.Sha256,
            ApiSpecTable.DefaultTableIdentity, ApiSpecTable.DefaultTableVersion,
            ApiSpecTable.Default.ContentSha256);
    }

    private static string RequiredVersion(string? value, string name)
    {
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException("The worker " + name + " is unavailable.");
    }

    internal static int ValidateAndReport(
        string resultPath, WorkerVerifyRequest request,
        string? expectedInputHash, WorkerClaimManifest? expectedManifest,
        out bool validResponse, out WorkerVerifyResponse? validatedResponse)
    {
        validResponse = false;
        validatedResponse = null;
        WorkerVerifyResponse? response;
        try
        {
            response = WorkerProtocolJson.DeserializeResponse(
                WorkerProtocolJson.ReadUtf8File(resultPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException)
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
        validatedResponse = response;
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

        var incompleteError = incomplete.Length != 0 &
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

        return refuted ? 5 : incompleteError | assumptionError ? 6 : 0;
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
        CompilerManifestArtifact artifact, byte[] artifactBytes, string expectedInputHash,
        WorkerVerifyResponse response)
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

internal sealed partial class LauncherArguments
{
    internal const int MaximumCompilerManifestBytes =
        CompilerManifestArtifactFile.MaximumBytes;

    private readonly IReadOnlyDictionary<string, string> _values;

    private LauncherArguments(IReadOnlyDictionary<string, string> values)
    {
        _values = values;
    }

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
            if (!s_allowed.Contains(key) | !values.TryAdd(key, args[index + 1]))
            {
                return false;
            }
        }
        if (s_required.Any(key => !values.TryGetValue(key, out var value) | string.IsNullOrWhiteSpace(value)))
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
        return CreateRequest(null, out artifact, out artifactBytes);
    }

    internal WorkerVerifyRequest CreateRequest(
        WorkerRuntimeClosureSnapshot? runtimeSnapshot,
        out CompilerManifestArtifact artifact, out byte[] artifactBytes)
    {
        ValidateDistinctPaths(runtimeSnapshot, Optional("cache-directory"));
        var compilerManifest = CreateCompilerManifestReference(
            out artifact,
            out artifactBytes);
        var request = ProjectRequest(compilerManifest);
        ValidateDistinctPaths(
            runtimeSnapshot,
            WorkerCachePath.Resolve(
                Optional("cache-directory"),
                artifact.Compilation.ProjectDirectory));
        return request;
    }

    internal void ValidateDistinctPaths(
        WorkerRuntimeClosureSnapshot? runtimeSnapshot,
        string? cacheDirectory = null)
    {
        var workerPath = WorkerPath;
        var runtimeRoots = new[] {
            workerPath,
            Path.ChangeExtension(workerPath, ".deps.json"),
            Path.ChangeExtension(workerPath, ".runtimeconfig.json")
        };
        string?[] candidates = [..runtimeRoots,
            ..LauncherArguments.LauncherRuntimePaths,
            cacheDirectory, RequestPath, ResultPath, CompilerManifestPath,
            PublishRequestPath, PublishResultPath, PublishCompilerManifestPath,
            PublishSarifPath];
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (candidates.OfType<string>().Any(path => !paths.Add(path)) ||
            runtimeSnapshot?.ComponentPaths.Any(path =>
                !runtimeRoots.Contains(path, StringComparer.OrdinalIgnoreCase) &&
                !paths.Add(path)) is true)
        {
            throw new ArgumentException("SharpProof I/O paths must be distinct.");
        }
    }

    internal void ValidatePreflight()
    {
        var graceMilliseconds = TerminationGraceMilliseconds;
        ArgumentOutOfRangeException.ThrowIfLessThan(
            graceMilliseconds, 1, "termination-grace-ms");
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            graceMilliseconds,
            WorkerLauncherDefaults.MaximumTerminationGraceMilliseconds,
            "termination-grace-ms");
    }

    private WorkerFileReference CreateCompilerManifestReference(
        out CompilerManifestArtifact artifact, out byte[] bytes)
    {
        var path = FullPath("compiler-manifest");
        bytes = ReadCompilerManifest(path);
        artifact = CompilerManifestArtifactJson.Deserialize(new UTF8Encoding(false, true).GetString(bytes));
        return new WorkerFileReference { Path = path, Sha256 = WorkerProtocolJson.ComputeSha256(bytes) };
    }

    internal static byte[] ReadCompilerManifest(string path)
    {
        return CompilerManifestArtifactFile.ReadAllBytes(path);
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
        if (!OperatingSystem.IsWindows() | RuntimeInformation.ProcessArchitecture != Architecture.X64 |
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

    internal unsafe SuspendedProcess StartSuspended(
        string applicationPath, IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var startupInfo = new NativeMethods.StartupInfo
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.StartupInfo>())
        };
        var commandLine = new StringBuilder(QuoteCommandLineArgument(applicationPath));
        foreach (var argument in arguments)
        {
            commandLine.Append(' ').Append(QuoteCommandLineArgument(argument));
        }

        var commandLineCharacters = (commandLine.ToString() + '\0').ToCharArray();
        NativeMethods.ProcessInformation processInformation;
        fixed (char* commandLinePointer = commandLineCharacters)
        {
            if (!NativeMethods.CreateProcess(
                    applicationPath, commandLinePointer, IntPtr.Zero, IntPtr.Zero,
                    inheritHandles: false,
                    NativeMethods.CreateSuspended | NativeMethods.CreateNoWindow,
                    IntPtr.Zero, workingDirectory,
                    &startupInfo, &processInformation))
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The SharpProof worker could not be created suspended.");
            }
        }

        if (_handle == IntPtr.Zero ||
            !NativeMethods.AssignProcessToJobObject(
                _handle, processInformation.Process))
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.TerminateProcess(processInformation.Process, 125);
            NativeMethods.CloseHandle(processInformation.Thread);
            NativeMethods.CloseHandle(processInformation.Process);
            throw new System.ComponentModel.Win32Exception(
                error,
                "The SharpProof worker could not be assigned to its Job Object.");
        }

        return new SuspendedProcess(
            processInformation.Process, processInformation.Thread);
    }

    internal void Terminate(uint exitCode)
    {
        if (_handle == IntPtr.Zero |
            !NativeMethods.TerminateJobObject(_handle, exitCode))
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "The SharpProof worker Job Object could not be terminated.");
        }
    }

    internal bool HasNoActiveProcesses()
    {
        var information = new NativeMethods.JobObjectBasicAccountingInformation();
        var size = checked((uint)Marshal.SizeOf<
            NativeMethods.JobObjectBasicAccountingInformation>());
        if (_handle == IntPtr.Zero |
            !NativeMethods.QueryInformationJobObject(
                _handle, 1, ref information, size, IntPtr.Zero))
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "The SharpProof worker Job Object state is unavailable.");
        }

        return information.ActiveProcesses == 0;
    }

    private static string QuoteCommandLineArgument(string argument)
    {
        return "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
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

    internal static partial class NativeMethods
    {
        internal const uint CreateSuspended = 0x00000004;
        internal const uint CreateNoWindow = 0x08000000;
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

        [StructLayout(LayoutKind.Explicit, Size = 48)]
        internal struct JobObjectBasicAccountingInformation
        {
            [FieldOffset(40)] internal uint ActiveProcesses;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct StartupInfo
        {
            internal uint Size;
            internal IntPtr Reserved;
            internal IntPtr Desktop;
            internal IntPtr Title;
            internal uint X;
            internal uint Y;
            internal uint XSize;
            internal uint YSize;
            internal uint XCountChars;
            internal uint YCountChars;
            internal uint FillAttribute;
            internal uint Flags;
            internal ushort ShowWindow;
            internal ushort ReservedBytes;
            internal IntPtr ReservedPointer;
            internal IntPtr StandardInput;
            internal IntPtr StandardOutput;
            internal IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation
        {
            internal IntPtr Process;
            internal IntPtr Thread;
            internal uint ProcessId;
            internal uint ThreadId;
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
        internal static partial bool QueryInformationJobObject(
            IntPtr job, int informationClass,
            ref JobObjectBasicAccountingInformation information,
            uint informationLength, IntPtr returnLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW",
            SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static unsafe partial bool CreateProcess(
            string applicationName, char* commandLine,
            IntPtr processAttributes, IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags, IntPtr environment, string currentDirectory,
            StartupInfo* startupInfo, ProcessInformation* processInformation);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial uint ResumeThread(IntPtr thread);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TerminateProcess(IntPtr process, uint exitCode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TerminateJobObject(IntPtr job, uint exitCode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr handle);
    }
}

internal sealed class SuspendedProcess : IDisposable
{
    private IntPtr _process;
    private IntPtr _thread;

    internal SuspendedProcess(IntPtr process, IntPtr thread)
    {
        _process = process;
        _thread = thread;
    }

    internal int ExitCode
    {
        get
        {
            if (_process == IntPtr.Zero |
                !WindowsJob.NativeMethods.GetExitCodeProcess(
                    _process, out var exitCode))
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The SharpProof worker exit code is unavailable.");
            }

            return unchecked((int)exitCode);
        }
    }

    internal void Resume()
    {
        if (_thread == IntPtr.Zero |
            WindowsJob.NativeMethods.ResumeThread(_thread) == uint.MaxValue)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "The SharpProof worker could not be resumed.");
        }
    }

    internal bool WaitForExit(int milliseconds)
    {
        var result = WindowsJob.NativeMethods.WaitForSingleObject(
            _process, checked((uint)milliseconds));
        return result switch
        {
            0 => true,
            258 => false,
            _ => throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "Waiting for the SharpProof worker failed.")
        };
    }

    public void Dispose()
    {
        var thread = Interlocked.Exchange(ref _thread, IntPtr.Zero);
        var process = Interlocked.Exchange(ref _process, IntPtr.Zero);
        WindowsJob.NativeMethods.CloseHandle(thread);
        WindowsJob.NativeMethods.CloseHandle(process);
    }
}
