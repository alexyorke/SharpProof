internal sealed class TempDirectory : IDisposable
{
    private readonly DirectoryInfo directory;
    private readonly string? expectedRoot;
    private readonly string? errorMessage;

    internal TempDirectory(string prefix)
    {
        directory = Directory.CreateTempSubdirectory(prefix);
    }

    internal TempDirectory(string prefix, string parentDirectory)
    {
        var parent = Directory.CreateDirectory(parentDirectory);
        directory = parent.CreateSubdirectory(
            prefix + Guid.NewGuid().ToString("N"));
    }

    private TempDirectory(
        DirectoryInfo directory,
        string expectedRoot,
        string errorMessage)
    {
        this.directory = directory;
        this.expectedRoot = expectedRoot;
        this.errorMessage = errorMessage;
    }

    internal static TempDirectory CreateOwned(
        string rootName,
        string prefix,
        string errorMessage = "Refusing to remove an unexpected test directory.")
    {
        if (string.IsNullOrWhiteSpace(rootName) ||
            Path.IsPathRooted(rootName) ||
            rootName is "." or ".." ||
            rootName.Contains(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal) ||
            rootName.Contains(
                Path.AltDirectorySeparatorChar.ToString(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The temporary root name must be one relative directory segment.",
                nameof(rootName));
        }

        var expectedRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            rootName));
        var parent = Directory.CreateDirectory(Path.GetTempPath());
        var root = parent.CreateSubdirectory(rootName);
        var directory = root.CreateSubdirectory(
            prefix + Guid.NewGuid().ToString("N"));
        return new TempDirectory(directory, expectedRoot, errorMessage);
    }

    internal string FullName => directory.FullName;

    public void Dispose()
    {
        if (directory.Exists)
        {
            if (expectedRoot is not null)
            {
                var resolved = Path.GetFullPath(directory.FullName);
                var relative = Path.GetRelativePath(expectedRoot, resolved);
                if (Path.IsPathRooted(relative) ||
                    relative == "." ||
                    relative == ".." ||
                    relative.StartsWith(
                        ".." + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(errorMessage);
                }

                foreach (var file in Directory.EnumerateFiles(
                             resolved,
                             "*",
                             SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
            }

            directory.Delete(recursive: true);
        }
    }
}
