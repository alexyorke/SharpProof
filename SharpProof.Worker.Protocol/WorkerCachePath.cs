namespace SharpProof.Worker.Protocol;

internal static class WorkerCachePath
{
    internal static string Resolve(
        string? configuredDirectory,
        string projectDirectory)
    {
        var root = Path.GetFullPath(projectDirectory);
        return Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine(root, "obj", "SharpProof", "cache")
                : Path.Combine(root, configuredDirectory!));
    }

    internal static bool IsSameOrDescendant(string path, string directory)
    {
        return (path + Path.DirectorySeparatorChar).StartsWith(
            directory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
