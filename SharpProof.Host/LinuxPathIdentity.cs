using System.Buffers.Binary;
using System.Buffers;
using System.Diagnostics;
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
    private const int ErrorInvalidArgument = 22;
    private const int ErrorNotDirectory = 20;
    private const int ErrorNotTty = 25;
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
    private const int OpenNonBlock = 0x800;
    private const int OpenCloseOnExec = 0x80000;
    private const uint OwnerReadWrite = 0x180;
    private const uint Ext4CasefoldFlag = 0x40000000;
    private const uint IoctlGetFlags = 0x80086601;
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
    private static readonly SearchValues<char> HexUppercase =
        SearchValues.Create("0123456789ABCDEF");
    private static readonly HashSet<string> UnsupportedRemoteFileSystems =
        new(StringComparer.Ordinal)
        {
            "9p", "afs", "ceph", "cifs", "nfs", "nfs4", "smb3", "sshfs",
            "fuse.sshfs", "fuse", "lustre", "virtiofs"
        };

    // Test-only probe. A null result represents an unexpected native probe
    // failure and must remain fail-closed just like the real ioctl path.
    internal static Func<string, bool?>? CaseFoldedParentProbeOverrideForTest
    {
        get;
        set;
    }

    // Test-only probes keep filesystem classification regressions deterministic
    // without requiring a particular mount type in the test container.
    internal static Func<string, long?>? StatFsTypeProbeOverrideForTest
    {
        get;
        set;
    }

    internal static Func<string, string?>? MountInfoFileSystemTypeProbeOverrideForTest
    {
        get;
        set;
    }

    internal static Action<string>? DirectorySyncOverrideForTest
    {
        get;
        set;
    }

    // A launcher invocation qualifies the same publication paths through
    // several validation and publication phases.  Keep the expensive,
    // read-only filesystem classification in this short-lived cache while
    // still re-running Canonicalize at every boundary and the post-lock
    // identity confirmation without the cache.
    internal sealed class PathQualificationCache
    {
        private readonly Dictionary<string, bool> _caseFoldedParents = [];
        private readonly Dictionary<string, string> _fileSystemTypes = [];
        private readonly Dictionary<string, string> _mountTypes = [];

        internal List<(string Mount, string Type)>? MountInfo
        {
            get;
            set;
        }

        internal bool TryGetCaseFoldedParent(
            string path,
            out bool isCaseFolded)
        {
            return _caseFoldedParents.TryGetValue(path, out isCaseFolded);
        }

        internal void SetCaseFoldedParent(string path, bool isCaseFolded)
        {
            _caseFoldedParents[path] = isCaseFolded;
        }

        internal bool TryGetFileSystemType(
            string path,
            out string fileSystemType)
        {
            return _fileSystemTypes.TryGetValue(path, out fileSystemType!);
        }

        internal void SetFileSystemType(string path, string fileSystemType)
        {
            _fileSystemTypes[path] = fileSystemType;
        }

        internal bool TryGetMountType(
            string path,
            out string fileSystemType)
        {
            return _mountTypes.TryGetValue(path, out fileSystemType!);
        }

        internal void SetMountType(string path, string fileSystemType)
        {
            _mountTypes[path] = fileSystemType;
        }
    }

    public static string Canonicalize(string path)
    {
        EnsureLinux();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        // Resolve only literal components below. Lexically collapsing a
        // parent segment before lstat would let a symlinked directory be
        // traversed and then hidden by the resulting path.
        if (path.Split('/').Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException(
                "SharpProof paths must not contain dot segments.",
                nameof(path));
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
        return RequireLocalPath(path, qualificationCache: null);
    }

    internal static string RequireRegularFilePath(string path)
    {
        var canonical = Canonicalize(path);
        if (NativeMethods.LStat(canonical, out var information) == 0)
        {
            if ((information.Mode & FileTypeMask) != FileTypeRegular)
            {
                throw new ArgumentException(
                    "SharpProof writable file paths must be regular files.",
                    nameof(path));
            }
            return canonical;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != ErrorNoEntry)
        {
            throw new IOException(
                $"SharpProof could not inspect the writable file path (errno {error}).");
        }
        return canonical;
    }

    internal static string RequireDirectoryPath(string path)
    {
        var canonical = Canonicalize(path);
        if (NativeMethods.LStat(canonical, out var information) == 0)
        {
            if ((information.Mode & FileTypeMask) != FileTypeDirectory)
            {
                throw new ArgumentException(
                    "SharpProof cache paths must be directories.",
                    nameof(path));
            }
            return canonical;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != ErrorNoEntry)
        {
            throw new IOException(
                $"SharpProof could not inspect the directory path (errno {error}).");
        }
        return canonical;
    }

    internal static string RequireLocalPath(
        string path,
        PathQualificationCache? qualificationCache)
    {
        var canonical = Canonicalize(path);
        if (IsCaseFoldedParent(canonical, qualificationCache))
        {
            throw new ArgumentException(
                "SharpProof publication paths on case-folding directories are unsupported.",
                nameof(path));
        }
        var fileSystem = FindVisibleFileSystemType(
            canonical,
            qualificationCache);
        if (UnsupportedRemoteFileSystems.Contains(fileSystem))
        {
            throw new ArgumentException(
                $"SharpProof preview publication does not support the '{fileSystem}' filesystem.",
                nameof(path));
        }
        return canonical;
    }

    private static bool IsCaseFoldedParent(
        string canonicalPath,
        PathQualificationCache? qualificationCache = null)
    {
        var parent = Path.GetDirectoryName(canonicalPath);
        while (!string.IsNullOrEmpty(parent))
        {
            if (qualificationCache is not null &&
                qualificationCache.TryGetCaseFoldedParent(
                    parent,
                    out var cachedCaseFolded))
            {
                if (cachedCaseFolded)
                {
                    return true;
                }

                parent = Path.GetDirectoryName(parent);
                continue;
            }

            if (CaseFoldedParentProbeOverrideForTest is { } probe)
            {
                var result = probe(parent);
                if (result is null)
                {
                    throw new IOException(
                        "SharpProof could not inspect case-folding flags.");
                }
                qualificationCache?.SetCaseFoldedParent(parent, result.Value);
                if (result.Value)
                {
                    return true;
                }
                parent = Path.GetDirectoryName(parent);
                continue;
            }
            if (NativeMethods.LStat(parent, out _) == 0)
            {
                var descriptor = NativeMethods.Open(
                    parent,
                    OpenReadOnly | OpenDirectory | OpenNoFollow | OpenCloseOnExec,
                    mode: 0);
                if (descriptor < 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == ErrorNoEntry)
                    {
                        parent = Path.GetDirectoryName(parent);
                        continue;
                    }

                    throw new IOException(
                        $"SharpProof could not inspect case-folding flags " +
                        $"(errno {error}).");
                }

                try
                {
                    var flags = 0u;
                    if (NativeMethods.Ioctl(
                            descriptor,
                            IoctlGetFlags,
                            ref flags) == 0)
                    {
                        if ((flags & Ext4CasefoldFlag) != 0)
                        {
                            qualificationCache?.SetCaseFoldedParent(parent, true);
                            return true;
                        }
                        qualificationCache?.SetCaseFoldedParent(parent, false);
                    }
                    else
                    {
                        var error = Marshal.GetLastPInvokeError();
                        if (error != ErrorNotTty &&
                            error != ErrorInvalidArgument)
                        {
                            throw new IOException(
                                "SharpProof could not inspect case-folding " +
                                $"flags (errno {error}).");
                        }
                    }
                }
                finally
                {
                    _ = NativeMethods.Close(descriptor);
                }
            }
            parent = Path.GetDirectoryName(parent);
        }
        return false;
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
        if (DirectorySyncOverrideForTest is { } sync)
        {
            sync(canonical);
            return;
        }
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

    private static void SyncParentDirectories(IEnumerable<string> paths)
    {
        foreach (var directory in paths
                     .Select(static path => Path.GetDirectoryName(path))
                     .Where(static path => !string.IsNullOrEmpty(path))
                     .Distinct(StringComparer.Ordinal))
        {
            SyncDirectory(directory!);
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
        using var lease = AcquirePublicationLocks(
            canonicalPaths,
            timeout,
            cancellationToken);

        var markerState = markerPaths
            .Select(static path => TryInformation(path))
            .ToArray();
        var pathState = canonicalPaths
            .Select(static path => TryInformation(path))
            .ToArray();
        // A process killed while writing the old in-place marker could leave
        // malformed metadata behind. It is safe to remove that metadata only
        // when its destination is absent; a present destination still requires
        // an exact authenticated marker before any cleanup is allowed.
        for (var index = 0; index < markerPaths.Length; index++)
        {
            if (markerState[index].HasValue &&
                !pathState[index].HasValue &&
                TryRemoveTornPublicationMarker(markerPaths[index]))
            {
                markerState[index] = null;
            }
        }
        var markerCount = markerState.Count(static value => value.HasValue);
        if (markerCount == 0 && pathState.All(static value => !value.HasValue))
        {
            return;
        }
        if (markerCount != markerPaths.Length)
        {
            // A partial marker sequence can be left by an interrupted bind or
            // reset.  Only exact markers authenticate ownership.  A partially
            // owned set is safe to finish cleaning only when every unmarked
            // member is absent; an unmarked destination is never removed or
            // adopted by a retry.
            var partialMarker = PublicationMarkerHeader +
                PublicationSetId(canonicalPaths) + "\n";
            for (var index = 0; index < markerPaths.Length; index++)
            {
                if (markerState[index].HasValue)
                {
                    ValidatePublicationMarker(markerPaths[index], partialMarker);
                    if (pathState[index] is { } information &&
                        (information.Mode & FileTypeMask) != FileTypeRegular)
                    {
                        throw new IOException(
                            "SharpProof publication members must be regular files.");
                    }
                }
                else if (pathState[index].HasValue)
                {
                    throw new IOException(
                        "SharpProof cannot reset an incomplete publication set.");
                }
            }

            var changedPaths = new List<string>();
            try
            {
                for (var index = 0; index < canonicalPaths.Length; index++)
                {
                    if (markerState[index].HasValue)
                    {
                        if (pathState[index].HasValue)
                        {
                            File.Delete(canonicalPaths[index]);
                            changedPaths.Add(canonicalPaths[index]);
                        }
                        File.Delete(markerPaths[index]);
                        changedPaths.Add(markerPaths[index]);
                    }
                }
            }
            finally
            {
                SyncParentDirectories(changedPaths);
            }
            return;
        }

        var marker = PublicationMarkerHeader +
            PublicationSetId(canonicalPaths) + "\n";
        for (var index = 0; index < canonicalPaths.Length; index++)
        {
            ValidatePublicationMarker(markerPaths[index], marker);
            if (pathState[index].HasValue &&
                (pathState[index]!.Value.Mode & FileTypeMask) != FileTypeRegular)
            {
                throw new IOException(
                    "SharpProof publication members must be regular files.");
            }
        }
        var fullChangedPaths = new List<string>();
        try
        {
            for (var index = 0; index < canonicalPaths.Length; index++)
            {
                if (pathState[index].HasValue)
                {
                    File.Delete(canonicalPaths[index]);
                    fullChangedPaths.Add(canonicalPaths[index]);
                }
            }
            foreach (var markerPath in markerPaths)
            {
                File.Delete(markerPath);
                fullChangedPaths.Add(markerPath);
            }
        }
        finally
        {
            SyncParentDirectories(fullChangedPaths);
        }
    }

    public static void InvalidatePublicationSet(
        IEnumerable<string> publicationPaths,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        InvalidatePublicationSetCore(
            publicationPaths,
            timeout,
            cancellationToken,
            qualificationCache: null);
    }

    internal static void InvalidatePublicationSet(
        IEnumerable<string> publicationPaths,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        PathQualificationCache qualificationCache)
    {
        InvalidatePublicationSetCore(
            publicationPaths,
            timeout,
            cancellationToken,
            qualificationCache);
    }

    private static void InvalidatePublicationSetCore(
        IEnumerable<string> publicationPaths,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        PathQualificationCache? qualificationCache)
    {
        ArgumentNullException.ThrowIfNull(publicationPaths);
        var requestedPaths = publicationPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        var canonicalPaths = CanonicalPublicationPaths(
            requestedPaths,
            qualificationCache);
        ValidatePublicationTopology(canonicalPaths);
        ValidatePublicationMetadataAliases(canonicalPaths);
        using var lease = AcquirePublicationLocks(
            canonicalPaths,
            timeout,
            cancellationToken,
            qualificationCache: qualificationCache);

        var marker = PublicationMarkerHeader +
            PublicationSetId(canonicalPaths) + "\n";
        var owned = new List<(string Path, string MarkerPath)>();
        for (var index = 0; index < canonicalPaths.Length; index++)
        {
            var markerPath = PublicationMarkerPath(canonicalPaths[index]);
            if (TryInformation(markerPath) is { } markerInformation)
            {
                if ((markerInformation.Mode & FileTypeMask) != FileTypeRegular)
                {
                    throw new IOException(
                        "SharpProof publication ownership markers must be regular files.");
                }
                ValidatePublicationMarker(markerPath, marker);
                owned.Add((canonicalPaths[index], markerPath));
            }
        }

        var changedPaths = new List<string>();
        try
        {
            foreach (var (path, _) in owned)
            {
                var information = TryInformation(path);
                if (information is { } value &&
                    (value.Mode & FileTypeMask) != FileTypeRegular)
                {
                    throw new IOException(
                        "SharpProof publication members must be regular files.");
                }
                if (information.HasValue)
                {
                    File.Delete(path);
                    changedPaths.Add(path);
                }
            }
            foreach (var (_, markerPath) in owned)
            {
                File.Delete(markerPath);
                changedPaths.Add(markerPath);
            }
        }
        finally
        {
            SyncParentDirectories(changedPaths);
        }
    }

    /// <summary>
    /// Removes the owned output members of a publication while retaining the
    /// lock set for the current invocation.  Output members may carry a
    /// marker from an earlier generation when a caller changes one of the
    /// non-output paths (for example, the compiler-manifest path).  The
    /// marker still proves that the member was created by SharpProof, so it is
    /// safe to invalidate that member without requiring the old generation's
    /// complete path list to be reconstructed.
    /// </summary>
    public static void InvalidatePublicationMembers(
        IEnumerable<string> publicationPaths,
        IEnumerable<string> outputPaths,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publicationPaths);
        ArgumentNullException.ThrowIfNull(outputPaths);
        var canonicalPublicationPaths = CanonicalPublicationPaths(
            publicationPaths.Where(static path => !string.IsNullOrWhiteSpace(path)));
        var canonicalOutputPaths = CanonicalPublicationPaths(
            outputPaths.Where(static path => !string.IsNullOrWhiteSpace(path)));
        ValidatePublicationTopology(canonicalPublicationPaths);
        ValidatePublicationMetadataAliases(canonicalPublicationPaths);
        var publicationSet = new HashSet<string>(
            canonicalPublicationPaths,
            StringComparer.Ordinal);
        if (canonicalOutputPaths.Any(path => !publicationSet.Contains(path)))
        {
            throw new ArgumentException(
                "Every output path must be a member of the publication set.",
                nameof(outputPaths));
        }

        using var lease = AcquirePublicationLocks(
            canonicalPublicationPaths,
            timeout,
            cancellationToken,
            bind: false);
        var hasOwnedPublicationMarker = false;
        foreach (var publicationPath in canonicalPublicationPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var markerPath = PublicationMarkerPath(publicationPath);
            if (TryInformation(markerPath) is not { } markerInformation)
            {
                continue;
            }
            if ((markerInformation.Mode & FileTypeMask) != FileTypeRegular)
            {
                throw new IOException(
                    "SharpProof publication ownership markers must be regular files.");
            }
            ValidatePublicationMarkerFormat(markerPath);
            hasOwnedPublicationMarker = true;
        }
        var changedPaths = new List<string>();
        try
        {
            foreach (var path in canonicalOutputPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var markerPath = PublicationMarkerPath(path);
                if (TryInformation(markerPath) is not { } markerInformation)
                {
                    if (hasOwnedPublicationMarker && TryInformation(path) is { } memberInformation)
                    {
                        if ((memberInformation.Mode & FileTypeMask) != FileTypeRegular)
                        {
                            throw new IOException(
                                "SharpProof publication members must be regular files.");
                        }
                        File.Delete(path);
                        changedPaths.Add(path);
                    }
                    continue;
                }

                if ((markerInformation.Mode & FileTypeMask) != FileTypeRegular)
                {
                    throw new IOException(
                        "SharpProof publication ownership markers must be regular files.");
                }
                ValidatePublicationMarkerFormat(markerPath);
                if (TryInformation(path) is { } information)
                {
                    if ((information.Mode & FileTypeMask) != FileTypeRegular)
                    {
                        throw new IOException(
                            "SharpProof publication members must be regular files.");
                    }
                    File.Delete(path);
                    changedPaths.Add(path);
                }
                File.Delete(markerPath);
                changedPaths.Add(markerPath);
            }
        }
        finally
        {
            SyncParentDirectories(changedPaths);
        }
    }

    public static IDisposable AcquirePublicationSet(
        IEnumerable<string> publicationPaths,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return AcquirePublicationLocks(
            publicationPaths,
            timeout,
            cancellationToken,
            bind: true);
    }

    internal static IDisposable AcquirePublicationSet(
        IEnumerable<string> publicationPaths,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        PathQualificationCache qualificationCache)
    {
        return AcquirePublicationLocks(
            publicationPaths,
            timeout,
            cancellationToken,
            bind: true,
            qualificationCache: qualificationCache);
    }

    public static IDisposable AcquirePublicationSetLease(
        IEnumerable<string> publicationPaths,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return AcquirePublicationLocks(
            publicationPaths,
            timeout,
            cancellationToken,
            bind: false);
    }

    private static PublicationLease AcquirePublicationLocks(
        IEnumerable<string> publicationPaths,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool bind = false,
        PathQualificationCache? qualificationCache = null)
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

        // Keep the legacy duplicate diagnostic for aliases such as
        // `file` and `./file`; Canonicalize still rejects dot segments for
        // all non-duplicate paths before any filesystem operation.
        var lexicalPaths = requestedPaths
            .Select(Path.GetFullPath)
            .ToArray();
        if (lexicalPaths.Length != lexicalPaths.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ArgumentException(
                "SharpProof publication paths must not contain duplicate canonical paths.",
                nameof(publicationPaths));
        }

        var canonicalPaths = CanonicalPublicationPaths(
            requestedPaths,
            qualificationCache);
        ValidatePublicationTopology(canonicalPaths);
        ValidatePublicationMetadataAliases(canonicalPaths);
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
            SweepOrphanedTemporaryMarkers(canonicalPaths);
            if (bind)
            {
                BindPublicationSet(canonicalPaths);
            }
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
            throw new IOException(
                "SharpProof outputs must be regular files.");
        }

        foreach (var protectedPath in protectedPaths)
        {
            var protectedInformation = TryInformation(
                Canonicalize(protectedPath));
            if (protectedInformation.HasValue &&
                SameFile(candidate.Value, protectedInformation.Value))
            {
                throw new IOException(
                    "SharpProof output aliases a protected file.");
            }
        }
        File.Delete(canonicalPath);
        return true;
    }

    public static bool TryReadRegularFile(
        string path,
        out byte[] content)
    {
        var canonicalPath = Canonicalize(path);
        if (NativeMethods.LStat(canonicalPath, out var information) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorNoEntry)
            {
                content = [];
                return false;
            }
            throw new IOException(
                $"SharpProof could not inspect the publication member (errno {error}).");
        }
        if ((information.Mode & FileTypeMask) != FileTypeRegular)
        {
            throw new IOException(
                "SharpProof publication members must be regular files.");
        }

        using var handle = OpenRegularMetadata(
            canonicalPath,
            OpenReadOnly | OpenNonBlock,
            mode: 0,
            "publication member",
            out _);
        using var stream = new FileStream(handle, FileAccess.Read);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        content = buffer.ToArray();
        return true;
    }

    private static string[] CanonicalPublicationPaths(
        IEnumerable<string> publicationPaths,
        PathQualificationCache? qualificationCache = null)
    {
        return publicationPaths
            .Select(path => RequireLocalPath(path, qualificationCache))
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
            EnsureRegularPublicationPath(path);
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
                var temporaryMarker = item.MarkerPath + "." +
                    Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(
                               temporaryMarker,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.Read))
                    {
                        stream.Write(bytes);
                        stream.Flush(true);
                    }
                    try
                    {
                        File.Move(temporaryMarker, item.MarkerPath);
                        created.Add(item.MarkerPath);
                    }
                    catch (IOException) when (File.Exists(item.MarkerPath))
                    {
                        ValidatePublicationMarker(item.MarkerPath, marker);
                    }
                }
                finally
                {
                    try
                    {
                        File.Delete(temporaryMarker);
                    }
                    catch (Exception exception) when (exception is
                        IOException or UnauthorizedAccessException)
                    {
                    }
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

    private static void SweepOrphanedTemporaryMarkers(
        IEnumerable<string> canonicalPaths)
    {
        foreach (var markerPath in canonicalPaths.Select(PublicationMarkerPath))
        {
            var metadataDirectory = Path.GetDirectoryName(markerPath) ??
                throw new IOException(
                    "SharpProof publication metadata has no directory.");
            var pattern = Path.GetFileName(markerPath) + ".*.tmp";
            var removed = false;
            foreach (var temporaryMarker in Directory.EnumerateFiles(
                         metadataDirectory,
                         pattern,
                         SearchOption.TopDirectoryOnly))
            {
                File.Delete(temporaryMarker);
                removed = true;
            }

            if (removed)
            {
                SyncDirectory(metadataDirectory);
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

    private static void ValidatePublicationMarkerFormat(string markerPath)
    {
        if (!IsPublicationMarkerWellFormed(markerPath))
        {
            throw new IOException(
                "SharpProof publication ownership marker is malformed.");
        }
    }

    private static bool TryRemoveTornPublicationMarker(string markerPath)
    {
        if (IsPublicationMarkerWellFormed(markerPath))
        {
            return false;
        }

        File.Delete(markerPath);
        SyncDirectory(Path.GetDirectoryName(markerPath)!);
        return true;
    }

    private static bool IsPublicationMarkerWellFormed(string markerPath)
    {
        using var handle = OpenRegularMetadata(
            markerPath,
            OpenReadOnly,
            mode: 0,
            "publication ownership marker",
        out var information);
        if (information.Size is < 0 or > 256)
        {
            return false;
        }
        using var stream = new FileStream(handle, FileAccess.Read);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 256,
            leaveOpen: false);
        string marker;
        try
        {
            marker = reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        return marker.Length >= PublicationMarkerHeader.Length + 64 &&
            marker.StartsWith(PublicationMarkerHeader, StringComparison.Ordinal) &&
            marker.Length == PublicationMarkerHeader.Length + 64 + 1 &&
            marker[^1] == '\n' &&
            marker.AsSpan(PublicationMarkerHeader.Length, 64).IndexOfAnyExcept(
                HexUppercase) < 0;
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
        Exception? firstFailure = null;
        for (var index = acquired - 1; index >= 0; index--)
        {
            try
            {
                locks[index].Release();
            }
            catch (IOException exception)
            {
                firstFailure ??= exception;
            }
        }
        foreach (var publicationLock in locks)
        {
            try
            {
                publicationLock.Dispose();
            }
            catch (IOException exception)
            {
                firstFailure ??= exception;
            }
            catch (ObjectDisposedException exception)
            {
                firstFailure ??= exception;
            }
            catch (InvalidOperationException exception)
            {
                firstFailure ??= exception;
            }
        }
        if (firstFailure != null)
        {
            throw firstFailure;
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

    private static void EnsureRegularPublicationPath(string path)
    {
        if (NativeMethods.LStat(path, out var information) == 0)
        {
            if ((information.Mode & FileTypeMask) != FileTypeRegular)
            {
                throw new IOException(
                    "SharpProof publication members must be regular files.");
            }
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != ErrorNoEntry)
        {
            throw new IOException(
                $"SharpProof could not inspect the publication destination (errno {error}).");
        }
    }

    private static bool SameFile(LinuxStat left, LinuxStat right)
    {
        return left.Device == right.Device && left.Inode == right.Inode;
    }

    private static string FindFileSystemType(
        string canonicalPath,
        PathQualificationCache? qualificationCache = null)
    {
        if (qualificationCache is not null &&
            qualificationCache.TryGetMountType(
                canonicalPath,
                out var cachedMountType))
        {
            return cachedMountType;
        }

        if (MountInfoFileSystemTypeProbeOverrideForTest is { } probe)
        {
            var fileSystemType = probe(canonicalPath) ?? throw new IOException(
                "SharpProof could not identify the publication filesystem.");
            qualificationCache?.SetMountType(canonicalPath, fileSystemType);
            return fileSystemType;
        }

        const string mountInfoPath = "/proc/self/mountinfo";
        if (!File.Exists(mountInfoPath))
        {
            throw new PlatformNotSupportedException(
                "SharpProof requires Linux mount metadata.");
        }

        string? bestMount = null;
        string? bestType = null;
        foreach (var (mount, type) in ReadMountInfo(qualificationCache, mountInfoPath))
        {
            if (!IsPathWithin(canonicalPath, mount) ||
                bestMount != null && mount.Length < bestMount.Length)
            {
                continue;
            }
            bestMount = mount;
            bestType = type;
        }
        var result = bestType ?? throw new IOException(
            "SharpProof could not identify the publication filesystem.");
        qualificationCache?.SetMountType(canonicalPath, result);
        return result;
    }

    private static IEnumerable<(string Mount, string Type)> ReadMountInfo(
        PathQualificationCache? qualificationCache,
        string mountInfoPath)
    {
        if (qualificationCache?.MountInfo is { } cached)
        {
            return cached;
        }

        var entries = new List<(string Mount, string Type)>();
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
            entries.Add((DecodeMountPath(left[4]), right[0]));
        }

        if (qualificationCache is not null)
        {
            qualificationCache.MountInfo = entries;
        }

        return entries;
    }

    private static string FindVisibleFileSystemType(
        string canonicalPath,
        PathQualificationCache? qualificationCache = null)
    {
        var existing = canonicalPath;
        while (NativeMethods.LStat(existing, out _) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != ErrorNoEntry)
            {
                break;
            }
            existing = Path.GetDirectoryName(existing) ?? "/";
            if (existing == "/")
            {
                break;
            }
        }

        if (qualificationCache is not null &&
            qualificationCache.TryGetFileSystemType(
                existing,
                out var cachedFileSystemType))
        {
            return cachedFileSystemType;
        }

        if (StatFsTypeProbeOverrideForTest is { } probe)
        {
            var type = probe(existing);
            if (type is null)
            {
                var fileSystemType = FindFileSystemType(
                    canonicalPath,
                    qualificationCache);
                qualificationCache?.SetFileSystemType(existing, fileSystemType);
                return fileSystemType;
            }

            var fileSystemTypeFromMagic = type.Value switch
            {
                0x6969 => "nfs",
                unchecked((long)0xFF534D42) => "cifs",
                unchecked((long)0xFE534D42) => "smb3",
                0x65735546 => "fuse",
                _ => FindFileSystemType(canonicalPath, qualificationCache)
            };
            qualificationCache?.SetFileSystemType(existing, fileSystemTypeFromMagic);
            return fileSystemTypeFromMagic;
        }

        if (NativeMethods.StatFs(existing, out var stats) == 0)
        {
            var fileSystemTypeFromStats = stats.Type switch
            {
                0x6969 => "nfs",
                unchecked((long)0xFF534D42) => "cifs",
                unchecked((long)0xFE534D42) => "smb3",
                0x65735546 => "fuse",
                _ => FindFileSystemType(canonicalPath, qualificationCache)
            };
            qualificationCache?.SetFileSystemType(existing, fileSystemTypeFromStats);
            return fileSystemTypeFromStats;
        }
        var fileSystemTypeFromMount = FindFileSystemType(
            canonicalPath,
            qualificationCache);
        qualificationCache?.SetFileSystemType(existing, fileSystemTypeFromMount);
        return fileSystemTypeFromMount;
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
            .Replace("\\015", "\r", StringComparison.Ordinal)
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
                if (stopwatch.Elapsed >= timeout)
                {
                    return false;
                }
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
            Exception? failure = null;
            try
            {
                if (_acquired)
                {
                    Release();
                }
            }
            catch (IOException exception)
            {
                failure = exception;
            }
            catch (ObjectDisposedException exception)
            {
                failure = exception;
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }
            try
            {
                _handle.Dispose();
            }
            catch (IOException exception)
            {
                failure ??= exception;
            }
            catch (ObjectDisposedException exception)
            {
                failure ??= exception;
            }
            catch (InvalidOperationException exception)
            {
                failure ??= exception;
            }
            if (failure != null)
            {
                throw failure;
            }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxFileSystemStats
    {
        internal long Type;
        private long _blockSize;
        private ulong _blocks;
        private ulong _blocksFree;
        private ulong _blocksAvailable;
        private ulong _files;
        private ulong _filesFree;
        // Linux __kernel_fsid_t is two 32-bit values (8 bytes total). Keep
        // the native layout exact so fields after f_fsid remain aligned.
        private int _filesystemId0;
        private int _filesystemId1;
        private long _nameLength;
        private long _fragmentSize;
        private long _mountFlags;
        private long _spare0;
        private long _spare1;
        private long _spare2;
        private long _spare3;
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

        [LibraryImport("libc", EntryPoint = "statfs", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int StatFs(
            string path,
            out LinuxFileSystemStats stats);

        [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Ioctl(
            int descriptor,
            uint request,
            ref uint value);

        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Close(int descriptor);

        [LibraryImport("libc", EntryPoint = "geteuid")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial uint GetEffectiveUserId();
    }
}
