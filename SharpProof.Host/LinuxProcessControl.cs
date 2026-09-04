using System.Runtime.InteropServices;

namespace SharpProof.Host;

internal static partial class LinuxProcessControl
{
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    internal static partial int Kill(int processId, int signal);
}
