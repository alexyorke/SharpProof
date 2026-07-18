using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class RepositoryArchitectureTests
{
    [Test]
    public void ProductionProjects_HaveValidDependencyDirectionAndModuleOwnership()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var script = Path.Combine(root, "scripts", "Get-SharpProofProductionMetrics.ps1");
        var startInfo = TestProcessSupport.CreatePowerShellStartInfo(root);
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-Json");
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.That(process.ExitCode, Is.EqualTo(0), standardError);
        using var document = JsonDocument.Parse(standardOutput);
        var report = document.RootElement;
        Assert.That(report.GetProperty("unassignedFiles").GetArrayLength(), Is.Zero);
        Assert.That(report.GetProperty("ambiguousFiles").GetArrayLength(), Is.Zero);
        Assert.That(report.GetProperty("dependencyViolations").GetArrayLength(), Is.Zero);
    }

    [Test]
    public void Projects_DoNotCompileSourceFromAnotherProject()
    {
        var root = ReadmeExampleFixture.GetRepositoryRoot();
        var violations = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path, root))
            .SelectMany(GetExternalCompileItems)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.That(violations, Is.Empty,
            "Production source must be owned by a project and shared through a ProjectReference.");
    }

    private static IEnumerable<string> GetExternalCompileItems(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var document = XDocument.Load(projectPath);
        foreach (var element in document.Descendants("Compile"))
        {
            var include = element.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;

            var sourcePath = Path.GetFullPath(Path.Combine(projectDirectory, include));
            if (!sourcePath.StartsWith(projectDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                yield return $"{Path.GetFileName(projectPath)} -> {include}";
        }
    }

    private static bool IsIgnored(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith(".", StringComparison.Ordinal) ||
               relative.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase);
    }

}
