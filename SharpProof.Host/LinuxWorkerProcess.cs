using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using WorkerInvocationArguments = SharpProof.Host.Invocation.WorkerInvocationArguments;

namespace SharpProof.Host;

public enum LinuxWorkerCompletionKind
{
    Exited,
    TimedOut
}

public sealed record LinuxWorkerCompletion(
    LinuxWorkerCompletionKind Kind,
    int ExitCode);

public sealed partial class LinuxWorkerProcess : IDisposable
{
    public const string StartMessage = "SharpProof.Start/1";
    internal const string ArmedMessage = "SharpProof.Armed/1";
    internal const string CleanupMessage = "SharpProof.Cleanup/1";
    private const int PollMilliseconds = 25;
    private Process? _process;
    private long _terminationDeadlineTimestamp;

    private LinuxWorkerProcess(Process process)
    {
        _process = process;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1062",
        Justification = "The standard ThrowIfNull guard validates the collection before enumeration.")]
    public static LinuxWorkerProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        return StartCore(
            executable,
            arguments,
            workingDirectory,
            validatedDotNetHostPath: null,
            Environment.GetEnvironmentVariable);
    }

    internal static LinuxWorkerProcess StartDotNet(
        string validatedDotNetHostPath,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        return StartCore(
            validatedDotNetHostPath,
            arguments,
            workingDirectory,
            validatedDotNetHostPath,
            Environment.GetEnvironmentVariable);
    }

    internal static LinuxWorkerProcess StartWithEnvironment(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> inheritedEnvironment)
    {
        ArgumentNullException.ThrowIfNull(inheritedEnvironment);
        return StartCore(
            executable,
            arguments,
            workingDirectory,
            validatedDotNetHostPath: null,
            name => inheritedEnvironment.TryGetValue(name, out var value)
                ? value
                : null);
    }

    private static LinuxWorkerProcess StartCore(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? validatedDotNetHostPath,
        Func<string, string?> readEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        EnsureLinux();

        const string sessionLauncher = "/usr/bin/setsid";
        if (!File.Exists(sessionLauncher))
        {
            throw new PlatformNotSupportedException(
                "SharpProof worker containment requires /usr/bin/setsid from the canonical container.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = sessionLauncher,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        TrustedChildEnvironment.Apply(
            startInfo,
            validatedDotNetHostPath,
            readEnvironmentVariable);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(executable);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add(WorkerInvocationArguments.ParentPidOption);
        startInfo.ArgumentList.Add(
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException(
            "The SharpProof worker process could not be started.");
        var ownershipTransferred = false;
        try
        {
            try
            {
                process.StandardInput.WriteLine(StartMessage);
                process.StandardInput.Close();
            }
            catch (IOException) when (process.WaitForExit(0))
            {
                // A short-lived process can finish before the startup frame
                // reaches its stdin pipe. Preserve the process so callers can
                // observe its exit code instead of turning that race into a
                // host-side startup failure.
            }
            var worker = new LinuxWorkerProcess(process);
            ownershipTransferred = true;
            return worker;
        }
        finally
        {
            if (!ownershipTransferred)
            {
                TerminateNow(process);
                process.Dispose();
            }
        }
    }

    public LinuxWorkerCompletion WaitForExit(
        TimeSpan terminationStart,
        TimeSpan finalLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            terminationStart,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            finalLimit,
            terminationStart);
        var process = _process ?? throw new ObjectDisposedException(
            nameof(LinuxWorkerProcess));
        var stopwatch = Stopwatch.StartNew();
        Interlocked.Exchange(
            ref _terminationDeadlineTimestamp,
            Stopwatch.GetTimestamp() +
                (long)(finalLimit.TotalSeconds * Stopwatch.Frequency));
        while (!process.WaitForExit(0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= terminationStart)
            {
                return CompleteAtDeadline(process, stopwatch, finalLimit, ownsSession: true);
            }
            if (cancellationToken.WaitHandle.WaitOne(PollMilliseconds))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
        return new LinuxWorkerCompletion(
            LinuxWorkerCompletionKind.Exited,
            process.ExitCode);
    }

    public static void EnterChildBoundaryRequired(int expectedParentProcessId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            expectedParentProcessId,
            1);
        EnsureLinux();
        if (LinuxPrctl.ControlProcess(
                LinuxProcessControlConstants.ParentDeathSignal,
                LinuxProcessControlConstants.SignalKill,
                0,
                0,
                0) != 0)
        {
            throw NativeFailure(
                "SharpProof could not install the worker parent-death boundary.");
        }
        if (NativeMethods.GetParentProcessId() != expectedParentProcessId)
        {
            throw new InvalidOperationException(
                "The SharpProof launcher exited before worker startup completed.");
        }
    }

    public void Dispose()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process == null)
        {
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                var stopwatch = Stopwatch.StartNew();
                var deadline = Interlocked.Read(ref _terminationDeadlineTimestamp);
                var remaining = deadline == 0
                    ? TimeSpan.FromSeconds(1)
                    : TimeSpan.FromSeconds(Math.Max(
                        0,
                        (deadline - Stopwatch.GetTimestamp()) /
                            (double)Stopwatch.Frequency));
                _ = Terminate(process, stopwatch, remaining, ownsSession: true);
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    internal static LinuxWorkerCompletion CompleteAtDeadline(
        Process process,
        Stopwatch stopwatch,
        TimeSpan finalLimit)
    {
        return CompleteAtDeadline(process, stopwatch, finalLimit, ownsSession: false);
    }

    private static LinuxWorkerCompletion CompleteAtDeadline(
        Process process,
        Stopwatch stopwatch,
        TimeSpan finalLimit,
        bool ownsSession)
    {
        return Terminate(process, stopwatch, finalLimit, ownsSession)
            ? new LinuxWorkerCompletion(
                LinuxWorkerCompletionKind.TimedOut,
                LinuxProcessControlConstants.TimeoutExitCode)
            : new LinuxWorkerCompletion(
                LinuxWorkerCompletionKind.Exited,
                process.ExitCode);
    }

    private static bool Terminate(
        Process process,
        Stopwatch stopwatch,
        TimeSpan finalLimit,
        bool ownsSession)
    {
        if (process.HasExited)
        {
            return false;
        }
        (int Id, ulong StartTime)? session = null;
        if (ownsSession && TryReadProcessStat(process.Id, out var rootStat) &&
            rootStat.ProcessGroupId == process.Id && rootStat.SessionId == process.Id &&
            rootStat.StartTime is { } rootStartTime)
        {
            session = (process.Id, rootStartTime);
        }
        var descendants = CaptureDescendants(process.Id, session);
        if (LinuxProcessControl.Kill(
                process.Id,
                LinuxProcessControlConstants.SignalTerminate) != 0)
        {
            if (Marshal.GetLastPInvokeError() ==
                    LinuxProcessControlConstants.ProcessNotFound &&
                process.WaitForExit(0))
            {
                AddDescendants(process.Id, descendants, session);
                KillCapturedDescendants(descendants);
                return false;
            }
            throw NativeFailure(
                "SharpProof could not terminate the worker process.");
        }
        // The owned session survives leader exit, so children created by a
        // SIGTERM handler remain discoverable even after they are reparented.
        var remainingForTerminate = finalLimit - stopwatch.Elapsed;
        var terminateWait = checked((int)Math.Min(
            Math.Max(0, remainingForTerminate.TotalMilliseconds / 2),
            int.MaxValue));
        var terminateDeadline = stopwatch.Elapsed +
            TimeSpan.FromMilliseconds(terminateWait);
        while (!process.WaitForExit(0) &&
            stopwatch.Elapsed < terminateDeadline)
        {
            AddDescendants(
                process.Id,
                descendants, session);
            var remaining = terminateDeadline - stopwatch.Elapsed;
            var delay = remaining < TimeSpan.FromMilliseconds(PollMilliseconds)
                ? remaining
                : TimeSpan.FromMilliseconds(PollMilliseconds);
            if (delay > TimeSpan.Zero)
            {
                Thread.Sleep(delay);
            }
        }
        AddDescendants(process.Id, descendants, session);
        if (!process.WaitForExit(0))
        {
            try
            {
                if (!process.HasExited)
                {
                    AddDescendants(process.Id, descendants, session);
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.WaitForExit(0))
            {
            }
            var remaining = finalLimit - stopwatch.Elapsed;
            var killWait = checked((int)Math.Min(
                Math.Max(0, remaining.TotalMilliseconds),
                int.MaxValue));
            if (!process.WaitForExit(killWait))
            {
                throw new InvalidOperationException(
                    "The SharpProof worker did not terminate within its grace period.");
            }
            AddDescendants(process.Id, descendants, session);
        }
        KillCapturedDescendants(descendants);
        return true;
    }

    private static void AddDescendants(
        int rootProcessId,
        List<(int ProcessId, ulong StartTime)> descendants,
        (int Id, ulong StartTime)? session)
    {
        foreach (var descendant in CaptureDescendants(rootProcessId, session))
        {
            if (!descendants.Contains(descendant))
            {
                descendants.Add(descendant);
            }
        }
    }

    private static List<(int ProcessId, ulong StartTime)> CaptureDescendants(
        int rootProcessId,
        (int Id, ulong StartTime)? session)
    {
        var descendants = new List<(int ProcessId, ulong StartTime)>();
        var childrenByParent =
            new Dictionary<int, List<(int ProcessId, ulong StartTime)>>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var processId) ||
                !TryReadProcessStat(processId, out var stat) ||
                stat.StartTime is not { } startTime)
            {
                continue;
            }
            if (processId != rootProcessId && session is { } owned &&
                stat.SessionId == owned.Id && startTime >= owned.StartTime)
            {
                descendants.Add((processId, startTime));
            }
            if (!childrenByParent.TryGetValue(stat.ParentProcessId, out var children))
            {
                children = [];
                childrenByParent.Add(stat.ParentProcessId, children);
            }
            children.Add((processId, startTime));
        }

        var pending = new Queue<int>([rootProcessId]);
        while (pending.TryDequeue(out var parentId))
        {
            if (!childrenByParent.TryGetValue(parentId, out var children))
            {
                continue;
            }
            foreach (var child in children)
            {
                if (!descendants.Contains(child))
                {
                    descendants.Add(child);
                }
                pending.Enqueue(child.ProcessId);
            }
        }
        return descendants;
    }

    private static void KillCapturedDescendants(
        IReadOnlyList<(int ProcessId, ulong StartTime)> descendants)
    {
        foreach (var (processId, startTime) in descendants)
        {
            if (!TryReadProcessStat(processId, out _, out var currentStartTime) ||
                currentStartTime != startTime)
            {
                continue;
            }
            if (LinuxProcessControl.Kill(
                    processId,
                    LinuxProcessControlConstants.SignalKill) != 0 &&
                Marshal.GetLastPInvokeError() !=
                    LinuxProcessControlConstants.ProcessNotFound)
            {
                throw NativeFailure(
                    "SharpProof could not terminate a worker descendant.");
            }
        }
    }

    private static bool TryReadProcessStat(
        int processId,
        out int parentId,
        out ulong startTime)
    {
        parentId = 0;
        startTime = 0;
        if (!TryReadProcessStat(processId, out var stat) || stat.StartTime is not { } parsedStartTime)
        {
            return false;
        }
        parentId = stat.ParentProcessId;
        startTime = parsedStartTime;
        return true;
    }

    private static bool TryReadProcessStat(int processId, out LinuxProcessStat processStat)
    {
        processStat = default;
        try
        {
            var stat = File.ReadAllText($"/proc/{processId}/stat");
            return LinuxProcessStatParser.TryParse(stat, out processStat);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TerminateNow(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static InvalidOperationException NativeFailure(string message)
    {
        return new InvalidOperationException(
            $"{message} (errno {Marshal.GetLastPInvokeError()}).");
    }

    private static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "SharpProof worker containment requires Linux amd64.");
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("libc", EntryPoint = "getppid")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int GetParentProcessId();

    }
}

internal static class TrustedChildEnvironment
{
    private static readonly string[] AllowedVariables =
    [
        "PATH",
        "HOME",
        "TMPDIR",
        "SHARPPROOF_CONTAINER",
        "SHARPPROOF_CONTAINER_CONTRACT",
        "SHARPPROOF_NATIVE_ROOT",
        "DOTNET_CLI_TELEMETRY_OPTOUT"
    ];

    private static readonly string[] UnsafeRuntimeVariables =
    [
        "DOTNET_STARTUP_HOOKS",
        "DOTNET_ADDITIONAL_DEPS",
        "DOTNET_SHARED_STORE",
        "DOTNET_ROLL_FORWARD",
        "DOTNET_ROLL_FORWARD_TO_PRERELEASE",
        "DOTNET_ROOT_X64",
        "DOTNET_ROOT_X86",
        "DOTNET_MULTILEVEL_LOOKUP",
        "LD_PRELOAD",
        "LD_LIBRARY_PATH",
        "LD_AUDIT"
    ];

    internal static void Apply(
        ProcessStartInfo startInfo,
        string? validatedDotNetHostPath,
        Func<string, string?>? readEnvironmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        readEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var inheritedValues = AllowedVariables
            .Select(name => (Name: name, Value: readEnvironmentVariable(name)))
            .Where(static item => item.Value is not null)
            .ToArray();
        string? dotNetRoot = null;
        if (validatedDotNetHostPath is not null)
        {
            if (!Path.IsPathFullyQualified(validatedDotNetHostPath) ||
                !string.Equals(
                    Path.GetFileName(validatedDotNetHostPath),
                    "dotnet",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SharpProof child runtime must use a validated absolute dotnet muxer.");
            }
            dotNetRoot = Path.GetDirectoryName(validatedDotNetHostPath);
            if (string.IsNullOrWhiteSpace(dotNetRoot))
            {
                throw new InvalidOperationException(
                    "SharpProof child runtime has no validated installation root.");
            }
        }

        startInfo.Environment.Clear();
        foreach (var item in inheritedValues)
        {
            startInfo.Environment[item.Name] = item.Value;
        }
        if (dotNetRoot is not null)
        {
            startInfo.Environment["DOTNET_ROOT"] = dotNetRoot;
        }
    }

    internal static string? FindUnsafeRuntimeVariable(
        Func<string, string?>? readEnvironmentVariable = null)
    {
        readEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        foreach (var name in UnsafeRuntimeVariables)
        {
            if (!string.IsNullOrWhiteSpace(readEnvironmentVariable(name)))
            {
                return name;
            }
        }
        var dotNetRoot = readEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotNetRoot))
        {
            var processPath = Environment.ProcessPath;
            var processRoot = processPath is null
                ? null
                : Path.GetDirectoryName(processPath);
            if (!Path.IsPathFullyQualified(dotNetRoot) ||
                processRoot is null ||
                !string.Equals(
                    Path.GetFullPath(dotNetRoot),
                    Path.GetFullPath(processRoot),
                    StringComparison.Ordinal))
            {
                return "DOTNET_ROOT";
            }
        }
        return null;
    }
}

internal static partial class LinuxPrctl
{
    [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int ControlProcess(
        int option,
        nuint argument2,
        nuint argument3,
        nuint argument4,
        nuint argument5);
}

internal static class LinuxProcessControlConstants
{
    internal const int ProcessNotFound = 3;
    internal const int ParentDeathSignal = 1;
    internal const int ChildSubreaper = 36;
    internal const int SetDumpable = 4;
    internal const int Enable = 1;
    internal const int Disable = 0;
    internal const int PidFdOpenSystemCall = 434;
    internal const int PidFdSendSignalSystemCall = 424;
    internal const int SignalNone = 0;
    internal const int SignalKill = 9;
    internal const int SignalTerminate = 15;
    internal const int SignalStop = 19;
    // Match GNU timeout's conventional status codes at process boundaries.
    internal const int TimeoutExitCode = 124;
    internal const int EnvironmentFailureExitCode = 125;
}
