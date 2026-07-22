using System.Runtime.InteropServices;
namespace SharpProof.Symbolic.Smt;
internal static class SmtNativeLibraryBootstrap {
    internal const string AnalyzerLocatorFileName = "SharpProof.NativeSmtLocator.txt";
    private const int RtldNow = 0x2;
    private const int RtldGlobal = 0x8;
    private static readonly object Sync = new();
    private static readonly HashSet<string> AttemptedLibraryPaths = new(StringComparer.OrdinalIgnoreCase);
    private static IntPtr s_libraryHandle;
    internal static void TryLoadAdjacentLibrary() {
        try {
            var assemblyDirectory = Path.GetDirectoryName(typeof(SmtAnalysisService).Assembly.Location);
            if (!string.IsNullOrWhiteSpace(assemblyDirectory)) TryLoadFromDirectories(new[] { assemblyDirectory });
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
        }
    }
    internal static void TryLoadFromAnalyzerLocatorPaths(IEnumerable<string> paths) {
        if (paths == null) throw new ArgumentNullException(nameof(paths));
        var directories = new List<string>();
        foreach (var path in paths) {
            try {
                if (!string.Equals(Path.GetFileName(path), AnalyzerLocatorFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) directories.Add(directory);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
            }
        }
        TryLoadFromDirectories(directories);
    }
    private static void TryLoadFromDirectories(IEnumerable<string> directories) {
        var fileName = GetNativeLibraryFileName();
        if (fileName == null) return;
        foreach (var directory in directories) {
            string libraryPath;
            try {
                libraryPath = Path.GetFullPath(Path.Combine(directory, fileName));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                continue;
            }
            lock (Sync) {
                if (s_libraryHandle != IntPtr.Zero) return;
                if (!AttemptedLibraryPaths.Add(libraryPath) || !File.Exists(libraryPath)) continue;
                try {
                    s_libraryHandle = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? LoadLibraryWindows(libraryPath)
                        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                            ? LoadLibraryMac(libraryPath, RtldNow | RtldGlobal)
                            : LoadLibraryLinux(libraryPath, RtldNow | RtldGlobal);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                    s_libraryHandle = IntPtr.Zero;
                }
                if (s_libraryHandle != IntPtr.Zero) return;
            }
        }
    }
    private static string? GetNativeLibraryFileName() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetNativeLibraryFileName(OSPlatform.Windows, RuntimeInformation.ProcessArchitecture);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return GetNativeLibraryFileName(OSPlatform.OSX, RuntimeInformation.ProcessArchitecture);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetNativeLibraryFileName(OSPlatform.Linux, RuntimeInformation.ProcessArchitecture);
        return null;
    }
    internal static string? GetNativeLibraryFileName(OSPlatform platform, Architecture architecture) {
        if (architecture != Architecture.X64) return null;
        if (platform == OSPlatform.Windows) return "libz3.dll";
        if (platform == OSPlatform.OSX) return "libz3.dylib";
        if (platform == OSPlatform.Linux) return "libz3.so";
        return null;
    }
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryWindows(string libraryPath);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen")]
    private static extern IntPtr LoadLibraryMac(string libraryPath, int mode);
    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr LoadLibraryLinux(string libraryPath, int mode);
}
