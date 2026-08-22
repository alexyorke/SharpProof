using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SharpProof.BuildTasks;

internal static partial class VerifierProcessSupervisor
{
    private const int ChildSubreaper = 36;
    private const int SetDumpable = 4;
    private const int PidFdOpenSystemCall = 434;
    private const int PidFdSendSignalSystemCall = 424;
    private const int SignalKill = 9;
    private const int SignalNone = 0;
    private const int SignalStop = 19;
    private const int ProcessNotFound = 3;
    private const string StartMessage = "SharpProof.Start/1";
    private const string ArmedMessage = "SharpProof.Armed/1";
    private const string CleanupMessage = "SharpProof.Cleanup/1";
    private const int CleanupMilliseconds = 750;
    private const int RetryCleanupMilliseconds = 100;
    private const int CleanupDescriptorReserveCount = 3;

    internal static int Run(string[] command)
    {
        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            NativeMethods.ControlProcess(
                ChildSubreaper,
                1,
                0,
                0,
                0) != 0)
        {
            return 125;
        }
        if (NativeMethods.ControlProcess(
                SetDumpable,
                0,
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
                    SignalNone) != 0)
            {
                CloseDescriptors(cleanupDescriptorReserves);
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
                gate.StartsWith(StartMessage + " ",
                    StringComparison.Ordinal)
                ? gate[(StartMessage.Length + 1)..]
                : string.Empty;
            if (!IsValidNonce(nonce))
            {
                return 125;
            }
            Console.Out.WriteLine(ArmedMessage + " " + nonce);
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
            process.StartInfo.ArgumentList.Add("--run-verifier-child");
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
                descriptorReserves: descriptorReserves);
            var hadDescendants = cleanup.HadDescendants;
            var retryDelayMilliseconds = 10;
            while (!cleanup.Complete)
            {
                Thread.Sleep(retryDelayMilliseconds);
                retryDelayMilliseconds = Math.Min(
                    retryDelayMilliseconds * 2,
                    5000);
                cleanup = StopDescendants(
                    Environment.ProcessId,
                    RetryCleanupMilliseconds);
                hadDescendants |= cleanup.HadDescendants;
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
        Console.Out.WriteLine(CleanupMessage + " " + nonce);
        Console.Out.Flush();
    }

    internal static int RunWorker(string[] command)
    {
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
        IReadOnlyList<int>? descriptorReserves = null)
    {
        CloseDescriptors(descriptorReserves ?? []);
        var foundAny = false;
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < maximumMilliseconds)
        {
            var discovered = DescendantProcessIds(supervisorId);
            if (discovered.Count == 0)
            {
                return new DescendantStopResult(
                    foundAny,
                    Complete: true);
            }
            foundAny = true;
            foreach (var processId in discovered)
            {
                var descriptor = openPidFd?.Invoke(processId) ??
                    OpenPidFd(processId);
                if (descriptor < 0)
                {
                    if (Marshal.GetLastPInvokeError() != ProcessNotFound)
                    {
                        Thread.Sleep(1);
                    }
                    continue;
                }
                try
                {
                    if (!IsDescendant(
                            processId,
                            supervisorId,
                            ReadProcessParents()))
                    {
                        continue;
                    }
                    if ((sendSignal?.Invoke(
                             descriptor,
                             SignalStop) ??
                         SendPidFdSignal(descriptor, SignalStop)) != 0)
                    {
                        if (Marshal.GetLastPInvokeError() != ProcessNotFound)
                        {
                            Thread.Sleep(1);
                        }
                        continue;
                    }
                    if ((sendSignal?.Invoke(
                             descriptor,
                             SignalKill) ??
                         SendPidFdSignal(descriptor, SignalKill)) != 0 &&
                        Marshal.GetLastPInvokeError() != ProcessNotFound)
                    {
                        Thread.Sleep(1);
                    }
                }
                finally
                {
                    _ = NativeMethods.Close(descriptor);
                }
            }
            if (supervisorId == Environment.ProcessId)
            {
                ReapExitedChildren();
            }
            Thread.Yield();
        }
        return new DescendantStopResult(
            foundAny,
            Complete: DescendantProcessIds(supervisorId).Count == 0);
    }

    internal readonly record struct DescendantStopResult(
        bool HadDescendants,
        bool Complete);

    private static void CloseDescriptors(IEnumerable<int> descriptors)
    {
        foreach (var descriptor in descriptors.Where(
                     static descriptor => descriptor >= 0))
        {
            _ = NativeMethods.Close(descriptor);
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
        var parents = ReadProcessParents();
        return parents.Keys
            .Where(processId =>
                IsDescendant(processId, supervisorId, parents))
            .ToHashSet();
    }

    private static bool IsDescendant(
        int processId,
        int supervisorId,
        Dictionary<int, int> parents)
    {
        var seen = new HashSet<int>();
        for (var current = processId;
             current > 1 && seen.Add(current) &&
             parents.TryGetValue(current, out var parent);
             current = parent)
        {
            if (parent == supervisorId)
            {
                return true;
            }
        }
        return false;
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
        return (int)NativeMethods.SystemCall2(
            PidFdOpenSystemCall,
            processId,
            0);
    }

    private static int SendPidFdSignal(int descriptor, int signal)
    {
        return (int)NativeMethods.SystemCall4(
            PidFdSendSignalSystemCall,
            descriptor,
            signal,
            0,
            0);
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

    private static partial class NativeMethods
    {
        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Close(int descriptor);

        [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int ControlProcess(
            int option,
            nuint argument2,
            nuint argument3,
            nuint argument4,
            nuint argument5);

        [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial nint SystemCall2(
            nint number,
            int argument1,
            uint argument2);

        [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial nint SystemCall4(
            nint number,
            int argument1,
            int argument2,
            nint argument3,
            uint argument4);

        [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int WaitForProcess(
            int processId,
            out int status,
            int options);
    }
}
