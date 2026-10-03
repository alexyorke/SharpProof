namespace SharpProof.Host;

internal static class PublicationPaths
{
    internal static void ValidateWorkerRuntime(IEnumerable<string> paths, string? workerPath)
    {
        if (string.IsNullOrWhiteSpace(workerPath))
        {
            return;
        }
        var worker = PublicationLease.CanonicalMember(workerPath);
        if (!File.Exists(worker))
        {
            return;
        }
        var workerFile = new FileInfo(worker);
        var workerTarget = workerFile.LinkTarget == null ? null : workerFile.ResolveLinkTarget(returnFinalTarget: true);
        var directories = new[] { Path.GetDirectoryName(worker)!,
            Path.GetDirectoryName(PublicationLease.CanonicalMember(workerTarget?.FullName ?? worker))! }
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (var path in paths)
        {
            var member = PublicationLease.CanonicalMember(path);
            var file = new FileInfo(member);
            var target = file.LinkTarget == null ? null : file.ResolveLinkTarget(returnFinalTarget: true);
            if (directories.Any(directory => IsWithin(member, directory) ||
                target != null && IsWithin(PublicationLease.CanonicalMember(target.FullName), directory)))
            {
                throw new ArgumentException("SharpProof I/O paths must not be inside the worker runtime.", nameof(paths));
            }
        }
    }

    internal static bool IsWithin(string path, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        return string.Equals(path, directory, StringComparison.Ordinal) || path.StartsWith(root, StringComparison.Ordinal);
    }
}
