using System.Diagnostics;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SharpProof.Host;

public static partial class LinuxPathIdentity
{
    private const int ErrorNoEntry = 2;
    private const int ErrorInterrupted = 4;
    private const int ErrorWouldBlock = 11;
    private const int ErrorNotDirectory = 20;
    private const uint FileTypeMask = 0xF000;
    private const uint FileTypeDirectory = 0x4000;
    private const uint FileTypeRegular = 0x8000;
    private const uint FileTypeSymbolicLink = 0xA000;
    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;
    private const int LockUnlock = 8;
    private const int OpenReadOnly = 0;
    private const int OpenReadWrite = 2;
    private const int OpenCreate = 0x40;
    private const int OpenDirectory = 0x10000;
    private const int OpenNoFollow = 0x20000;
    private const int OpenCloseOnExec = 0x80000;
    private const uint OwnerReadWrite = 0x180;
    private const string LegacyPublicationMarkerSuffix =
        ".sharpproof-publication-set";
    private const string LegacyPublicationLockSuffix =
        ".sharpproof-publication-lock";
    private const string PublicationMetadataDirectory =
        ".sharpproof-publication";
    private const string PublicationMarkerExtension = ".set";
    private const string PublicationLockExtension = ".lock";
    private const string PublicationMarkerHeader =
        "SharpProof.PublicationSet/1\n";
    private static readonly byte[] PublicationSetIdentityDomain =
        Encoding.ASCII.GetBytes("SharpProof.PublicationSetIdentity/1\0");
    private static readonly byte[] PublicationPathIdentityDomain =
        Encoding.ASCII.GetBytes("SharpProof.PublicationPathIdentity/1\0");
    // Publication requires local flock, atomic rename, and directory fsync
    // semantics. Unknown mount types must fail closed.
    private static readonly HashSet<string> SupportedLocalFileSystems =
        new(StringComparer.Ordinal)
        {
            "btrfs", "ext2", "ext3", "ext4", "overlay", "tmpfs", "xfs"
        };

    public static string Canonicalize(string path)
    {
        EnsureLinux();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(fullPath) || fullPath[0] != '/')
        {
            throw new ArgumentException(
                "SharpProof paths must be absolute Linux paths.",
                nameof(path));
        }

        var current = "/";
        var segments = fullPath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            current = current == "/"
                ? "/" + segments[index]
                : current + "/" + segments[index];
            if (NativeMethods.LStat(current, out var information) == 0)
            {
                var type = information.Mode & FileTypeMask;
                if (type == FileTypeSymbolicLink)
                {
                    throw new ArgumentException(
                        "SharpProof paths must not traverse symbolic links.",
                        nameof(path));
                }
                if (index != segments.Length - 1 &&
                    type != FileTypeDirectory)
                {
                    throw new ArgumentException(
                        "SharpProof path ancestors must be directories.",
                        nameof(path));
                }
                continue;
            }

            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorNoEntry)
            {
                break;
            }
            if (error == ErrorNotDirectory)
            {
                throw new ArgumentException(
                    "SharpProof path ancestors must be directories.",
                    nameof(path));
            }
            throw new IOException(
                $"SharpProof could not establish path identity (errno {error}).");
        }
        return fullPath;
    }

    public static string RequireLocalPath(string path)
    {
        var canonical = Canonicalize(path);
        var fileSystem = FindFileSystemType(canonical);
        if (!SupportedLocalFileSystems.Contains(fileSystem))
        {
            throw new ArgumentException(
                $"SharpProof preview publication requires a supported local filesystem; '{fileSystem}' is not supported.",
                nameof(path));
        }
        return canonical;
    }

    public static string PublicationLockName(string publicationPath)
    {
        return PublicationLockNameForCanonicalPath(Canonicalize(publicationPath));
    }

    public static string PublicationMarkerPath(string publicationPath)
    {
        return PublicationMetadataPath(
            Canonicalize(publicationPath),
            PublicationMarkerExtension);
    }

    public static IDisposable AcquirePublicationSet(
        IEnumerable<string> publicationPaths,
        TimeSpan timeout)
    {
        return AcquirePublicationSet(
            publicationPaths,
            timeout,
            CancellationToken.None);
    }

    internal static void SyncDirectory(string directory)
    {
        EnsureLinux();
        var canonical = Canonicalize(directory);
        var descriptor = NativeMethods.Open(
            canonical,
            OpenReadOnly | OpenDirectory | OpenCloseOnExec,
            mode: 0);
        if (descriptor < 0)
        {
            throw new IOException(
                $"SharpProof could not open a publication directory (errno {Marshal.GetLastPInvokeError()}).");
        }

        using var handle = new SafeFileHandle(
            new IntPtr(descriptor), ownsHandle: true);
        while (NativeMethods.Fsync(descriptor) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorInterrupted)
            {
                continue;
            }
            throw new IOException(
                $"SharpProof could not synchronize a publication directory (errno {error}).");
        }
    }

    public static void ResetPublicationSet(
        IEnumerable<string> publicationPaths,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publicationPaths);
        var requestedPaths = publicationPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        var canonicalPaths = CanonicalPublicationPaths(requestedPaths);
        ValidatePublicationTopology(canonicalPaths);
        ValidatePublicationMetadataAliases(canonicalPaths);
        var markerPaths = canonicalPaths
            .Select(PublicationMarkerPath)
            .ToArray();
        var markerCount = markerPaths.Count(File.Exists);
        var publicationMembersAbsent = canonicalPaths.All(static path =>
            !File.Exists(path) && !Directory.Exists(path));
        if (markerCount == 0 && publicationMembersAbsent)
        {
            return;
        }
        if (markerCount != markerPaths.Length &&
            !publicationMembersAbsent)
        {
            throw new IOException(
                "SharpProof cannot reset an incomplete publication set.");
        }

        using var lease = AcquirePublicationSet(
            canonicalPaths,
            timeout,
            cancellationToken);
        if (markerPaths.Any(static path => !File.Exists(path)))
        {
            throw new IOException(
                "SharpProof publication ownership changed during reset.");
        }
        foreach (var path in canonicalPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
            {
                throw new IOException(
                    "SharpProof publication members must be regular files.");
            }
            File.Delete(path);
        }
        foreach (var markerPath in markerPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(markerPath);
        }
        foreach (var directory in canonicalPaths
                     .Concat(markerPaths)
                     .Select(static path => Path.GetDirectoryName(path)!)
                     .Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyncDirectory(directory);
        }
    }

    public static IDisposable AcquirePublicationSet(
        IEnumerable<string> publicationPaths,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publicationPaths);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            timeout,
            TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var requestedPaths = publicationPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        if (requestedPaths.Length == 0)
        {
            throw new ArgumentException(
                "At least one publication path is required.",
                nameof(publicationPaths));
        }

        var canonicalPaths = CanonicalPublicationPaths(requestedPaths);
        ValidatePublicationTopology(canonicalPaths);
        ValidatePublicationMetadataAliases(canonicalPaths);
        var ancestorIdentity = CaptureAncestorIdentity(canonicalPaths);
        var lockPaths = canonicalPaths
            .Select(PublicationLockNameForCanonicalPath)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var locks = new List<PublicationLock>(lockPaths.Length);
        var acquired = 0;
        var ownershipTransferred = false;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            foreach (var lockPath in lockPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var publicationLock = new PublicationLock(lockPath);
                locks.Add(publicationLock);
                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero ||
                    !publicationLock.Acquire(remaining, cancellationToken))
                {
                    throw new IOException(
                        "Timed out waiting for SharpProof publication paths.");
                }
                acquired++;
            }

            var confirmedPaths = CanonicalPublicationPaths(requestedPaths);
            if (!canonicalPaths.SequenceEqual(
                    confirmedPaths,
                    StringComparer.Ordinal))
            {
                throw new IOException(
                    "SharpProof publication path identity changed while acquiring locks.");
            }
            BindPublicationSet(canonicalPaths);
            var lease = new PublicationLease([.. locks]);
            ownershipTransferred = true;
            return lease;
        }
        finally
        {
            if (!ownershipTransferred)
            {
                ReleaseLocks([.. locks], acquired);
            }
        }
    }

    public static bool AreSameExistingFile(
        string firstPath,
        string secondPath)
    {
        var first = TryInformation(Canonicalize(firstPath));
        var second = TryInformation(Canonicalize(secondPath));
        return first.HasValue && second.HasValue &&
            SameFile(first.Value, second.Value);
    }

    public static bool IsSameOrDescendant(string path, string directory)
    {
        var canonicalPath = Canonicalize(path);
        var canonicalDirectory = Canonicalize(directory);
        if (string.Equals(
                canonicalPath,
                canonicalDirectory,
                StringComparison.Ordinal))
        {
            return true;
        }
        var prefix = canonicalDirectory.EndsWith('/')
            ? canonicalDirectory
            : canonicalDirectory + '/';
        return canonicalPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    public static bool PathsConflict(string firstPath, string secondPath)
    {
        return IsSameOrDescendant(firstPath, secondPath) ||
            IsSameOrDescendant(secondPath, firstPath) ||
            AreSameExistingFile(firstPath, secondPath);
    }

    public static bool DeleteIfUnprotected(
        string path,
        IEnumerable<string> protectedPaths)
    {
        ArgumentNullException.ThrowIfNull(protectedPaths);
        var canonicalPath = Canonicalize(path);
        var candidate = TryInformation(canonicalPath);
        if (!candidate.HasValue)
        {
            return false;
        }
        if ((candidate.Value.Mode & FileTypeMask) != FileTypeRegular)
        {
            throw new InvalidOperationException(
                "SharpProof outputs must be regular files.");
        }

        foreach (var protectedPath in protectedPaths)
        {
            var protectedInformation = TryInformation(
                Canonicalize(protectedPath));
            if (protectedInformation.HasValue &&
                SameFile(candidate.Value, protectedInformation.Value))
            {
                throw new InvalidOperationException(
                    "SharpProof output aliases a protected file.");
            }
        }
        File.Delete(canonicalPath);
        return true;
    }

    private static string[] CanonicalPublicationPaths(
        IEnumerable<string> publicationPaths)
    {
        return publicationPaths
            .Select(RequireLocalPath)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidatePublicationTopology(string[] canonicalPaths)
    {
        for (var index = 0; index < canonicalPaths.Length; index++)
        {
            for (var otherIndex = 0;
                 otherIndex < index;
                 otherIndex++)
            {
                var other = canonicalPaths[otherIndex];
                var current = canonicalPaths[index];
                if (string.Equals(other, current, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "SharpProof publication paths must not contain duplicate canonical paths.");
                }
                if (IsStrictPathAncestor(other, current) ||
                    IsStrictPathAncestor(current, other))
                {
                    throw new ArgumentException(
                        "SharpProof publication paths must not have ancestor or descendant conflicts.");
                }
            }
        }
    }

    private static bool IsStrictPathAncestor(
        string possibleAncestor,
        string possibleDescendant)
    {
        return possibleDescendant.Length > possibleAncestor.Length &&
            possibleDescendant.StartsWith(
                possibleAncestor,
                StringComparison.Ordinal) &&
            possibleDescendant[possibleAncestor.Length] ==
                Path.DirectorySeparatorChar;
    }

    private static Dictionary<string, LinuxStat> CaptureAncestorIdentity(
        string[] canonicalPaths)
    {
        var identities = new Dictionary<string, LinuxStat>(StringComparer.Ordinal);
        foreach (var path in canonicalPaths)
        {
            var current = Path.GetDirectoryName(path) ?? "/";
            while (true)
            {
                if (NativeMethods.LStat(current, out var information) != 0 ||
                    (information.Mode & FileTypeMask) != FileTypeDirectory)
                {
                    throw new IOException("SharpProof publication path ancestors changed during identity capture.");
                }
                identities[current] = information;
                if (current == "/")
                {
                    break;
                }
                current = Path.GetDirectoryName(current) ?? "/";
            }
        }
        return identities;
    }

    private static void ConfirmAncestorIdentity(
        Dictionary<string, LinuxStat> expected)
    {
        foreach (var pair in expected)
        {
            if (NativeMethods.LStat(pair.Key, out var actual) != 0 ||
                (actual.Mode & FileTypeMask) != FileTypeDirectory ||
                !SameFile(pair.Value, actual))
            {
                throw new IOException(
                    "SharpProof publication path ancestor identity changed while acquiring locks.");
            }
        }
    }

    private static string PublicationLockNameForCanonicalPath(
        string canonicalPath)
    {
        return PublicationMetadataPath(
            canonicalPath,
            PublicationLockExtension);
    }

    private static string PublicationMetadataPath(
        string canonicalPath,
        string extension)
    {
        var directory = Path.GetDirectoryName(canonicalPath) ??
            throw new IOException(
                "SharpProof publication path has no parent directory.");
        var utf8 = new UTF8Encoding(false, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(PublicationPathIdentityDomain);
        hash.AppendData(utf8.GetBytes(canonicalPath));
        var identity = Convert.ToHexString(hash.GetHashAndReset());
        return Path.Combine(
            directory,
            PublicationMetadataDirectory,
            identity + extension);
    }

    private static void ValidatePublicationMetadataAliases(
        string[] canonicalPaths)
    {
        if (canonicalPaths.Any(static path =>
                path.EndsWith(
                    LegacyPublicationMarkerSuffix,
                    StringComparison.Ordinal) ||
                path.EndsWith(
                    LegacyPublicationLockSuffix,
                    StringComparison.Ordinal) ||
                path.Split('/').Contains(
                    PublicationMetadataDirectory,
                    StringComparer.Ordinal)))
        {
            throw new ArgumentException(
                "SharpProof publication paths must not use the publication metadata namespace.");
        }
        var metadata = new HashSet<string>(
            canonicalPaths.SelectMany(static path => new[]
            {
                PublicationMetadataPath(path, PublicationMarkerExtension),
                PublicationMetadataPath(path, PublicationLockExtension)
            }),
            StringComparer.Ordinal);
        if (metadata.Count != canonicalPaths.Length * 2 ||
            canonicalPaths.Any(metadata.Contains))
        {
            throw new ArgumentException(
                "SharpProof publication paths must not alias publication metadata.");
        }
    }

    private static void BindPublicationSet(string[] canonicalPaths)
    {
        var setId = PublicationSetId(canonicalPaths);
        var marker = PublicationMarkerHeader + setId + "\n";
        var pending = new List<(string Path, string MarkerPath)>();
        foreach (var path in canonicalPaths)
        {
            var markerPath = PublicationMetadataPath(
                path,
                PublicationMarkerExtension);
            EnsurePublicationMetadataDirectory(markerPath);
            if (File.Exists(markerPath))
            {
                ValidatePublicationMarker(markerPath, marker);
                continue;
            }
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new IOException(
                    "SharpProof refuses to adopt a pre-existing publication destination without an exact ownership marker.");
            }
            pending.Add((path, markerPath));
        }

        foreach (var item in pending)
        {
            EnsurePublicationMetadataDirectory(item.MarkerPath);
        }

        var created = new List<string>();
        var completed = false;
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(marker);
            foreach (var item in pending)
            {
                if (File.Exists(item.Path) || Directory.Exists(item.Path))
                {
                    throw new IOException(
                        "SharpProof refuses to adopt a pre-existing publication destination without an exact ownership marker.");
                }
                try
                {
                    using var stream = new FileStream(
                        item.MarkerPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read);
                    created.Add(item.MarkerPath);
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                catch (IOException) when (File.Exists(item.MarkerPath))
                {
                    ValidatePublicationMarker(item.MarkerPath, marker);
                }
            }
            foreach (var directory in created
                         .Select(static path => Path.GetDirectoryName(path)!)
                         .Distinct(StringComparer.Ordinal))
            {
                SyncDirectory(directory);
            }
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                foreach (var markerPath in created.AsEnumerable().Reverse())
                {
                    try
                    {
                        File.Delete(markerPath);
                    }
                    catch (Exception exception) when (exception is
                        IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
        }
    }

    internal static string PublicationSetId(
        IEnumerable<string> canonicalPaths)
    {
        ArgumentNullException.ThrowIfNull(canonicalPaths);
        var paths = canonicalPaths
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var utf8 = new UTF8Encoding(false, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(PublicationSetIdentityDomain);
        AppendPublicationSetFrame(hash, paths.Length);
        foreach (var path in paths)
        {
            ArgumentNullException.ThrowIfNull(path);
            var bytes = utf8.GetBytes(path);
            AppendPublicationSetFrame(hash, bytes.Length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendPublicationSetFrame(
        IncrementalHash hash,
        int value)
    {
        Span<byte> frame = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(frame, value);
        hash.AppendData(frame);
    }

    private static void ValidatePublicationMarker(
        string markerPath,
        string expected)
    {
        using var handle = OpenRegularMetadata(
            markerPath,
            OpenReadOnly,
            mode: 0,
            "publication ownership marker",
            out var information);
        if (information.Size is < 0 or > 256)
        {
            throw new IOException(
                "SharpProof publication paths partially overlap another publication set. " +
                "Clean the prior output set before changing publication paths.");
        }

        using var stream = new FileStream(handle, FileAccess.Read);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 256,
            leaveOpen: false);
        if (!string.Equals(
                reader.ReadToEnd(),
                expected,
                StringComparison.Ordinal))
        {
            throw new IOException(
                "SharpProof publication paths partially overlap another publication set. " +
                "Clean the prior output set before changing publication paths.");
        }
    }

    private static SafeFileHandle OpenRegularMetadata(
        string path,
        int flags,
        uint mode,
        string description,
        out LinuxStat information)
    {
        information = default;
        var descriptor = NativeMethods.Open(
            path,
            flags | OpenNoFollow | OpenCloseOnExec,
            mode);
        if (descriptor < 0)
        {
            throw new IOException(
                $"SharpProof could not open a {description} (errno {Marshal.GetLastPInvokeError()}).");
        }

        var handle = new SafeFileHandle(
            new IntPtr(descriptor),
            ownsHandle: true);
        if (NativeMethods.FStat(descriptor, out information) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException(
                $"SharpProof could not inspect a {description} (errno {error}).");
        }
        // The pathname may have been replaced between open(2) and fstat(2).
        // Never claim the lock protects the current pathname unless its inode
        // still matches the descriptor we actually locked.
        if (NativeMethods.LStat(path, out var pathnameInformation) != 0 ||
            (pathnameInformation.Mode & FileTypeMask) == FileTypeSymbolicLink ||
            !SameFile(information, pathnameInformation))
        {
            handle.Dispose();
            throw new IOException(
                $"SharpProof {description} pathname changed during open.");
        }
        if ((information.Mode & FileTypeMask) != FileTypeRegular)
        {
            handle.Dispose();
            throw new IOException(
                $"SharpProof {description}s must be regular files.");
        }
        return handle;
    }

    private static void EnsurePublicationMetadataDirectory(
        string metadataPath)
    {
        var metadataDirectory = Path.GetDirectoryName(metadataPath) ??
            throw new IOException(
                "SharpProof publication metadata has no directory.");
        var publicationDirectory = Path.GetDirectoryName(metadataDirectory) ??
            throw new IOException(
                "SharpProof publication metadata has no parent directory.");
        Directory.CreateDirectory(publicationDirectory);
        if (!Directory.Exists(metadataDirectory))
        {
            if (!OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException(
                    "SharpProof publication metadata requires Linux.");
            }
            Directory.CreateDirectory(
                metadataDirectory,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
        if (NativeMethods.LStat(metadataDirectory, out var information) != 0)
        {
            throw new IOException(
                $"SharpProof could not inspect the publication metadata directory (errno {Marshal.GetLastPInvokeError()}).");
        }
        if ((information.Mode & FileTypeMask) != FileTypeDirectory ||
            information.UserId != NativeMethods.GetEffectiveUserId() ||
            (information.Mode & 0x3F) != 0)
        {
            throw new IOException(
                "SharpProof publication metadata must be an owned private directory.");
        }
    }

    private static void ReleaseLocks(PublicationLock[] locks, int acquired)
    {
        for (var index = acquired - 1; index >= 0; index--)
        {
            locks[index].Release();
        }
        foreach (var publicationLock in locks)
        {
            publicationLock.Dispose();
        }
    }

    private static LinuxStat? TryInformation(string path)
    {
        if (NativeMethods.LStat(path, out var information) == 0)
        {
            if ((information.Mode & FileTypeMask) == FileTypeSymbolicLink)
            {
                throw new ArgumentException(
                    "SharpProof paths must not traverse symbolic links.",
                    nameof(path));
            }
            return information;
        }
        var error = Marshal.GetLastPInvokeError();
        if (error == ErrorNoEntry || error == ErrorNotDirectory)
        {
            return null;
        }
        throw new IOException(
            $"SharpProof could not establish file identity (errno {error}).");
    }

    private static bool SameFile(LinuxStat left, LinuxStat right)
    {
        return left.Device == right.Device && left.Inode == right.Inode;
    }

    private static string FindFileSystemType(string canonicalPath)
    {
        const string mountInfoPath = "/proc/self/mountinfo";
        if (!File.Exists(mountInfoPath))
        {
            throw new PlatformNotSupportedException(
                "SharpProof requires Linux mount metadata.");
        }

        string? bestMount = null;
        string? bestType = null;
        foreach (var line in File.ReadLines(mountInfoPath))
        {
            var separator = line.IndexOf(" - ", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }
            var left = line.Substring(0, separator).Split(' ');
            var right = line.Substring(separator + 3).Split(' ');
            if (left.Length < 5 || right.Length == 0)
            {
                continue;
            }
            var mount = DecodeMountPath(left[4]);
            if (!IsPathWithin(canonicalPath, mount) ||
                bestMount != null && mount.Length <= bestMount.Length)
            {
                continue;
            }
            bestMount = mount;
            bestType = right[0];
        }
        return bestType ?? throw new IOException(
            "SharpProof could not identify the publication filesystem.");
    }

    private static bool IsPathWithin(string path, string directory)
    {
        if (string.Equals(path, directory, StringComparison.Ordinal))
        {
            return true;
        }
        var prefix = directory.EndsWith('/')
            ? directory
            : directory + '/';
        return path.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string DecodeMountPath(string value)
    {
        return value
            .Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal)
            .Replace("\\012", "\n", StringComparison.Ordinal)
            .Replace("\\134", "\\", StringComparison.Ordinal);
    }

    private static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "SharpProof host identity requires Linux amd64.");
        }
    }

    private sealed class PublicationLock : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private bool _acquired;

        internal PublicationLock(string path)
        {
            EnsurePublicationMetadataDirectory(path);
            _handle = OpenRegularMetadata(
                path,
                OpenReadWrite | OpenCreate,
                OwnerReadWrite,
                "publication lock",
                out _);
        }

        internal bool Acquire(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (NativeMethods.Flock(
                        _handle.DangerousGetHandle().ToInt32(),
                        LockExclusive | LockNonBlocking) == 0)
                {
                    _acquired = true;
                    return true;
                }
                var error = Marshal.GetLastPInvokeError();
                if (error != ErrorWouldBlock)
                {
                    throw new IOException(
                        $"SharpProof could not acquire a publication lock (errno {error}).");
                }
                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }
                var delay = remaining < TimeSpan.FromMilliseconds(20)
                    ? remaining
                    : TimeSpan.FromMilliseconds(20);
                if (cancellationToken.WaitHandle.WaitOne(delay))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }

        internal void Release()
        {
            if (!_acquired)
            {
                return;
            }
            if (NativeMethods.Flock(
                    _handle.DangerousGetHandle().ToInt32(),
                    LockUnlock) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                throw new IOException(
                    $"SharpProof could not release a publication lock (errno {error}).");
            }
            _acquired = false;
        }

        public void Dispose()
        {
            if (_acquired)
            {
                Release();
            }
            _handle.Dispose();
        }
    }

    private sealed class PublicationLease : IDisposable
    {
        private PublicationLock[]? _locks;

        internal PublicationLease(PublicationLock[] locks)
        {
            _locks = locks;
        }

        public void Dispose()
        {
            var locks = Interlocked.Exchange(ref _locks, null);
            if (locks != null)
            {
                ReleaseLocks(locks, locks.Length);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxTimespec
    {
        internal long Seconds;
        internal long Nanoseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxStat
    {
        internal ulong Device;
        internal ulong Inode;
        internal ulong HardLinks;
        internal uint Mode;
        internal uint UserId;
        internal uint GroupId;
        private int _padding;
        internal ulong RDevice;
        internal long Size;
        internal long BlockSize;
        internal long Blocks;
        internal LinuxTimespec AccessTime;
        internal LinuxTimespec ModificationTime;
        internal LinuxTimespec ChangeTime;
        private long _reserved0;
        private long _reserved1;
        private long _reserved2;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("libc", EntryPoint = "lstat", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int LStat(string path, out LinuxStat information);

        [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Flock(int descriptor, int operation);

        [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int FStat(int descriptor, out LinuxStat information);

        [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Fsync(int descriptor);

        [LibraryImport("libc", EntryPoint = "open", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Open(string path, int flags, uint mode);

        [LibraryImport("libc", EntryPoint = "geteuid")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial uint GetEffectiveUserId();
    }
}
