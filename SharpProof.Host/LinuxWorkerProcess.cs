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
    private Process? _process;

    private LinuxWorkerProcess(Process process)
    {
        _process = process;
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
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
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
            process.StandardInput.WriteLine(StartMessage);
            process.StandardInput.Close();
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
        while (!process.WaitForExit(0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= terminationStart)
            {
                return CompleteAtDeadline(process, stopwatch, finalLimit);
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
        var process = Interlocked.Exchange(ref _process, null);
        if (process == null)
        {
            return;
        }
        if (!process.HasExited)
        {
            var stopwatch = Stopwatch.StartNew();
            _ = Terminate(process, stopwatch, TimeSpan.FromSeconds(1));
        }
        process.Dispose();
    }

    internal static LinuxWorkerCompletion CompleteAtDeadline(
        Process process,
        Stopwatch stopwatch,
        TimeSpan finalLimit)
    {
        return Terminate(process, stopwatch, finalLimit)
            ? new LinuxWorkerCompletion(
                LinuxWorkerCompletionKind.TimedOut,
                124)
            : new LinuxWorkerCompletion(
                LinuxWorkerCompletionKind.Exited,
                process.ExitCode);
    }

    private static bool Terminate(
        Process process,
        Stopwatch stopwatch,
        TimeSpan finalLimit)
    {
        if (process.HasExited)
        {
            return false;
        }
        if (NativeMethods.Kill(process.Id, SignalTerminate) != 0)
        {
            if (Marshal.GetLastPInvokeError() == 3 &&
                process.WaitForExit(0))
            {
                return false;
            }
            throw NativeFailure(
                "SharpProof could not terminate the worker process.");
        }
        var remainingForTerminate = finalLimit - stopwatch.Elapsed;
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
            var remaining = finalLimit - stopwatch.Elapsed;
            var killWait = checked((int)Math.Min(
                Math.Max(0, remaining.TotalMilliseconds),
                int.MaxValue));
            if (!process.WaitForExit(killWait))
            {
                throw new InvalidOperationException(
                    "The SharpProof worker did not terminate within its grace period.");
            }
        }
        return true;
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
