using System.Diagnostics;
using System.Text;

namespace SharpProof.Test;

internal static class TestProcessSupport
{
    public static ProcessStartInfo CreatePowerShellStartInfo(
        string workingDirectory,
        bool redirectStandardInput = false,
        Encoding? encoding = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FindPowerShellExecutable(),
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (encoding != null)
        {
            if (redirectStandardInput) startInfo.StandardInputEncoding = encoding;
            startInfo.StandardOutputEncoding = encoding;
            startInfo.StandardErrorEncoding = encoding;
        }

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }

        return startInfo;
    }

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
