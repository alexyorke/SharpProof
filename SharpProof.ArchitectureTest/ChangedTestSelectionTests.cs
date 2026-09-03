using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[NonParallelizable]
public sealed class ChangedTestSelectionTests
{
    [TestCase("Directory.Build.props")]
    [TestCase("Directory.Packages.props")]
    [TestCase("Directory.Build.targets")]
    [TestCase("SharpProof.AnalyzerConsumer.props")]
    [TestCase("SharpProof.PackageMetadata.props")]
    [TestCase("SharpProof.Release.props")]
    public async Task RootBuildInputsSelectTheCompleteTestGraph(
        string changedInput)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.ChangedTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await CreateFixtureAsync(root, changedInput);
            await ArchitectureGitRepository.InitializeAsync(
                root,
                "test@example.invalid",
                "SharpProof Test");
            await RunAsync(root, "git", "add", ".");
            await RunAsync(root, "git", "commit", "--quiet", "-m", "baseline");
            await File.AppendAllTextAsync(
                Path.Combine(root, changedInput),
                "\n<!-- changed -->\n");

            var result = await RunAsync(
                root,
                "pwsh",
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(root, "scripts", "Invoke-SharpProofChangedTests.ps1"),
                "-ComparisonRef",
                "HEAD",
                "-PlanOnly");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Output,
                    Does.Contain(
                        "SharpProof.Product.Test\\SharpProof.Product.Test.csproj"));
                Assert.That(
                    result.Output,
                    Does.Contain(
                        "SharpProof.ArchitectureTest\\SharpProof.ArchitectureTest.csproj"));
                Assert.That(
                    result.Output,
                    Does.Contain(
                        "SharpProof.Package.Test (duration-aware sharder)"));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateFixtureAsync(
        string root,
        string changedInput)
    {
        var repository = TestRepository.FindRoot();
        foreach (var directory in new[]
                 {
                     "scripts",
                     "eng/acceptance",
                     "SharpProof.Product",
                     "SharpProof.Product.Test",
                     "SharpProof.ArchitectureTest",
                     "SharpProof.Package.Test"
                 })
        {
            Directory.CreateDirectory(Path.Combine(root, directory));
        }
        File.Copy(
            Path.Combine(
                repository,
                "scripts",
                "Invoke-SharpProofChangedTests.ps1"),
            Path.Combine(root, "scripts", "Invoke-SharpProofChangedTests.ps1"));
        File.Copy(
            Path.Combine(
                repository,
                "scripts",
                "SharpProof.ContainerExecution.psm1"),
            Path.Combine(root, "scripts", "SharpProof.ContainerExecution.psm1"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "eng", "acceptance", "contract.json"),
            "{\"automation\":{\"testProjectCpuDivisor\":1}}\n");
        await File.WriteAllTextAsync(
            Path.Combine(root, changedInput),
            "<Project />\n");
        await File.WriteAllTextAsync(
            Path.Combine(root, "SharpProof.sln"),
            string.Empty);
        await File.WriteAllTextAsync(
            Path.Combine(
                root,
                "SharpProof.Product",
                "SharpProof.Product.csproj"),
            "<Project />\n");
        await File.WriteAllTextAsync(
            Path.Combine(
                root,
                "SharpProof.Product.Test",
                "SharpProof.Product.Test.csproj"),
            """
            <Project>
              <ItemGroup>
                <ProjectReference Include="../SharpProof.Product/SharpProof.Product.csproj" />
              </ItemGroup>
            </Project>
            """);
        foreach (var project in new[]
                 {
                     "SharpProof.ArchitectureTest/SharpProof.ArchitectureTest.csproj",
                     "SharpProof.Package.Test/SharpProof.Package.Test.csproj"
                 })
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, project),
                "<Project />\n");
        }
    }

    private static async Task<ProcessResult> RunAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var combined = (await output) + Environment.NewLine + (await error);
        Assert.That(process.ExitCode, Is.Zero, combined);
        return new ProcessResult(combined);
    }

    private sealed record ProcessResult(string Output);
}
