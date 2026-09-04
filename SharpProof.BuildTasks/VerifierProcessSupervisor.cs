using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

internal static partial class VerifierProcessSupervisor
{
    private const int CleanupMilliseconds = 750;
    private const int RetryCleanupMilliseconds = 100;
    private const int MaximumCleanupRetries = 8;
    private const int CleanupDescriptorReserveCount = 3;

    internal static int Run(string[] command)
    {
        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            LinuxPrctl.ControlProcess(
                LinuxProcessControlConstants.ChildSubreaper,
                LinuxProcessControlConstants.Enable,
                0,
                0,
                0) != 0)
        {
            return 125;
        }
        if (LinuxPrctl.ControlProcess(
                LinuxProcessControlConstants.SetDumpable,
                LinuxProcessControlConstants.Disable,
                0,
                0,
                0) != 0)
        {
            return 125;
        }

        var cleanupDescriptorReserves = Enumerable
            .Repeat(-1, CleanupDescriptorReserveCount)
            .ToArray();
        for (var index = 0;
             index < cleanupDescriptorReserves.Length;
             index++)
        {
            cleanupDescriptorReserves[index] = OpenPidFd(
                Environment.ProcessId);
            if (cleanupDescriptorReserves[index] < 0 ||
                SendPidFdSignal(
                    cleanupDescriptorReserves[index],
                    LinuxProcessControlConstants.SignalNone) != 0)
            {
                CloseDescriptors(cleanupDescriptorReserves);
                cleanupDescriptorReserves = [];
                return 125;
            }
        }

        using var cancellation = new CancellationTokenSource();
        using var terminate = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context =>
            {
                context.Cancel = true;
                cancellation.Cancel();
            });
        using var interrupt = PosixSignalRegistration.Create(
            PosixSignal.SIGINT,
            context =>
            {
                context.Cancel = true;
                cancellation.Cancel();
            });
        try
        {
            var gate = Console.In.ReadLine();
            var nonce = gate != null &&
                gate.StartsWith(LinuxWorkerProcess.StartMessage + " ",
                    StringComparison.Ordinal)
                ? gate[(LinuxWorkerProcess.StartMessage.Length + 1)..]
                : string.Empty;
            if (!IsValidNonce(nonce))
            {
                return 125;
            }
            Console.Out.WriteLine(LinuxWorkerProcess.ArmedMessage + " " + nonce);
            Console.Out.Flush();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command[0],
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(
                typeof(VerifierProcessSupervisor).Assembly.Location);
            process.StartInfo.ArgumentList.Add(Program.WorkerArgument);
            foreach (var argument in command)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            if (!process.Start())
            {
                WriteCleanupReceipt(nonce);
                return 125;
            }

            while (!process.WaitForExit(25) &&
                   !cancellation.IsCancellationRequested)
            {
            }
            var directExitCode = process.HasExited
                ? process.ExitCode
                : 143;
            var descriptorReserves = cleanupDescriptorReserves;
            cleanupDescriptorReserves = [];
            var cleanup = StopDescendants(
                Environment.ProcessId,
                CleanupMilliseconds,
                descriptorReserves: descriptorReserves.Skip(1),
                supervisorPidFd: descriptorReserves[0],
                managedProcessId: process.Id);
            cleanup = RetryCleanup(
                cleanup,
                descriptorReserves[0],
                (pidFd) => StopDescendants(
                    Environment.ProcessId,
                    RetryCleanupMilliseconds,
                    supervisorPidFd: pidFd,
                    managedProcessId: process.Id));
            var hadDescendants = cleanup.HadDescendants;
            if (!cleanup.Complete)
            {
                // Do not keep the build task alive indefinitely when a
                // hostile or stuck descendant cannot be reaped.
                return 125;
            }
            if (!process.HasExited && !process.WaitForExit(1000))
            {
                return 125;
            }
            ReapOwnedDescendants();
            WriteCleanupReceipt(nonce);
            return cancellation.IsCancellationRequested
                ? 143
                : hadDescendants
                    ? 124
                    : directExitCode;
        }
        finally
        {
            CloseDescriptors(cleanupDescriptorReserves);
        }
    }

    internal static bool IsValidNonce(string nonce)
    {
        return nonce.Length == 64 && nonce.All(static character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');
    }

    private static void WriteCleanupReceipt(string nonce)
    {
        // The verifier may leave its final stdout line unterminated. Cleanup
        // runs after every writer has exited, so this separator gives the
        // authenticated record an unambiguous frame on the shared stream.
        Console.Out.WriteLine();
        Console.Out.WriteLine(LinuxWorkerProcess.CleanupMessage + " " + nonce);
        Console.Out.Flush();
    }

    internal static int RunWorker(string[] command)
    {
        // Keep the verifier attached to this containment boundary.  If the
        // supervisor is killed abruptly, Linux reparents the worker (and any
        // verifier it starts) to init; PDEATHSIG makes the kernel terminate
        // the whole inherited launch chain instead.
        if (LinuxPrctl.ControlProcess(
                LinuxProcessControlConstants.ParentDeathSignal,
                LinuxProcessControlConstants.SignalKill,
                0,
                0,
                0) != 0)
        {
            return 125;
        }
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command[0],
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in command.Skip(1))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        return process.Start()
            ? WaitForWorkerExit(process)
            : 125;
    }

    private static int WaitForWorkerExit(Process process)
    {
        process.WaitForExit();
        return process.ExitCode;
    }

    internal static DescendantStopResult StopDescendants(
        int supervisorId,
        int maximumMilliseconds,
        Func<int, int>? openPidFd = null,
        Func<int, int, int>? sendSignal = null,
        IEnumerable<int>? descriptorReserves = null,
        int supervisorPidFd = -1,
        int managedProcessId = -1)
    {
        CloseDescriptors(descriptorReserves ?? []);
        var foundAny = false;
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < maximumMilliseconds)
        {
            // A numeric PID is not an identity once the process has exited.
            // If the retained pidfd no longer names the original supervisor,
            // do not scan that PID's descendants: it may have been recycled
            // for an unrelated process.
            if (supervisorPidFd >= 0 &&
                (sendSignal?.Invoke(supervisorPidFd,
                    LinuxProcessControlConstants.SignalNone) ??
                 SendPidFdSignal(
                     supervisorPidFd,
                     LinuxProcessControlConstants.SignalNone)) != 0)
            {
                return new DescendantStopResult(
                    foundAny,
                    Complete: true);
            }
            var parents = ReadProcessParents();
            var discovered = DescendantProcessIds(supervisorId, parents);
            if (discovered.Count == 0)
            {
                return new DescendantStopResult(
                    foundAny,
                    Complete: true);
            }
            foundAny = true;
            // The process-parent table is a snapshot for this pass. Reusing it
            // avoids rescanning /proc once per descendant (which made large
            // trees quadratic) while preserving the existing pidfd checks.
            foreach (var processId in discovered)
            {
                if (deadline.ElapsedMilliseconds >= maximumMilliseconds)
                {
                    break;
                }
                var descriptor = openPidFd?.Invoke(processId) ??
                    OpenPidFd(processId);
                if (descriptor < 0)
                {
                    if (Marshal.GetLastPInvokeError() !=
                            LinuxProcessControlConstants.ProcessNotFound)
                    {
                        Thread.Sleep(1);
                    }
                    continue;
                }
                try
                {
                    if ((sendSignal?.Invoke(
                             descriptor,
                             LinuxProcessControlConstants.SignalStop) ??
                         SendPidFdSignal(
                             descriptor,
                             LinuxProcessControlConstants.SignalStop)) != 0)
                    {
                        if (Marshal.GetLastPInvokeError() !=
                                LinuxProcessControlConstants.ProcessNotFound)
                        {
                            Thread.Sleep(1);
                        }
                        continue;
                    }
                    if ((sendSignal?.Invoke(
                             descriptor,
                             LinuxProcessControlConstants.SignalKill) ??
                         SendPidFdSignal(
                             descriptor,
                             LinuxProcessControlConstants.SignalKill)) != 0 &&
                        Marshal.GetLastPInvokeError() !=
                            LinuxProcessControlConstants.ProcessNotFound)
                    {
                        Thread.Sleep(1);
                    }
                }
                finally
                {
                    _ = LinuxNativeMethods.Close(descriptor);
                }
            }
            if (supervisorId == Environment.ProcessId)
            {
                ReapExitedChildren(discovered, managedProcessId);
            }
            Thread.Yield();
        }
        // The deadline may have allowed the supervisor PID to be recycled.
        // A failed pidfd probe proves that the retained identity is gone, so
        // scanning the numeric PID here could otherwise inspect an unrelated
        // process. Only use the process table while the identity is verified.
        var supervisorGone = supervisorPidFd >= 0 &&
            (sendSignal?.Invoke(
                supervisorPidFd,
                LinuxProcessControlConstants.SignalNone) ??
             SendPidFdSignal(
                 supervisorPidFd,
                 LinuxProcessControlConstants.SignalNone)) != 0;
        var complete = supervisorGone ||
            DescendantProcessIds(supervisorId).Count == 0;
        return new DescendantStopResult(foundAny, complete);
    }

    internal readonly record struct DescendantStopResult(
        bool HadDescendants,
        bool Complete);

    internal static DescendantStopResult RetryCleanup(
        DescendantStopResult cleanup,
        int supervisorPidFd,
        Func<int, DescendantStopResult> retry,
        Action<int>? delay = null)
    {
        var retryDelayMilliseconds = 10;
        for (var attempt = 0;
             !cleanup.Complete && attempt < MaximumCleanupRetries;
             attempt++)
        {
            (delay ?? Thread.Sleep)(retryDelayMilliseconds);
            retryDelayMilliseconds = Math.Min(
                retryDelayMilliseconds * 2,
                5000);
            var next = retry(supervisorPidFd);
            cleanup = new DescendantStopResult(
                cleanup.HadDescendants || next.HadDescendants,
                next.Complete);
        }
        return cleanup;
    }

    private static void CloseDescriptors(IEnumerable<int> descriptors)
    {
        foreach (var descriptor in descriptors.Where(
                     static descriptor => descriptor >= 0))
        {
            _ = LinuxNativeMethods.Close(descriptor);
        }
    }

    private static void ReapOwnedDescendants()
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < 1000)
        {
            ReapExitedChildren();
            if (DescendantProcessIds(Environment.ProcessId).Count == 0)
            {
                return;
            }
            Thread.Sleep(10);
        }
    }

    private static HashSet<int> DescendantProcessIds(int supervisorId)
    {
        return DescendantProcessIds(supervisorId, ReadProcessParents());
    }

    private static HashSet<int> DescendantProcessIds(
        int supervisorId,
        Dictionary<int, int> parents)
    {
        var descendants = new HashSet<int>();
        var states = new Dictionary<int, bool>();
        var path = new List<int>();
        var pathNodes = new HashSet<int>();
        foreach (var processId in parents.Keys)
        {
            if (!states.TryGetValue(processId, out var isDescendant))
            {
                path.Clear();
                pathNodes.Clear();
                var current = processId;
                isDescendant = false;
                while (current > 1 &&
                       !states.ContainsKey(current) &&
                       parents.TryGetValue(current, out var parent))
                {
                    if (!pathNodes.Add(current))
                    {
                        break;
                    }
                    path.Add(current);
                    if (parent == supervisorId)
                    {
                        isDescendant = true;
                        break;
                    }
                    current = parent;
                }
                if (!isDescendant &&
                    current > 1 &&
                    states.TryGetValue(current, out var resolved))
                {
                    isDescendant = resolved;
                }
                foreach (var node in path)
                {
                    states[node] = isDescendant;
                }
            }

            if (isDescendant)
            {
                descendants.Add(processId);
            }
        }
        return descendants;
    }

    private static Dictionary<int, int> ReadProcessParents()
    {
        var result = new Dictionary<int, int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(
                    Path.GetFileName(directory),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var processId))
            {
                continue;
            }
            try
            {
                var stat = File.ReadAllText(
                    Path.Combine(directory, "stat"));
                var close = stat.LastIndexOf(')');
                if (close < 0)
                {
                    continue;
                }
                var fields = stat.AsSpan(close + 2)
                    .ToString()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 2 &&
                    int.TryParse(
                        fields[1],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parentId))
                {
                    result[processId] = parentId;
                }
            }
            catch (DirectoryNotFoundException) { }
            catch (FileNotFoundException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    private static int OpenPidFd(int processId)
    {
        return (int)LinuxNativeMethods.OpenPidFd(processId);
    }

    private static int SendPidFdSignal(int descriptor, int signal)
    {
        return (int)LinuxNativeMethods.SendPidFdSignal(descriptor, signal);
    }

    private static void ReapExitedChildren()
    {
        while (NativeMethods.WaitForProcess(
                   -1,
                   out _,
                   1) > 0)
        {
        }
    }

    private static void ReapExitedChildren(
        IEnumerable<int> processIds,
        int excludedProcessId)
    {
        foreach (var processId in processIds.Where(
                     processId => processId != excludedProcessId))
        {
            _ = NativeMethods.WaitForProcess(processId, out _, 1);
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int WaitForProcess(
            int processId,
            out int status,
            int options);
    }
}
