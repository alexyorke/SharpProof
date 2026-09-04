internal sealed class TempDirectory : IDisposable
{
    private readonly DirectoryInfo directory;

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

    internal string FullName => directory.FullName;

    public void Dispose()
    {
        if (directory.Exists)
        {
            directory.Delete(recursive: true);
        }
    }
}
