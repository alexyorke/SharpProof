using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[NonParallelizable]
public sealed class ChangedTestSelectionTests
{
    [TestCase("caf\u00e9.cs", false)]
    [TestCase("quoted\".cs", false)]
    [TestCase("line\nbreak.cs", false)]
    [TestCase("tab\tname.cs", false)]
    [TestCase("caf\u00e9.cs", true)]
    [TestCase("quoted\".cs", true)]
    [TestCase("line\nbreak.cs", true)]
    [TestCase("tab\tname.cs", true)]
    public async Task QuotedGitPathsSelectTheirConsumers(string filename, bool untracked)
    {
        using var temporary = new TempDirectory("SharpProof.ChangedTests-");
        var root = temporary.FullName;
        var changedInput = "SharpProof.Product/" + filename;
        await CreateFixtureAsync(root, changedInput);
        if (untracked)
        {
            File.Delete(Path.Combine(root, changedInput));
        }
        await ArchitectureGitRepository.InitializeAsync(root, "test@example.invalid", "SharpProof Test");
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(root, "git", "add", "."));
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(root, "git", "commit", "--quiet", "-m", "baseline"));
        await File.AppendAllTextAsync(Path.Combine(root, changedInput), "\n// changed\n");
        var result = await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(root, "pwsh", "-NoLogo", "-NoProfile", "-File",
                Path.Combine(root, "scripts", "Invoke-SharpProofChangedTests.ps1"),
                "-ComparisonRef", "HEAD", "-PlanOnly"));
        Assert.That(result.Output, Does.Contain("SharpProof.Product.Test\\SharpProof.Product.Test.csproj"));
    }

    [TestCase("<ProjectReference Include=\"../SharpProof.Product/SharpProof.Product.csproj;../SharpProof.Effects.Test/SharpProof.Effects.Test.csproj\" />")]
    [TestCase("<ProjectReference Include=\"../SharpProof.Product/SharpProof.Product.csproj\" /><ProjectReference Update=\"../SharpProof.Product/SharpProof.Product.csproj\" PrivateAssets=\"all\" />")]
    public async Task ProjectReferenceItemFormsPreserveDependentSelection(string references)
    {
        using var temporary = new TempDirectory("SharpProof.ChangedTests-");
        var root = temporary.FullName;
        const string changedInput = "SharpProof.Product/Source.cs";
        await CreateFixtureAsync(root, changedInput);
        await File.WriteAllTextAsync(
            Path.Combine(root, "SharpProof.Product.Test", "SharpProof.Product.Test.csproj"),
            $"<Project><ItemGroup>{references}</ItemGroup></Project>");
        await ArchitectureGitRepository.InitializeAsync(
            root, "test@example.invalid", "SharpProof Test");
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(root, "git", "add", "."));
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(
                root, "git", "commit", "--quiet", "-m", "baseline"));
        await File.AppendAllTextAsync(Path.Combine(root, changedInput), "\n// changed\n");

        var result = await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(
                root, "pwsh", "-NoLogo", "-NoProfile", "-File",
                Path.Combine(root, "scripts", "Invoke-SharpProofChangedTests.ps1"),
                "-ComparisonRef", "HEAD", "-PlanOnly"));
        Assert.That(result.Output, Does.Contain(
            "SharpProof.Product.Test\\SharpProof.Product.Test.csproj"));
    }

    [Test]
    public async Task RenamedSourceSelectsBothOldAndNewConsumers()
    {
        using var temporary = new TempDirectory("SharpProof.ChangedTests-");
        var root = temporary.FullName;
        const string original = "SharpProof.Product/Source.cs";
        const string destination = "SharpProof.Effects.Test/Source.cs";
        await CreateFixtureAsync(root, original);
        await ArchitectureGitRepository.InitializeAsync(
            root, "test@example.invalid", "SharpProof Test");
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(root, "git", "add", "."));
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(
                root, "git", "commit", "--quiet", "-m", "baseline"));
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(root, "git", "mv", original, destination));
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(root, "git", "config", "diff.renames", "true"));

        var result = await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(
                root, "pwsh", "-NoLogo", "-NoProfile", "-File",
                Path.Combine(root, "scripts", "Invoke-SharpProofChangedTests.ps1"),
                "-ComparisonRef", "HEAD", "-PlanOnly"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Output, Does.Contain(
                "SharpProof.Product.Test\\SharpProof.Product.Test.csproj"));
            Assert.That(result.Output, Does.Contain(
                "SharpProof.Effects.Test\\SharpProof.Effects.Test.csproj"));
        }
    }

    [TestCase("../Shared/First.cs;../Shared/Second.cs", "Shared/Second.cs", false, true)]
    [TestCase("../Shared/First.cs;../Shared/Second.cs", "Shared/First.cs", false, true)]
    [TestCase("..\\Shared\\First.cs;..\\Shared\\Second.cs", "Shared/Second.cs", false, true)]
    [TestCase("../Shared/**/*.cs", "Shared/First.cs", false, true)]
    [TestCase("../Shared/**/*.cs", "Shared/Nested/First.cs", false, true)]
    [TestCase("../Shared/**/*.cs", "Shared/Nested/First.cs", true, true)]
    [TestCase("..\\Shared\\**\\*.cs", "Shared/Nested/First.cs", false, true)]
    [TestCase("../Shared/File?.cs", "Shared/File1.cs", false, true)]
    [TestCase("../Shared/*.cs", "Shared/Nested/First.cs", false, false)]
    [TestCase("../Shared/**/*.cs", "Shared/First.txt", false, false)]
    public async Task LinkedCompileInputsSelectTheirConsumers(
        string includes,
        string changedInput,
        bool deleted,
        bool selectsConsumer)
    {
        using var temporary = new TempDirectory("SharpProof.ChangedTests-");
        var root = temporary.FullName;
        await CreateFixtureAsync(root, changedInput);
        await File.WriteAllTextAsync(
            Path.Combine(root, "SharpProof.Product", "SharpProof.Product.csproj"),
            $"<Project><ItemGroup><Compile Include=\"{includes}\" /></ItemGroup></Project>");
        var evaluated = await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(
                root, "dotnet", "msbuild",
                Path.Combine(root, "SharpProof.Product", "SharpProof.Product.csproj"),
                "-getItem:Compile", "-nologo"));
        using var inventory = JsonDocument.Parse(evaluated.Output);
        Assert.That(inventory.RootElement.GetProperty("Items").GetProperty("Compile")
            .EnumerateArray().Any(item => string.Equals(
                item.GetProperty("FullPath").GetString(), Path.Combine(root, changedInput),
                StringComparison.Ordinal)), Is.EqualTo(selectsConsumer));
        await ArchitectureGitRepository.InitializeAsync(
            root, "test@example.invalid", "SharpProof Test");
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(root, "git", "add", "."));
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(
                root, "git", "commit", "--quiet", "-m", "baseline"));
        if (deleted)
        {
            File.Delete(Path.Combine(root, changedInput));
        }
        else
        {
            await File.AppendAllTextAsync(Path.Combine(root, changedInput), "\n// changed\n");
        }

        var result = await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(
                root, "pwsh", "-NoLogo", "-NoProfile", "-File",
                Path.Combine(root, "scripts", "Invoke-SharpProofChangedTests.ps1"),
                "-ComparisonRef", "HEAD", "-PlanOnly"));

        Assert.That(result.Output.Contains(
            "SharpProof.Product.Test\\SharpProof.Product.Test.csproj",
            StringComparison.Ordinal), Is.EqualTo(selectsConsumer));
        Assert.That(result.Output, Does.Not.Contain(
            "SharpProof.Effects.Test\\SharpProof.Effects.Test.csproj"));
    }

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

    [TestCase("catalog-only")]
    [TestCase("catalog-plus-unrelated-test")]
    [TestCase("catalog-plus-generated-output")]
    public async Task DeclarativeModelCatalogSelectsItsValidationConsumer(
        string scenario)
    {
        using var temporary = new TempDirectory("SharpProof.ChangedTests-");
        var root = temporary.FullName;
        const string catalogPath = "SharpProof.DeclarativeModels.catalog.json";
        await CreateFixtureAsync(root, catalogPath);
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
            Path.Combine(root, catalogPath),
            "\nchanged\n");

        switch (scenario)
        {
            case "catalog-plus-unrelated-test":
                await File.AppendAllTextAsync(
                    Path.Combine(
                        root,
                        "SharpProof.Effects.Test",
                        "SharpProof.Effects.Test.csproj"),
                    "<!-- unrelated -->\n");
                break;
            case "catalog-plus-generated-output":
                await File.WriteAllTextAsync(
                    Path.Combine(
                        root,
                        "SharpProof.Product",
                        "DeclarativeModels.generated.cs"),
                    "// generated change\n");
                break;
            case "catalog-only":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

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

        Assert.That(
            result.Output,
            Does.Contain(
                "SharpProof.ArchitectureTest\\SharpProof.ArchitectureTest.csproj"));
    }

    [Test]
    public async Task DeletedIndexedTestProjectSelectsSurvivingTestsAndShardsPackage()
    {
        using var temporary = new TempDirectory("SharpProof.ChangedTests-");
        var root = temporary.FullName;
        const string changedPath =
            "SharpProof.Effects.Test/SharpProof.Effects.Test.csproj";
        await CreateFixtureAsync(root, changedPath);
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
        File.Delete(Path.Combine(
            root,
            "SharpProof.Effects.Test",
            "SharpProof.Effects.Test.csproj"));

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
            Assert.That(result.ExitCode, Is.Zero, result.Error);
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
            Assert.That(result.Output, Does.Not.Contain(
                "SharpProof.Effects.Test\\SharpProof.Effects.Test.csproj"));
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
                     "SharpProof.Effects.Test",
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
            Path.Combine(root, "SharpProof.slnx"),
            "<Solution />\n");
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
                      "SharpProof.Effects.Test/SharpProof.Effects.Test.csproj",
                      "SharpProof.Package.Test/SharpProof.Package.Test.csproj"
                 })
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, project),
                "<Project />\n");
        }
    }

}
