using System.Runtime.InteropServices;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

internal static partial class LinuxNativeMethods
{
    internal static nint OpenPidFd(int processId)
    {
        return SystemCall2(
            LinuxProcessControlConstants.PidFdOpenSystemCall,
            processId,
            0);
    }

    internal static nint SendPidFdSignal(int descriptor, int signal)
    {
        return SystemCall4(
            LinuxProcessControlConstants.PidFdSendSignalSystemCall,
            descriptor,
            signal,
            0,
            0);
    }

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int Close(int descriptor);

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
}
