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

    internal static void ValidateNoReparsePoints(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            for (string? current = Path.GetFullPath(path);
                 current is not null;
                 current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new ArgumentException(
                            "SharpProof paths must not traverse reparse points.",
                            nameof(paths));
                    }
                }
                catch (FileNotFoundException)
                {
                }
                catch (DirectoryNotFoundException)
                {
                }
            }
        }
    }
}
