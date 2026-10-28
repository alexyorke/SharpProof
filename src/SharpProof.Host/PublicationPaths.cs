using System.Collections.Immutable;
namespace SharpProof.Host;

internal static class PublicationPaths
{
    // macOS commonly uses case-insensitive APFS. Treat case variants as one
    // publication even on case-sensitive volumes rather than permit aliasing.
    internal static bool IgnoreCase => Comparison == StringComparison.OrdinalIgnoreCase;
    internal static StringComparer Comparer => IgnoreCase
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    internal static StringComparison Comparison => PathComparison(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

    internal static StringComparison PathComparison(bool isWindows, bool isMacOs)
    {
        return isWindows || isMacOs ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    internal static void ValidateWorkerRuntime(IEnumerable<string> paths, string? workerPath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(workerPath))
        {
            return;
        }
        var worker = PublicationLease.CanonicalMember(workerPath);
        var workerFile = new FileInfo(worker);
        var workerTarget = workerFile.LinkTarget == null ? null : workerFile.ResolveLinkTarget(returnFinalTarget: true);
        var directories = ImmutableArray.Create(Path.GetDirectoryName(worker)!,
            Path.GetDirectoryName(PublicationLease.CanonicalMember(workerTarget?.FullName ?? worker)) ??
                throw new InvalidDataException("The worker runtime link target must name a file."))
            .Select(RuntimeDirectory).Distinct(Comparer).ToImmutableArray();
        foreach (var path in paths)
        {
            var member = PublicationLease.CanonicalMember(path);
            var file = new FileInfo(member);
            var target = file.LinkTarget == null ? null : file.ResolveLinkTarget(returnFinalTarget: true);
            if (directories.Any(directory => directory is null ? throw new System.ArgumentNullException(nameof(directory)) : (IsWithin(member, directory, Comparison) ||
                target != null && IsWithin(PublicationLease.CanonicalMember(target.FullName), directory, Comparison))))
            {
                throw new ArgumentException("SharpProof I/O paths must not be inside the worker runtime.", nameof(paths));
            }
        }
    }

    private static string RuntimeDirectory(string managedDirectory)
    {
        ArgumentNullException.ThrowIfNull(managedDirectory);
        // Package runtimes include native payloads beside tools/net10. Other
        // worker layouts retain their existing containing-directory boundary.
        var directory = new DirectoryInfo(managedDirectory);
        return Comparer.Equals(directory.Name, "net10") &&
            directory.Parent is { } parent && Comparer.Equals(parent.Name, "tools")
            ? parent.FullName : directory.FullName;
    }

    internal static bool IsWithin(string path, string directory)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(directory);
        return IsWithin(path, directory, Comparison);
    }

    internal static bool IsWithin(string path, string directory, StringComparison comparison)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        System.ArgumentNullException.ThrowIfNull(path);
        var root = Path.TrimEndingDirectorySeparator(directory);
        if (!Path.EndsInDirectorySeparator(root))
        {
            root += Path.DirectorySeparatorChar;
        }
        return string.Equals(path, directory, comparison) || path.StartsWith(root, comparison);
    }
}
