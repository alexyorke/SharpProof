using System.Diagnostics;
using System.ComponentModel;
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
    private const int CleanupRetryBudgetMilliseconds = 5000;
    private const int CleanupDescriptorReserveCount = 3;

    // This hook is test-only and is intentionally limited to the supervisor's
    // cleanup boundary. It lets the failure-path test hold cleanup incomplete
    // without manufacturing descendant processes in the test process.
    internal static Func<int, int, DescendantStopResult>? StopDescendantsOverrideForTest
    {
        get;
        set;
    }

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
            bool started;
            try
            {
                started = process.Start();
            }
            catch (Exception exception) when (
                exception is Win32Exception or InvalidOperationException or IOException)
            {
                WriteCleanupReceipt(nonce);
                return 125;
            }
            if (!started)
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
            var cleanup = StopDescendantsForRun(
                Environment.ProcessId,
                CleanupMilliseconds,
                descriptorReserves: descriptorReserves,
                protectedProcessId: process.Id);
            var hadDescendants = cleanup.HadDescendants;
            var retryDelayMilliseconds = 10;
            var cleanupBudget = Stopwatch.StartNew();
            while (!cleanup.Complete &&
                   cleanupBudget.ElapsedMilliseconds < CleanupRetryBudgetMilliseconds)
            {
                var remaining = CleanupRetryBudgetMilliseconds -
                    (int)cleanupBudget.ElapsedMilliseconds;
                Thread.Sleep(Math.Min(retryDelayMilliseconds, remaining));
                retryDelayMilliseconds = Math.Min(
                    retryDelayMilliseconds * 2,
                    5000);
                cleanup = StopDescendantsForRun(
                    Environment.ProcessId,
                    RetryCleanupMilliseconds,
                    protectedProcessId: process.Id);
                hadDescendants |= cleanup.HadDescendants;
            }
            if (!cleanup.Complete)
            {
                // Do not emit an authenticated cleanup receipt when the
                // supervisor could not prove that all owned descendants are
                // gone. The caller must treat this as containment failure.
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

    private static DescendantStopResult StopDescendantsForRun(
        int supervisorId,
        int maximumMilliseconds,
        IReadOnlyList<int>? descriptorReserves = null,
        int? protectedProcessId = null)
    {
        if (StopDescendantsOverrideForTest is { } overrideForTest)
        {
            CloseDescriptors(descriptorReserves ?? []);
            return overrideForTest(supervisorId, maximumMilliseconds);
        }

        return StopDescendants(
            supervisorId,
            maximumMilliseconds,
            descriptorReserves: descriptorReserves,
            protectedProcessId: protectedProcessId);
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
        try
        {
            return process.Start()
                ? WaitForWorkerExit(process)
                : 125;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            return 125;
        }
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
        IReadOnlyList<int>? descriptorReserves = null,
        int? protectedProcessId = null)
    {
        CloseDescriptors(descriptorReserves ?? []);
        var foundAny = false;
        var deadline = Stopwatch.StartNew();
        var consecutiveEmptyScans = 0;
        while (deadline.ElapsedMilliseconds < maximumMilliseconds)
        {
            var discovered = DescendantProcessIds(
                supervisorId,
                protectedProcessId,
                () => deadline.ElapsedMilliseconds >= maximumMilliseconds);
            if (deadline.ElapsedMilliseconds >= maximumMilliseconds)
            {
                break;
            }
            if (discovered.Count == 0)
            {
                consecutiveEmptyScans++;
                if (consecutiveEmptyScans >= 2)
                {
                    return new DescendantStopResult(
                        foundAny,
                        Complete: true);
                }
                Thread.Sleep(1);
                continue;
            }
            consecutiveEmptyScans = 0;
            foundAny = true;
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
                            ReadProcessParents(
                                () => deadline.ElapsedMilliseconds >= maximumMilliseconds)))
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
                if (deadline.ElapsedMilliseconds >= maximumMilliseconds)
                {
                    break;
                }
                ReapExitedChildren(protectedProcessId);
            }
            if (deadline.ElapsedMilliseconds < maximumMilliseconds)
            {
                Thread.Yield();
            }
        }
        var finalComplete = deadline.ElapsedMilliseconds < maximumMilliseconds &&
            DescendantProcessIds(
                supervisorId,
                protectedProcessId,
                () => deadline.ElapsedMilliseconds >= maximumMilliseconds).Count == 0;
        return new DescendantStopResult(
            foundAny,
            Complete: finalComplete);
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

    private static HashSet<int> DescendantProcessIds(
        int supervisorId,
        int? protectedProcessId = null,
        Func<bool>? deadlineExpired = null)
    {
        var parents = ReadProcessParents(deadlineExpired);
        return parents.Keys
            .Where(processId =>
                processId != protectedProcessId &&
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

    private static Dictionary<int, int> ReadProcessParents(
        Func<bool>? deadlineExpired = null)
    {
        var result = new Dictionary<int, int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (deadlineExpired?.Invoke() == true)
            {
                break;
            }
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

    private static void ReapExitedChildren(int? protectedProcessId = null)
    {
        if (protectedProcessId is { } protectedId)
        {
            foreach (var processId in DescendantProcessIds(
                         Environment.ProcessId,
                         protectedId))
            {
                _ = NativeMethods.WaitForProcess(processId, out _, 1);
            }
            return;
        }
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
