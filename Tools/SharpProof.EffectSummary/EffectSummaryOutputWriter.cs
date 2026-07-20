internal static class EffectSummaryOutputWriter {
    public static void WriteDocument(EffectSummaryDocument document, string? outputPath) {
        var json = JsonSerializer.Serialize(
            document,
            new JsonSerializerOptions {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            });

        if (string.IsNullOrWhiteSpace(outputPath)) {
            Console.WriteLine(json);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, json);
    }

    public static void WriteManifestIfChanged(string manifestPath, IEnumerable<string> paths) {
        var normalizedPath = Path.GetFullPath(manifestPath);
        var content = string.Join(
                          "\n",
                          paths
                              .Select(Path.GetFullPath)
                              .Distinct(OperatingSystem.IsWindows()
                                  ? StringComparer.OrdinalIgnoreCase
                                  : StringComparer.Ordinal)
                              .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) +
                      "\n";
        if (File.Exists(normalizedPath) &&
            string.Equals(File.ReadAllText(normalizedPath), content, StringComparison.Ordinal))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedPath)!);
        File.WriteAllText(normalizedPath, content, new UTF8Encoding(false));
    }
}
