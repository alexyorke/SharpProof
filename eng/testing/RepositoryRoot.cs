internal static class RepositoryRoot
{
    internal static string? Find(string? start, string marker, string secondaryMarker)
    {
        var directory = new DirectoryInfo(start ?? System.AppContext.BaseDirectory);
        while (directory != null &&
               (!File.Exists(Path.Combine(directory.FullName, marker)) ||
                !File.Exists(Path.Combine(directory.FullName, secondaryMarker))))
        {
            directory = directory.Parent;
        }
        return directory?.FullName;
    }
}
