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
    [TestCase("eng/testing/TestRepository.cs")]
    public async Task RootBuildInputsSelectTheCompleteTestGraph(
        string changedInput)
    {
        using var temporary = new TempDirectory("SharpProof.ChangedTests-");
        var root = temporary.FullName;
        await CreateFixtureAsync(root, changedInput);
        await ArchitectureGitRepository.InitializeAsync(
            root,
            "test@example.invalid",
            "SharpProof Test");
        await ArchitectureRepository.RunProcessAsync(root, "git", "add", ".");
        await ArchitectureRepository.RunProcessAsync(
            root,
            "git",
            "commit",
            "--quiet",
            "-m",
            "baseline");
        await File.AppendAllTextAsync(
            Path.Combine(root, changedInput),
            "\n<!-- changed -->\n");

        var result = await ArchitectureRepository.RunProcessAsync(
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

    private static async Task CreateFixtureAsync(
        string root,
        string changedInput)
    {
        var repository = TestRepository.FindRoot();
        var changedPath = Path.Combine(root, changedInput);
        Directory.CreateDirectory(Path.GetDirectoryName(changedPath)!);
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
        await File.WriteAllTextAsync(changedPath, "<Project />\n");
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

}
