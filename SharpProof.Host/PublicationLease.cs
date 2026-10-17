using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace SharpProof.Host;

// Cooperative writers lock each stable directory entry in a common order.
// Sidecars stay in place: deleting a lock file could split cooperating owners
// between the old inode and a newly created one.
internal sealed class PublicationLease : IDisposable
{
    internal const int TimeoutMilliseconds = 30000;
    private readonly List<FileStream> _members = [];
    private PublicationLease() { }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The returned acquired lease transfers ownership to the caller, which disposes it after publication.")]
    internal static PublicationLease Acquire(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        return AcquireAsync(paths, cancellationToken).GetAwaiter().GetResult();
    }

    internal static async Task<PublicationLease> AcquireAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var members = paths.Select(CanonicalMember).Distinct(PublicationPaths.Comparer).Order(PublicationPaths.Comparer).ToArray();
        if (members.Length is < 1 or > 4)
        {
            throw new ArgumentException("A verification publication contains one to four stable members.", nameof(paths));
        }
        var lease = new PublicationLease();
        var elapsed = Stopwatch.StartNew();
        try
        {
            foreach (var member in members)
            {
                var directory = Path.GetDirectoryName(member) ??
                    throw new ArgumentException("A verification publication member must name a file, not a root directory.", nameof(paths));
                Directory.CreateDirectory(directory);
                var sidecar = Path.Combine(directory, ".sharpproof-publication-" +
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        OperatingSystem.IsWindows() ? member.ToUpperInvariant() : member))) + ".lock");
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        lease._members.Add(new FileStream(sidecar, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                        break;
                    }
                    catch (IOException) when (elapsed.ElapsedMilliseconds < TimeoutMilliseconds)
                    {
                        await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal static string[] ValidateMembers(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var members = paths.Select(CanonicalMember).ToArray();
        if (members.Length is < 1 or > 4 || members.Distinct(PublicationPaths.Comparer).Count() != members.Length)
        {
            throw new ArgumentException("Verification publication members must be distinct canonical paths.", nameof(paths));
        }
        return members;
    }

    internal static string CanonicalMember(string path)
    {
        return CanonicalMember(path, OperatingSystem.IsWindows());
    }

    internal static string CanonicalMember(string path, bool isWindows)
    {
        var full = Path.GetFullPath(path);
        if (isWindows)
        {
            // Win32 normalization can trim these aliases before full-path
            // validation sees them. Check the caller's spelling as well.
            if (path.Split(['\\', '/']).Any(static segment =>
                segment is not ("." or "..") && (segment.EndsWith('.') || segment.EndsWith(' '))))
            {
                throw new ArgumentException("SharpProof Windows paths must not have ambiguous trailing dots or spaces.");
            }
            ValidateWindowsMember(full);
        }
        var directory = Path.GetDirectoryName(full) ??
            throw new ArgumentException("A verification publication member must name a file, not a root directory.", nameof(path));
        var current = Path.GetPathRoot(directory)!;
        foreach (var segment in directory[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = new DirectoryInfo(Path.Combine(current, segment));
            current = next.Exists ? next.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next.FullName : next.FullName;
        }
        return Path.Combine(current, Path.GetFileName(full));
    }

    internal static void ValidateWindowsMember(string full,
        Func<string, bool>? isLocalNtfs = null, Func<string, bool>? isReparsePoint = null)
    {
        // Limit native publication to ordinary local NTFS paths. Reject aliases
        // that can bypass ownership checks or select a different lock sidecar.
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.Length < 3 ||
            full[1] != ':' || full[2] != '\\')
        {
            throw new ArgumentException("SharpProof Windows paths must be ordinary local NTFS paths.");
        }
        isLocalNtfs ??= IsLocalNtfs;
        isReparsePoint ??= IsReparsePoint;
        if (!isLocalNtfs(full[..3]))
        {
            throw new ArgumentException("SharpProof Windows paths require a fixed local NTFS volume.");
        }
        var current = full[..3];
        foreach (var segment in full[current.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var stem = segment.Split('.')[0];
            if (segment.Contains(':', StringComparison.Ordinal) || segment.Contains('~', StringComparison.Ordinal) || segment.EndsWith('.') || segment.EndsWith(' ') ||
                stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                    stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && char.IsDigit(stem[3])))
            {
                throw new ArgumentException("SharpProof Windows paths must not use device names, alternate streams, or short-name aliases.");
            }
            current = current.TrimEnd('\\') + "\\" + segment;
            if (isReparsePoint(current))
            {
                throw new ArgumentException("SharpProof Windows paths must not traverse reparse points.");
            }
        }
    }

    internal static bool IsLocalNtfs(string root)
    {
        var drive = new DriveInfo(root);
        return drive.DriveType == DriveType.Fixed && drive.DriveFormat == "NTFS";
    }

    internal static bool IsReparsePoint(string path)
    {
        return (File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    public void Dispose()
    {
        foreach (var member in _members)
        {
            member.Dispose();
        }
        _members.Clear();
    }
}
