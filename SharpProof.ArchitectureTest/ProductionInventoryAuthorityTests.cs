using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ProductionInventoryAuthorityTests
{
    private const string TemporaryRepositoryRootName =
        "SharpProof.Architecture.ProductionInventory";

    [Test]
    public async Task InventoryBindsParseGeneratorAndGeneratedAuthorities()
    {
        using var temporary = TempDirectory.CreateOwned(
            TemporaryRepositoryRootName,
            "inventory-",
            "Refusing to remove an unexpected production-inventory directory.");
        var repository = temporary.FullName;
        await InitializeRepositoryAsync(repository);
        await WriteFixtureAsync(repository);
        await CommitAllAsync(repository, "inventory fixture");

        var baseline = await RunInventoryAsync(repository);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(baseline.RootElement.TryGetProperty("sourceUniverseSha256", out _), Is.False);
            Assert.That(baseline.RootElement.TryGetProperty("generatedManifestSha256", out _), Is.False);
            Assert.That(baseline.RootElement.TryGetProperty("pdbUniverseSha256", out _), Is.False);
        }

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
            parseMutation.RootElement.GetProperty("projects")[0]
                .GetProperty("parseOptions").GetProperty("preprocessorSymbols")
                .EnumerateArray().Select(static symbol => symbol.GetString())
                .ToArray(),
            Does.Contain("MUTATED_PARSE"),
            "The inventory must still expose evaluated parse options.");

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
        var authorityPath = Path.Combine(repository, "authority.json");
        var contractPath = Path.Combine(repository, "contract.json");
        await File.WriteAllTextAsync(
            authorityPath,
            authority.RootElement.GetRawText() + "\n");
        await File.WriteAllTextAsync(
            contractPath,
            "{\"trustedKernel\":{\"paths\":[\"Project/Source.cs\"]}," +
            "\"trustedComputingBase\":{\"pipelineCompileProjects\":[\"Project/Project.csproj\"]," +
            "\"components\":[{\"name\":\"fixture\",\"paths\":[\"Project/Project.csproj\",\"Project/Generated.g.cs\"]}]}}\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "tcb-probe.ps1"),
            "Set-StrictMode -Version Latest\n" +
            ". (Join-Path $PSScriptRoot 'scripts/Get-SharpProofTcbPaths.ps1')\n" +
            "$authority = Get-Content (Join-Path $PSScriptRoot 'authority.json') -Raw | ConvertFrom-Json\n" +
            "$contract = Get-Content (Join-Path $PSScriptRoot 'contract.json') -Raw | ConvertFrom-Json\n" +
            "Get-SharpProofTcbPaths -Contract $contract -ProductionInventory $authority | Out-Null\n");
        var classifiedTcb = await ArchitectureRepository.RunProcessAsync(
            repository,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            Path.Combine(repository, "tcb-probe.ps1"));
        Assert.That(
            classifiedTcb.ExitCode,
            Is.Zero,
            classifiedTcb.Error + classifiedTcb.Output);

        await File.WriteAllTextAsync(
            contractPath,
            "{\"trustedKernel\":{\"paths\":[\"Project/Source.cs\"]}," +
            "\"trustedComputingBase\":{\"pipelineCompileProjects\":[\"Project/Project.csproj\"]," +
            "\"components\":[{\"name\":\"fixture\",\"paths\":[\"Project/Project.csproj\",\"Project/Generated.g.cs\",\"Project/Foreign.cs\"]}]}}\n");
        var tcbMutation = await ArchitectureRepository.RunProcessAsync(
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

        await File.WriteAllTextAsync(contractPath,
            "{\"trustedKernel\":{\"paths\":[\"Project/Source.cs\"]}," +
            "\"trustedComputingBase\":{\"pipelineCompileProjects\":[\"Project/Project.csproj\"]," +
            "\"components\":[{\"name\":\"fixture\",\"paths\":[\"Project/Project.csproj\",\"Project/Generated.g.cs\"]}]}}\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "Project", "Added.cs"),
            "public partial class Shared { public static int Added() => 2; }\n");
        using var changedAuthority = await RunInventoryAsync(repository);
        await File.WriteAllTextAsync(
            authorityPath,
            changedAuthority.RootElement.GetRawText() + "\n");
        var unclassifiedCompileItem = await ArchitectureRepository.RunProcessAsync(
            repository,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            Path.Combine(repository, "tcb-probe.ps1"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unclassifiedCompileItem.ExitCode, Is.Not.Zero);
            Assert.That(
                unclassifiedCompileItem.Error + unclassifiedCompileItem.Output,
                Does.Contain("Production pipeline Compile item is not classified")
                    .And.Contain("Project/Added.cs"));
        }
    }

    [Test]
    public async Task InventoryRejectsMissingRepositoryAnalyzer()
    {
        using var temporary = TempDirectory.CreateOwned(
            TemporaryRepositoryRootName,
            "analyzer-",
            "Refusing to remove an unexpected production-inventory directory.");
        var repository = temporary.FullName;
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

    [Test]
    public async Task ProductionPipelineSourcesAndTrustedPartialTypesAreComplete()
    {
        var repository = TestRepository.FindRoot();
        using var inventory = await RunInventoryAsync(repository);
        using var contract = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(repository, "eng", "acceptance", "contract.json")));
        var tcb = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in contract.RootElement.GetProperty("trustedKernel")
                     .GetProperty("paths").EnumerateArray())
        {
            tcb.Add(path.GetString() ?? "");
        }
        var declaration = contract.RootElement.GetProperty("trustedComputingBase");
        foreach (var component in declaration.GetProperty("components")
                     .EnumerateArray())
        {
            foreach (var path in component.GetProperty("paths").EnumerateArray())
            {
                tcb.Add(path.GetString() ?? "");
            }
        }

        var pipelineProjects = declaration.GetProperty("pipelineCompileProjects")
            .EnumerateArray()
            .Select(static project => project.GetString() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var pipelineProject in pipelineProjects)
        {
            Assert.That(
                tcb.Contains(pipelineProject),
                Is.True,
                $"Trusted pipeline project is not in the TCB: {pipelineProject}");
        }
        var productionProjects = inventory.RootElement.GetProperty("projects")
            .EnumerateArray()
            .ToArray();
        var observedPipelineProjects = productionProjects
            .Where(project => pipelineProjects.Contains(
                project.GetProperty("projectPath").GetString() ?? ""))
            .ToArray();
        Assert.That(
            observedPipelineProjects.Length,
            Is.EqualTo(pipelineProjects.Count),
            "Every declared proof pipeline project must appear in the evaluated inventory.");

        foreach (var project in observedPipelineProjects)
        {
            var projectPath = project.GetProperty("projectPath").GetString() ?? "";
            foreach (var file in project.GetProperty("compile").EnumerateArray())
            {
                var sourcePath = file.GetProperty("path").GetString() ?? "";
                Assert.That(
                    tcb.Contains(sourcePath),
                    Is.True,
                    $"Evaluated pipeline Compile source is not in the TCB: {projectPath} -> {sourcePath}");
            }
        }

        var partialGaps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in productionProjects)
        {
            var projectName = project.GetProperty("name").GetString() ?? "unknown";
            var parseOptionsElement = project.GetProperty("parseOptions");
            var preprocessorSymbols = parseOptionsElement
                .GetProperty("preprocessorSymbols")
                .EnumerateArray()
                .Select(static symbol => symbol.GetString() ?? "")
                .Where(static symbol => symbol.Length != 0)
                .ToArray();
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.Preview,
                preprocessorSymbols: preprocessorSymbols);
            var trees = project.GetProperty("compile")
                .EnumerateArray()
                .Select(file =>
                {
                    var sourcePath = file.GetProperty("path").GetString() ?? "";
                    return CSharpSyntaxTree.ParseText(
                        File.ReadAllText(Path.Combine(
                            repository,
                            sourcePath.Replace('/', Path.DirectorySeparatorChar))),
                        parseOptions,
                        path: sourcePath);
                })
                .ToArray();
            var compilation = CSharpCompilation.Create(
                "TcbPartialAudit_" + projectName,
                trees,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            foreach (var tree in trees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var syntaxRoot = await tree.GetRootAsync();
                foreach (var typeDeclaration in syntaxRoot
                             .DescendantNodes()
                             .OfType<TypeDeclarationSyntax>()
                             .Where(static declaration => declaration.Modifiers
                                 .Any(SyntaxKind.PartialKeyword)))
                {
                    if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not
                        INamedTypeSymbol symbol)
                    {
                        continue;
                    }

                    var declarationPaths = symbol.DeclaringSyntaxReferences
                        .Select(static reference => reference.SyntaxTree.FilePath)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (!declarationPaths.Any(tcb.Contains))
                    {
                        continue;
                    }

                    foreach (var declarationPath in declarationPaths)
                    {
                        if (!tcb.Contains(declarationPath))
                        {
                            partialGaps.Add(
                                $"{symbol.ToDisplayString()} -> {declarationPath}");
                        }
                    }
                }
            }
        }

        Assert.That(
            partialGaps,
            Is.Empty,
            "Every source file declaring part of a TCB type must itself be in the TCB.");
    }

    [Test]
    public void ProductionConsumersUseOneInventoryAuthority()
    {
        var root = TestRepository.FindRoot();
        var consumers = new[]
        {
            "scripts/Test-SharpProofCoverage.ps1",
            "scripts/Invoke-SharpProofCoverage.ps1",
            "scripts/Test-ProductionCSharpComplexity.ps1",
            "eng/acceptance/Verify.ps1",
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
        var root = TestRepository.FindRoot();
        var result = await ArchitectureRepository.RunProcessAsync(
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
            "public static class Source { public static int Value() => 1; }\n" +
            "public partial class Shared { }\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "Project", "Generated.g.cs"),
            "// <auto-generated />\npublic static class Generated { public static int Value() => 1; }\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "SharpProof.slnx"),
            "<Solution>\n" +
            "  <Project Path=\"Project/Project.csproj\" />\n" +
            "</Solution>\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "eng", "generated", "approved-outputs.v1.json"),
            "{\"schemaVersion\":1,\"outputs\":[\"Project/Generated.g.cs\"]}\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "scripts", "Generate-Fixture.ps1"),
            "# generator input\n");
        File.Copy(
            Path.Combine(TestRepository.FindRoot(), "scripts", "Get-SharpProofProductionInventory.ps1"),
            Path.Combine(repository, "scripts", "Get-SharpProofProductionInventory.ps1"));
        File.Copy(
            Path.Combine(TestRepository.FindRoot(), "scripts", "Get-SharpProofTcbPaths.ps1"),
            Path.Combine(repository, "scripts", "Get-SharpProofTcbPaths.ps1"));
        File.Copy(
            Path.Combine(TestRepository.FindRoot(), "scripts", "SharpProof.ContainerExecution.psm1"),
            Path.Combine(repository, "scripts", "SharpProof.ContainerExecution.psm1"));
        File.Copy(
            Path.Combine(TestRepository.FindRoot(), "scripts", "SharpProof.PEMetadata.psm1"),
            Path.Combine(repository, "scripts", "SharpProof.PEMetadata.psm1"));
    }

    private static async Task<JsonDocument> RunInventoryAsync(string repository)
    {
        var result = await RunInventoryProcessAsync(repository);
        Assert.That(result.ExitCode, Is.Zero, result.Error + result.Output);
        return JsonDocument.Parse(result.Output);
    }

    private static Task<ProcessRunnerResult> RunInventoryProcessAsync(string repository)
    {
        return ArchitectureRepository.RunProcessAsync(
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

    private static async Task InitializeRepositoryAsync(string repository)
    {
        await ArchitectureGitRepository.InitializeAsync(
            repository,
            "inventory@example.invalid",
            "Inventory Test",
            ("core.autocrlf", "false"));
    }

    private static async Task CommitAllAsync(string repository, string message)
    {
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(
                repository, "git", "add", "--", "."), includeOutput: true);
        await ArchitectureRepository.AssertSuccessAsync(
            ArchitectureRepository.RunProcessAsync(
                repository, "git", "commit", "-m", message), includeOutput: true);
    }
}
