using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ProductionInventoryAuthorityTests
{
    [Test]
    public async Task InventoryBindsParseGeneratorAndGeneratedAuthorities()
    {
        var repository = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-production-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            await InitializeRepositoryAsync(repository);
            await WriteFixtureAsync(repository);
            await CommitAllAsync(repository, "inventory fixture");

            var baseline = await RunInventoryAsync(repository);
            var baselineSource = GetString(baseline, "sourceUniverseSha256");

            var projectPath = Path.Combine(repository, "Project", "Project.csproj");
            var project = await File.ReadAllTextAsync(projectPath);
            await File.WriteAllTextAsync(
                projectPath,
                project.Replace(
                    "<DefineConstants>BASE</DefineConstants>",
                    "<DefineConstants>BASE;MUTATED_PARSE</DefineConstants>",
                    StringComparison.Ordinal));
            var parseMutation = await RunInventoryAsync(repository);
            Assert.That(
                GetString(parseMutation, "sourceUniverseSha256"),
                Is.Not.EqualTo(baselineSource),
                "A self-defined parse/preprocessor mutation must change the source authority.");

            var generatorPath = Path.Combine(
                repository,
                "scripts",
                "Generate-Fixture.ps1");
            await File.AppendAllTextAsync(generatorPath, "# generator mutation\n");
            var generatorMutation = await RunInventoryAsync(repository);
            Assert.That(
                GetString(generatorMutation, "sourceUniverseSha256"),
                Is.Not.EqualTo(GetString(parseMutation, "sourceUniverseSha256")),
                "A generator-input mutation must change the source authority.");

            var manifestPath = Path.Combine(
                repository,
                "eng",
                "generated",
                "approved-outputs.v1.json");
            await File.WriteAllTextAsync(
                manifestPath,
                "{\"schemaVersion\":1,\"outputs\":[]}\n");
            var generatedMutation = await RunInventoryProcessAsync(repository);
            Assert.That(
                generatedMutation.ExitCode,
                Is.Not.Zero,
                "Removing a generated output from the approved manifest must fail closed.");

            await File.WriteAllTextAsync(
                manifestPath,
                "{\"schemaVersion\":1,\"outputs\":[\"Project/Generated.g.cs\"]}\n");
            var authority = await RunInventoryAsync(repository);
            await File.WriteAllTextAsync(
                Path.Combine(repository, "authority.json"),
                authority.RootElement.GetRawText() + "\n");
            await File.WriteAllTextAsync(
                Path.Combine(repository, "contract.json"),
                "{\"trustedKernel\":{\"paths\":[\"Project/Foreign.cs\"]},\"trustedComputingBase\":{\"components\":[]}}\n");
            await File.WriteAllTextAsync(
                Path.Combine(repository, "tcb-probe.ps1"),
                "Set-StrictMode -Version Latest\n" +
                ". (Join-Path $PSScriptRoot 'scripts/Get-SharpProofTcbPaths.ps1')\n" +
                "$authority = Get-Content (Join-Path $PSScriptRoot 'authority.json') -Raw | ConvertFrom-Json\n" +
                "$contract = Get-Content (Join-Path $PSScriptRoot 'contract.json') -Raw | ConvertFrom-Json\n" +
                "Get-SharpProofTcbPaths -Contract $contract -ProductionInventory $authority | Out-Null\n");
            var tcbMutation = await RunAsync(
                repository,
                "pwsh",
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-File",
                Path.Combine(repository, "tcb-probe.ps1"));
            Assert.That(
                tcbMutation.ExitCode,
                Is.Not.Zero,
                "A TCB source outside the evaluated Compile universe must fail closed.");
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public async Task InventoryRejectsMissingRepositoryAnalyzer()
    {
        var repository = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-production-inventory-analyzer-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            await InitializeRepositoryAsync(repository);
            await WriteFixtureAsync(repository);
            var projectPath = Path.Combine(
                repository,
                "Project",
                "Project.csproj");
            var project = await File.ReadAllTextAsync(projectPath);
            await File.WriteAllTextAsync(
                projectPath,
                project.Replace(
                    "    <Compile Include=\"**/*.cs\" Exclude=\"bin/**/*.cs;obj/**/*.cs\" />",
                    "    <Compile Include=\"**/*.cs\" Exclude=\"bin/**/*.cs;obj/**/*.cs\" />\n" +
                    "    <Analyzer Include=\"../tools/MissingAnalyzer.dll\" />",
                    StringComparison.Ordinal));
            await CommitAllAsync(repository, "missing analyzer fixture");

            var result = await RunInventoryProcessAsync(repository);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(
                    result.Error + result.Output,
                    Does.Contain("MissingAnalyzer.dll"));
            }
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    [Test]
    public void ProductionConsumersUseOneInventoryAuthority()
    {
        var root = RepositoryRoot();
        var consumers = new[]
        {
            "scripts/Test-SharpProofCoverage.ps1",
            "scripts/Invoke-SharpProofCoverage.ps1",
            "scripts/Test-ProductionCSharpComplexity.ps1",
            "eng/acceptance/Verify.ps1",
            "scripts/Get-SharpProofReleaseDigests.ps1",
            "scripts/Test-SharpProofReleaseAuthorityClosure.ps1"
        };
        using (Assert.EnterMultipleScope())
        {
            foreach (var relativePath in consumers)
            {
                var text = File.ReadAllText(Path.Combine(root, relativePath));
                Assert.That(
                    text,
                    Does.Contain("Get-SharpProofProductionInventory.ps1"),
                    relativePath);
            }

            var complexity = File.ReadAllText(
                Path.Combine(root, "scripts", "Test-ProductionCSharpComplexity.ps1"));
            Assert.That(complexity, Does.Not.Contain("git ls-files"));
            Assert.That(complexity, Does.Contain("New-SharpProofCSharpParseOptions"));
            Assert.That(complexity, Does.Contain("generatedFiles"));
        }
    }

    [Test]
    public async Task ProductionComplexityGatePassesAgainstCanonicalInventory()
    {
        var root = RepositoryRoot();
        var result = await RunAsync(
            root,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            Path.Combine(root, "scripts", "Test-ProductionCSharpComplexity.ps1"),
            "-Json");

        Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        using var document = JsonDocument.Parse(result.Output);
        Assert.That(
            document.RootElement.GetProperty("schemaVersion").GetInt32(),
            Is.EqualTo(1));
        Assert.That(
            document.RootElement.GetProperty("passed").GetBoolean(),
            Is.True);
        Assert.That(
            document.RootElement.GetProperty("exclusions")
                .GetProperty("generatedFiles")
                .GetRawText(),
            Does.Contain("SharpProof.Ir/IrIdentifierAliases.cs"));
    }

    private static async Task WriteFixtureAsync(string repository)
    {
        Directory.CreateDirectory(Path.Combine(repository, "Project"));
        Directory.CreateDirectory(Path.Combine(repository, "scripts"));
        Directory.CreateDirectory(Path.Combine(repository, "eng", "coverage"));
        Directory.CreateDirectory(Path.Combine(repository, "eng", "generated"));
        await File.WriteAllTextAsync(
            Path.Combine(repository, "Project", "Project.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <SharpProofProductionProject>true</SharpProofProductionProject>\n" +
            "    <TargetFramework>net8.0</TargetFramework>\n" +
            "    <AssemblyName>Project</AssemblyName>\n" +
            "    <LangVersion>12.0</LangVersion>\n" +
            "    <DefineConstants>BASE</DefineConstants>\n" +
            "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <Compile Include=\"**/*.cs\" Exclude=\"bin/**/*.cs;obj/**/*.cs\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "Project", "Source.cs"),
            "public static class Source { public static int Value() => 1; }\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "Project", "Generated.g.cs"),
            "// <auto-generated />\npublic static class Generated { public static int Value() => 1; }\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "SharpProof.sln"),
            "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Project\", \"Project/Project.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\n" +
            "EndProject\nGlobal\nEndGlobal\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "eng", "coverage", "SharpProof.Gates.runsettings"),
            "<RunSettings />\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "eng", "generated", "approved-outputs.v1.json"),
            "{\"schemaVersion\":1,\"outputs\":[\"Project/Generated.g.cs\"]}\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "scripts", "Generate-Fixture.ps1"),
            "# generator input\n");
        File.Copy(
            Path.Combine(RepositoryRoot(), "scripts", "Get-SharpProofProductionInventory.ps1"),
            Path.Combine(repository, "scripts", "Get-SharpProofProductionInventory.ps1"));
        File.Copy(
            Path.Combine(RepositoryRoot(), "scripts", "Get-SharpProofTcbPaths.ps1"),
            Path.Combine(repository, "scripts", "Get-SharpProofTcbPaths.ps1"));
    }

    private static async Task<JsonDocument> RunInventoryAsync(string repository)
    {
        var result = await RunInventoryProcessAsync(repository);
        Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        return JsonDocument.Parse(result.Output);
    }

    private static Task<ProcessResult> RunInventoryProcessAsync(string repository)
    {
        return RunAsync(
            repository,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            Path.Combine(
                repository,
                "scripts",
                "Get-SharpProofProductionInventory.ps1"),
            "-RepositoryRoot",
            repository,
            "-Configuration",
            "Release");
    }

    private static string GetString(JsonDocument document, string property)
    {
        return document.RootElement.GetProperty(property).GetString()!;
    }

    private static async Task InitializeRepositoryAsync(string repository)
    {
        await AssertSuccessAsync(RunAsync(repository, "git", "init", "--object-format=sha1"));
        await AssertSuccessAsync(RunAsync(repository, "git", "config", "user.email", "inventory@example.invalid"));
        await AssertSuccessAsync(RunAsync(repository, "git", "config", "user.name", "Inventory Test"));
        await AssertSuccessAsync(RunAsync(repository, "git", "config", "core.autocrlf", "false"));
    }

    private static async Task CommitAllAsync(string repository, string message)
    {
        await AssertSuccessAsync(RunAsync(repository, "git", "add", "--", "."));
        await AssertSuccessAsync(RunAsync(repository, "git", "commit", "-m", message));
    }

    private static async Task AssertSuccessAsync(Task<ProcessResult> operation)
    {
        var result = await operation;
        Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
    }

    private static async Task<ProcessResult> RunAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await output,
            await error);
    }

    private static void DeleteTemporaryRepository(string repository)
    {
        if (Directory.Exists(repository))
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find the repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
