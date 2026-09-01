internal sealed class TempDirectory : IDisposable
{
    private readonly DirectoryInfo directory;

    internal TempDirectory(string prefix)
    {
        directory = Directory.CreateTempSubdirectory(prefix);
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
