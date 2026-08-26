using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SharpProof.Host;

public enum LinuxWorkerCompletionKind
{
    Exited,
    TimedOut
}

public sealed class LinuxWorkerCompletion
{
    internal LinuxWorkerCompletion(
        LinuxWorkerCompletionKind kind,
        int exitCode)
    {
        Kind = kind;
        ExitCode = exitCode;
    }

    public LinuxWorkerCompletionKind Kind { get; }
    public int ExitCode { get; }
}

public sealed partial class LinuxWorkerProcess : IDisposable
{
    public const string StartMessage = "SharpProof.Start/1";
    private const int ParentDeathSignal = 1;
    private const int SignalKill = 9;
    private const int SignalTerminate = 15;
    private const int PollMilliseconds = 25;
    private const string SetsidPath = "/usr/bin/setsid";
    private readonly object _synchronization = new();
    private readonly long _startedTimestamp;
    private Process? _process;

    private LinuxWorkerProcess(Process process, long startedTimestamp)
    {
        _process = process;
        _startedTimestamp = startedTimestamp;
    }

    public static LinuxWorkerProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        EnsureLinux();

        var startInfo = new ProcessStartInfo
        {
            FileName = SetsidPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(executable);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException(
            "The SharpProof worker process could not be started.");
        var ownershipTransferred = false;
        try
        {
            var startedTimestamp = Stopwatch.GetTimestamp();
            process.StandardInput.WriteLine(StartMessage);
            process.StandardInput.Close();
            var worker = new LinuxWorkerProcess(
                process,
                startedTimestamp);
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
        lock (_synchronization)
        {
            var process = _process ?? throw new ObjectDisposedException(
                nameof(LinuxWorkerProcess));
            while (!process.WaitForExit(0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var elapsed = Stopwatch.GetElapsedTime(_startedTimestamp);
                if (elapsed >= terminationStart)
                {
                    // The non-blocking check at the top of the loop and the
                    // deadline calculation are not atomic. Recheck before
                    // classifying the completion so a child that exits at the
                    // boundary is reported as exited rather than timed out.
                    if (process.WaitForExit(0))
                    {
                        return new LinuxWorkerCompletion(
                            LinuxWorkerCompletionKind.Exited,
                            process.ExitCode);
                    }
                    Terminate(process, _startedTimestamp, finalLimit);
                    return new LinuxWorkerCompletion(
                        LinuxWorkerCompletionKind.TimedOut,
                        124);
                }
                var remaining = terminationStart - elapsed;
                var waitMilliseconds = (int)Math.Min(
                    Math.Max(1, remaining.TotalMilliseconds),
                    PollMilliseconds);
                if (cancellationToken.WaitHandle.WaitOne(waitMilliseconds))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            return new LinuxWorkerCompletion(
                LinuxWorkerCompletionKind.Exited,
                process.ExitCode);
        }
    }

    public static void EnterChildBoundaryRequired(int expectedParentProcessId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            expectedParentProcessId,
            1);
        EnsureLinux();
        if (NativeMethods.ControlProcess(
                ParentDeathSignal,
                SignalKill,
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
        lock (_synchronization)
        {
            var process = _process;
            if (process == null)
            {
                return;
            }
            try
            {
                // A naturally exited leader can still leave descendants in
                // its setsid process group.  Terminate handles both live and
                // already-exited leaders, so always run it during disposal.
                Terminate(
                    process,
                    Stopwatch.GetTimestamp(),
                    TimeSpan.FromSeconds(1));
            }
            finally
            {
                process.Dispose();
                _process = null;
            }
        }
    }

    private static void Terminate(
        Process process,
        long startedTimestamp,
        TimeSpan finalLimit)
    {
        if (process.HasExited)
        {
            KillProcessGroup(process.Id);
            return;
        }
        if (NativeMethods.Kill(process.Id, SignalTerminate) != 0 &&
            Marshal.GetLastPInvokeError() != 3)
        {
            throw NativeFailure(
                "SharpProof could not terminate the worker process.");
        }
        var remainingForTerminate = finalLimit -
            Stopwatch.GetElapsedTime(startedTimestamp);
        var terminateWait = checked((int)Math.Min(
            Math.Max(0, remainingForTerminate.TotalMilliseconds / 2),
            int.MaxValue));
        if (!process.WaitForExit(terminateWait))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.WaitForExit(0))
            {
            }
            var remaining = finalLimit -
                Stopwatch.GetElapsedTime(startedTimestamp);
            var killWait = checked((int)Math.Min(
                Math.Max(0, remaining.TotalMilliseconds),
                int.MaxValue));
            if (!process.WaitForExit(killWait))
            {
                throw new InvalidOperationException(
                    "The SharpProof worker did not terminate within its grace period.");
            }
        }
        KillProcessGroup(process.Id);
    }

    private static void KillProcessGroup(int processId)
    {
        if (NativeMethods.Kill(-processId, SignalKill) != 0 &&
            Marshal.GetLastPInvokeError() != 3)
        {
            throw NativeFailure(
                "SharpProof could not terminate the worker process group.");
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
            KillProcessGroup(process.Id);
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
        [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int ControlProcess(
            int option,
            nuint argument2,
            nuint argument3,
            nuint argument4,
            nuint argument5);

        [LibraryImport("libc", EntryPoint = "getppid")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int GetParentProcessId();

        [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Kill(int processId, int signal);
    }
}
