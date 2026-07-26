namespace SharpProof.V2Gates;

public static class RepositoryLayout {
    public static string FindRoot(string? start = null) {
        var directory = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")) &&
                File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "eng",
                        "acceptance",
                        "v2",
                        "contract.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate the SharpProof repository root.");
    }
}
