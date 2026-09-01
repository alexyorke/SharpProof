using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class OpenCodePluginDependencyTests
{
    [Test]
    public async Task LocalPluginImportsHaveTrackedLockedDependencies()
    {
        var root = FindRepositoryRoot();
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, "opencode.json")));
        using var package = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, ".opencode", "package.json")));
        using var packageLock = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, ".opencode", "package-lock.json")));
        var dependencies = package.RootElement.GetProperty("dependencies");
        var lockPackages = packageLock.RootElement.GetProperty("packages");

        foreach (var plugin in config.RootElement.GetProperty("plugin")
                     .EnumerateArray()
                     .Select(static value => value.GetString()!))
        {
            var source = await File.ReadAllTextAsync(Path.Combine(root, plugin));
            foreach (Match import in Regex.Matches(
                         source,
                         "from\\s+[\"'](?<name>[^./][^\"']*)[\"']"))
            {
                var dependency = import.Groups["name"].Value;
                Assert.That(
                    dependencies.TryGetProperty(dependency, out _),
                    Is.True,
                    $"Missing dependency declaration for {dependency}.");
                Assert.That(
                    lockPackages.TryGetProperty(
                        "node_modules/" + dependency,
                        out _),
                    Is.True,
                    $"Missing lock entry for {dependency}.");
            }
        }

        var ignored = await File.ReadAllLinesAsync(Path.Combine(
            root,
            ".opencode",
            ".gitignore"));
        Assert.That(ignored, Does.Not.Contain("package.json"));
        Assert.That(ignored, Does.Not.Contain("package-lock.json"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
