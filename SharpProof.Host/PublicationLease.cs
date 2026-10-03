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
        var members = paths.Select(CanonicalMember).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
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
                var directory = Path.GetDirectoryName(member)!;
                Directory.CreateDirectory(directory);
                var sidecar = Path.Combine(directory, ".sharpproof-publication-" +
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(member))) + ".lock");
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
        var members = paths.Select(CanonicalMember).ToArray();
        if (members.Length is < 1 or > 4 || members.Distinct(StringComparer.Ordinal).Count() != members.Length)
        {
            throw new ArgumentException("Verification publication members must be distinct canonical paths.", nameof(paths));
        }
        return members;
    }

    internal static string CanonicalMember(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        var current = Path.GetPathRoot(directory)!;
        foreach (var segment in directory[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = new DirectoryInfo(Path.Combine(current, segment));
            current = next.Exists ? next.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next.FullName : next.FullName;
        }
        return Path.Combine(current, Path.GetFileName(full));
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
