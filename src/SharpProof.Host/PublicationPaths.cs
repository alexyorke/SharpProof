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
        var directories = ImmutableArray.Create(worker,
            PublicationLease.CanonicalMember(workerTarget?.FullName ?? worker))
            .Select(RuntimeDirectory).OfType<string>().Distinct(Comparer).ToImmutableArray();
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

    private static string? RuntimeDirectory(string workerPath)
    {
        ArgumentNullException.ThrowIfNull(workerPath);
        var directory = new DirectoryInfo(Path.GetDirectoryName(workerPath) ??
            throw new InvalidDataException("The worker runtime path must name a file."));
        // Package runtimes include native payloads beside tools/net10, even
        // when the managed payload is missing from a partial installation.
        if (Comparer.Equals(directory.Name, "net10") &&
            directory.Parent is { } parent && Comparer.Equals(parent.Name, "tools"))
        {
            return parent.FullName;
        }
        // An inactive verifier may use a missing standalone override in the
        // project directory. It does not establish a runtime directory there.
        return File.Exists(workerPath) ? directory.FullName : null;
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
