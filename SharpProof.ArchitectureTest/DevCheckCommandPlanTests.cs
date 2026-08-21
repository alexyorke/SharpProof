using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[NonParallelizable]
public sealed class DevCheckCommandPlanTests
{
    private static readonly string[] DebugCommandIds =
    [
        "restore", "solution-build", "semantic-tests",
        "package-restore", "package-test-build",
        "package-pack:SharpProof.Attributes",
        "package-pack:SharpProof.Package",
        "package-pack:SharpProof.Verifier",
        "performance-smoke"
    ];

    private static readonly string[] ReleaseCommandIds =
    [
        "restore", "solution-build", "semantic-tests",
        "package-pack:SharpProof.Attributes",
        "package-pack:SharpProof.Package",
        "package-pack:SharpProof.Verifier",
        "performance-smoke"
    ];

    [TestCase("Debug", 9, false)]
    [TestCase("Release", 7, true)]
    public async Task CommandPlanOwnsConfigurationSpecificBuildGraph(
        string configuration,
        int expectedCount,
        bool packageReuse)
    {
        using var document = await ReadPlan(configuration);
        var rows = document.RootElement.GetProperty("commands")
            .EnumerateArray().ToArray();

        Assert.That(rows, Has.Length.EqualTo(expectedCount));
        Assert.That(
            rows.Select(static row => row.GetProperty("id").GetString()),
            Is.EqualTo(configuration == "Debug"
                ? DebugCommandIds
                : ReleaseCommandIds));
        Assert.That(
            rows.Where(static row =>
                    row.GetProperty("id").GetString()!
                        .StartsWith("package-pack:", StringComparison.Ordinal))
                .Select(static row => row.GetProperty("noBuild").GetBoolean()),
            packageReuse ? Is.All.True : Is.All.False);
        Assert.That(
            rows.Single(static row =>
                    row.GetProperty("id").GetString() == "solution-build")
                .GetProperty("configuration").GetString(),
            Is.EqualTo(configuration));
        Assert.That(
            rows.Where(static row =>
                    row.GetProperty("id").GetString()!
                        .StartsWith("package-pack:", StringComparison.Ordinal))
                .Select(static row =>
                    row.GetProperty("configuration").GetString()),
            Is.All.EqualTo("Release"));
    }

    [Test]
    public async Task DeveloperCheckConsumesThePlanAuthority()
    {
        var root = FindRepositoryRoot();
        var script = await File.ReadAllTextAsync(Path.Combine(
            root, "scripts", "Invoke-SharpProofDevCheck.ps1"));

        Assert.That(script, Does.Contain("Get-SharpProofDevCheckPlan.ps1"));
        Assert.That(script, Does.Contain("[switch]$PlanOnly"));
        Assert.That(script, Does.Contain("package-test-build"));
        Assert.That(script, Does.Contain("NoBuild = $packagePlanReuse"));
    }

    private static async Task<JsonDocument> ReadPlan(string configuration)
    {
        var root = FindRepositoryRoot();
        var info = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(Path.Combine(
            root, "scripts", "Get-SharpProofDevCheckPlan.ps1"));
        info.ArgumentList.Add("-Configuration");
        info.ArgumentList.Add(configuration);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, await error);
        return JsonDocument.Parse(await output);
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
