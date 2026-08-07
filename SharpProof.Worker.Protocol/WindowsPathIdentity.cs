using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SharpProof.Worker.Protocol;

public static class WindowsPathIdentity
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint DeleteAccess = 0x00010000;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    public static string Canonicalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }
        var fullPath = NormalizeNamespace(Path.GetFullPath(path));
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "Windows path identity requires Windows.");
        }

        ValidateNoAlternateDataStream(fullPath);
        RejectReparsePoints(fullPath);
        var suffix = new Stack<string>();
        var existingPath = fullPath;
        while (true)
        {
            using var handle = Open(existingPath);
            if (!handle.IsInvalid)
            {
                if (suffix.Count != 0)
                {
                    if (!GetFileInformationByHandle(
                            handle, out var information))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(),
                            "SharpProof path ancestors must be directories.");
                    }
                    if ((information.FileAttributes &
                         (uint)FileAttributes.Directory) == 0)
                    {
                        throw new ArgumentException(
                            "SharpProof path ancestors must be directories.",
                            nameof(path));
                    }
                }
                var canonical = FinalPath(handle);
                while (suffix.Count != 0)
                {
                    canonical = Path.Combine(canonical, suffix.Pop());
                }
                return Path.GetFullPath(canonical);
            }

            var error = Marshal.GetLastWin32Error();
            if (error != ErrorFileNotFound && error != ErrorPathNotFound)
            {
                throw new Win32Exception(error,
                    "SharpProof could not establish path identity.");
            }

            var name = Path.GetFileName(existingPath);
            var parent = Path.GetDirectoryName(existingPath);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parent) ||
                string.Equals(parent, existingPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new Win32Exception(error,
                    "SharpProof could not find an existing path ancestor.");
            }
            suffix.Push(name);
            existingPath = parent;
        }
    }

    public static bool AreSameExistingFile(string firstPath, string secondPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "Windows file identity requires Windows.");
        }

        using var first = Open(Canonicalize(firstPath));
        if (first.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorFileNotFound || error == ErrorPathNotFound)
            {
                return false;
            }
            throw new Win32Exception(error,
                "SharpProof could not establish file identity.");
        }
        using var second = Open(Canonicalize(secondPath));
        if (second.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorFileNotFound || error == ErrorPathNotFound)
            {
                return false;
            }
            throw new Win32Exception(error,
                "SharpProof could not establish file identity.");
        }
        var firstInformation = Information(first);
        var secondInformation = Information(second);
        return SameFile(firstInformation, secondInformation);
    }

    public static bool IsSameOrDescendant(string path, string directory)
    {
        var canonicalPath = Canonicalize(path);
        var canonicalDirectory = Canonicalize(directory);
        if (string.Equals(canonicalPath, canonicalDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var prefix = canonicalDirectory.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            StringComparison.Ordinal)
            ? canonicalDirectory
            : canonicalDirectory + Path.DirectorySeparatorChar;
        return canonicalPath.StartsWith(
            prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool DeleteIfUnprotected(
        string path,
        IEnumerable<string> protectedPaths)
    {
        if (protectedPaths == null)
        {
            throw new ArgumentNullException(nameof(protectedPaths));
        }
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "Windows file deletion requires Windows.");
        }
        var canonicalPath = Canonicalize(path);
        using var candidate = Open(canonicalPath, DeleteAccess);
        if (candidate.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorFileNotFound || error == ErrorPathNotFound)
            {
                return false;
            }
            throw new Win32Exception(error,
                "SharpProof could not open an output for invalidation.");
        }
        if (!string.Equals(
                FinalPath(candidate),
                canonicalPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SharpProof output identity changed before invalidation.");
        }
        var candidateInformation = Information(candidate);
        if ((candidateInformation.FileAttributes &
             (uint)FileAttributes.Directory) != 0)
        {
            throw new InvalidOperationException(
                "SharpProof outputs must be files.");
        }
        foreach (var protectedPath in protectedPaths)
        {
            using var protectedHandle = Open(Canonicalize(protectedPath));
            if (protectedHandle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorFileNotFound || error == ErrorPathNotFound)
                {
                    continue;
                }
                throw new Win32Exception(error,
                    "SharpProof could not establish protected file identity.");
            }
            if (SameFile(candidateInformation, Information(protectedHandle)))
            {
                throw new InvalidOperationException(
                    "SharpProof output aliases a protected file.");
            }
        }
        var disposition = new FileDispositionInformation { DeleteFile = true };
        if (!SetFileInformationByHandle(
                candidate,
                FileInformationClass.FileDispositionInfo,
                ref disposition,
                (uint)Marshal.SizeOf<FileDispositionInformation>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "SharpProof could not invalidate an output.");
        }
        return true;
    }

    private static ByHandleFileInformation Information(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "SharpProof could not establish file identity.");
        }
        return information;
    }

    private static bool SameFile(
        ByHandleFileInformation left,
        ByHandleFileInformation right)
    {
        return left.VolumeSerialNumber == right.VolumeSerialNumber &&
            left.FileIndexHigh == right.FileIndexHigh &&
            left.FileIndexLow == right.FileIndexLow;
    }

    private static string NormalizeNamespace(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return path;
        }

        const string extendedUncPrefix = @"\\?\UNC\";
        const string extendedPrefix = @"\\?\";
        if (path.StartsWith(extendedUncPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path.Substring(extendedUncPrefix.Length);
        }
        if (path.StartsWith(extendedPrefix, StringComparison.Ordinal))
        {
            var remainder = path.Substring(extendedPrefix.Length);
            var drive = remainder.Length == 0 ? '\0' : remainder[0];
            var isDriveLetter = drive >= 'A' && drive <= 'Z' ||
                drive >= 'a' && drive <= 'z';
            if (remainder.Length >= 3 && isDriveLetter &&
                remainder[1] == ':' &&
                (remainder[2] == '\\' || remainder[2] == '/'))
            {
                return remainder;
            }
            throw new ArgumentException(
                "SharpProof paths must not use unsupported device namespaces.",
                nameof(path));
        }
        if (path.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "SharpProof paths must not use device namespaces.",
                nameof(path));
        }
        return path;
    }

    private static void ValidateNoAlternateDataStream(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        if (path.IndexOf(':', root.Length) >= 0)
        {
            throw new ArgumentException(
                "SharpProof paths must not use alternate data streams.",
                nameof(path));
        }
    }

    private static void RejectReparsePoints(string path)
    {
        var current = path;
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ArgumentException(
                        "SharpProof paths must not traverse reparse points.",
                        nameof(path));
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(
                    parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            current = parent;
        }
    }

    private static SafeFileHandle Open(string path, uint desiredAccess = 0)
    {
        return CreateFileW(
            NativePath(path),
            desiredAccess,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);
    }

    private static string NativePath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path.Substring(2);
        }
        return @"\\?\" + path;
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (true)
        {
            var buffer = new char[capacity];
            var length = GetFinalPathNameByHandleW(
                handle, buffer, (uint)buffer.Length, 0);
            if (length == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "SharpProof could not resolve a canonical path.");
            }
            if (length < buffer.Length)
            {
                return NormalizeNamespace(new string(buffer, 0, (int)length));
            }
            capacity = checked((int)length + 1);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        [Out] char[] filePath,
        uint filePathLength,
        uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        FileInformationClass fileInformationClass,
        ref FileDispositionInformation fileInformation,
        uint bufferSize);

    private enum FileInformationClass
    {
        FileDispositionInfo = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInformation
    {
        [MarshalAs(UnmanagedType.Bool)]
        internal bool DeleteFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }
}
