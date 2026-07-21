using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Test;

internal static class ReadmeExampleFixture {
    private static readonly Lazy<string> RepositoryRoot = new(AnalyzerTestHost.GetRepositoryRoot);

    public static string GetRepositoryRoot() {
        return RepositoryRoot.Value;
    }

    public static string GetRelativeExamplePath(string exampleId, string fileName) {
        return Path.Combine("docs", "readme-examples", exampleId, fileName)
            .Replace('\\', '/');
    }

    public static string GetAbsoluteExamplePath(string exampleId, string fileName) {
        return Path.Combine(
            RepositoryRoot.Value,
            "docs",
            "readme-examples",
            exampleId,
            fileName);
    }

    public static string LoadExampleSource(string exampleId) {
        return Normalize(File.ReadAllText(GetAbsoluteExamplePath(exampleId, "input.cs")));
    }

    public static void AssertOutputMatchesSnapshot(string exampleId, string actual) {
        var normalizedActual = NormalizeForSnapshot(actual);
        var outputPath = GetAbsoluteExamplePath(exampleId, "output.txt");
        if (ShouldRegenerateSnapshots()) {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, normalizedActual);
            Assert.Pass("Regenerated README example snapshot: " + exampleId);
        }

        var expected = NormalizeForSnapshot(File.ReadAllText(outputPath));
        Assert.That(normalizedActual, Is.EqualTo(expected));
    }

    public static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics) {
        var builder = new StringBuilder();
        foreach (var diagnostic in diagnostics
                     .OrderBy(static item => item.Location.SourceSpan.Start)
                     .ThenBy(static item => item.Id, StringComparer.Ordinal)) {
            var lineSpan = diagnostic.Location.GetLineSpan();
            var path = string.IsNullOrWhiteSpace(lineSpan.Path)
                ? "<no-location>"
                : lineSpan.Path.Replace('\\', '/');
            var line = lineSpan.StartLinePosition.Line + 1;
            var column = lineSpan.StartLinePosition.Character + 1;
            builder.Append(diagnostic.Id);
            builder.Append(' ');
            builder.Append(diagnostic.Severity);
            builder.Append(' ');
            builder.Append(path);
            builder.Append(':');
            builder.Append(line.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(column.ToString(CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.AppendLine(diagnostic.GetMessage(CultureInfo.InvariantCulture));
        }

        return Normalize(builder.ToString());
    }

    public static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunReadmeGeneratorAsync(
        bool verifyOnly) {
        var startInfo = new ProcessStartInfo {
            FileName = ResolvePowerShellPath(),
            WorkingDirectory = RepositoryRoot.Value,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine("scripts", "Generate-Readme.ps1"));
        if (verifyOnly) startInfo.ArgumentList.Add("-Verify");

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start README generator.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            process.Kill(true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            throw;
        }

        return (
            process.ExitCode,
            Normalize(await outputTask.ConfigureAwait(false)),
            Normalize(await errorTask.ConfigureAwait(false)));
    }

    public static string Normalize(string text) {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.TrimEnd('\n') + "\n";
    }

    public static string NormalizeForSnapshot(string text) {
        var normalized = Normalize(text);
        var root = RepositoryRoot.Value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        normalized = normalized.Replace(root + Path.DirectorySeparatorChar, string.Empty,
            StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace(root + Path.AltDirectorySeparatorChar, string.Empty,
            StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace('\\', '/');
        normalized = Regex.Replace(normalized, @"position=\d+", "position=<offset>");
        normalized = Regex.Replace(normalized, @"span=\d+-\d+", "span=<offset-range>");
        normalized = Regex.Replace(normalized, @"Node: ([^\r\n]+?) \d+-\d+", "Node: $1 <offset-range>");
        normalized = Regex.Replace(normalized, @"\b([A-Za-z_][A-Za-z0-9_]*)#\d+\b", "$1#<version>");
        return normalized;
    }

    private static string ResolvePowerShellPath() {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell",
                "7",
                "pwsh.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            "powershell.exe"
        };

        foreach (var candidate in candidates) {
            if (Path.IsPathRooted(candidate)) {
                if (File.Exists(candidate)) return candidate;

                continue;
            }

            return candidate;
        }

        throw new FileNotFoundException("Could not locate PowerShell.");
    }

    private static bool ShouldRegenerateSnapshots() {
        var value = Environment.GetEnvironmentVariable("SHARPPROOF_REGENERATE_EXAMPLE_OUTPUTS");
        return string.Equals(value, "1", StringComparison.Ordinal) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
