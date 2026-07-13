namespace SharpProof.Test;

internal static class TestProcessSupport
{
    public static string FindPowerShellExecutable()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "pwsh.exe", "pwsh", "powershell.exe", "powershell" }
            : new[] { "pwsh" };

        foreach (var candidate in candidates)
        {
            var path = FindExecutableOnPath(candidate);
            if (!string.IsNullOrWhiteSpace(path)) return path;
        }

        return OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
    }

    private static string FindExecutableOnPath(string fileName)
    {
        foreach (var directory in
                 (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        return string.Empty;
    }
}
