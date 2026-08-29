using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class BuildSchedulingTests
{
    private static readonly string[] BuildSolution =
        ["build", "SharpProof.sln", "--no-restore", "-graphBuild"];
    private static readonly string[] TestFilter =
        ["test", "SharpProof.Dev.Tests.slnf", "--no-build", "-graphBuild"];
    private static readonly string[] BuildProject =
        ["build", "SharpProof.Ir/SharpProof.Ir.csproj"];
    private static readonly string[] RestoreSolution =
        ["restore", "SharpProof.sln"];
    private static readonly string[] Existing =
        ["build", "SharpProof.sln", "-graphBuild"];

    [Test]
    public async Task SolutionBuildsAndTestsUseStaticGraphScheduling()
    {
        var root = FindRepositoryRoot();
        var module = Path.Combine(
            root, "scripts", "SharpProof.ContainerExecution.psm1");
        var escapedModule = module.Replace("'", "''", StringComparison.Ordinal);
        var command = $$"""
            Import-Module '{{escapedModule}}' -Force
            [ordered]@{
                buildSolution = @(Add-SharpProofStaticGraphArgument -Arguments @('build', 'SharpProof.sln', '--no-restore'))
                testFilter = @(Add-SharpProofStaticGraphArgument -Arguments @('test', 'SharpProof.Dev.Tests.slnf', '--no-build'))
                buildProject = @(Add-SharpProofStaticGraphArgument -Arguments @('build', 'SharpProof.Ir/SharpProof.Ir.csproj'))
                restoreSolution = @(Add-SharpProofStaticGraphArgument -Arguments @('restore', 'SharpProof.sln'))
                existing = @(Add-SharpProofStaticGraphArgument -Arguments @('build', 'SharpProof.sln', '-graphBuild'))
            } | ConvertTo-Json -Compress
            """;

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
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(command);

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, await error);

        using var document = JsonDocument.Parse(await output);
        var rootElement = document.RootElement;
        Assert.That(Read(rootElement, "buildSolution"),
            Is.EqualTo(BuildSolution));
        Assert.That(Read(rootElement, "testFilter"),
            Is.EqualTo(TestFilter));
        Assert.That(Read(rootElement, "buildProject"),
            Is.EqualTo(BuildProject));
        Assert.That(Read(rootElement, "restoreSolution"),
            Is.EqualTo(RestoreSolution));
        Assert.That(Read(rootElement, "existing"),
            Is.EqualTo(Existing));

        var container = await File.ReadAllTextAsync(Path.Combine(
            root, "scripts", "Invoke-SharpProofContainer.ps1"));
        var wrapper = await File.ReadAllTextAsync(Path.Combine(
            root, "scripts", "Invoke-SharpProofDotnet.ps1"));
        Assert.That(container,
            Does.Contain("Add-SharpProofStaticGraphArgument"));
        Assert.That(wrapper,
            Does.Contain("Add-SharpProofStaticGraphArgument"));
    }

    private static string[] Read(JsonElement root, string property)
    {
        return root.GetProperty(property).EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
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
