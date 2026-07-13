namespace SharpProof.Symbolic;

internal static class SymbolicSourceFile
{
    public static (string Text, string FilePath) Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Source file does not exist.", filePath);

        return (File.ReadAllText(filePath), Path.GetFullPath(filePath));
    }
}
