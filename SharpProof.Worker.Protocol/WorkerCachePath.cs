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
        // Both sides are separator-terminated so that "C:\toolsX" is not treated
        // as living under "C:\tools". A drive root already ends in a separator,
        // and unconditionally appending one there built the prefix "C:\\", which
        // no path could ever start with -- silently disabling containment checks
        // for a worker deployed at the root of a drive.
        return Terminate(path).StartsWith(
            Terminate(directory),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Terminate(string value)
    {
        return value.Length != 0 &&
            (value[value.Length - 1] == Path.DirectorySeparatorChar ||
             value[value.Length - 1] == Path.AltDirectorySeparatorChar)
            ? value
            : value + Path.DirectorySeparatorChar;
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
