using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using SharpProof.Tool;
using SharpProof.Worker.Protocol;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
public sealed class WorkspaceExportTests
{
    private const string Compose = """
        services:
          verify:
            image: __IMAGE__
            platform: linux/amd64
            mem_limit: ${SHARPPROOF_CONTAINER_MEMORY_LIMIT:-40g}
            cpus: ${SHARPPROOF_CONTAINER_CPU_LIMIT:-0}
            working_dir: /workspace/consumer
            environment:
              - NuGetPackageSourceCredentials_SharpProofPreview
            volumes:
              - .:/workspace/consumer
              - sharpproof-obj:/workspace/consumer/.sharpproof-obj
              - sharpproof-bin:/workspace/consumer/.sharpproof-bin
              - sharpproof-packages:/home/sharpproof/.nuget/packages
              - sharpproof-dotnet:/home/sharpproof/.dotnet
        volumes:
          sharpproof-obj:
          sharpproof-bin:
          sharpproof-packages:
          sharpproof-dotnet:
        """;

    private const string Properties = """
        <Project>
          <PropertyGroup Condition="'$(SHARPPROOF_CONTAINER)' == '1'">
            <SharpProofProjectOutputKey>$([MSBuild]::MakeRelative('$(MSBuildThisFileDirectory)', '$(MSBuildProjectDirectory)'))/$(MSBuildProjectName)</SharpProofProjectOutputKey>
            <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory).sharpproof-obj/$(SharpProofProjectOutputKey)/</BaseIntermediateOutputPath>
            <BaseOutputPath>$(MSBuildThisFileDirectory).sharpproof-bin/$(SharpProofProjectOutputKey)/</BaseOutputPath>
          </PropertyGroup>
        </Project>
        """;

    [Test]
    public async Task WorkspaceExportAndCapturedInputsRemainUsableAfterRelocationWithSpaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpproof workspace ' spaced-" + Guid.NewGuid().ToString("N"));
        using var fixture = new NUnitExportCoverageTests.ExportFixture(root: root, projectDirectoryName: "src/app with spaces");
        Configure(fixture);
        fixture.OutputPath = Path.Combine(fixture.Root, "tests", "Regression's Case");
        await ExportAsync(fixture, fixture.Root + Path.DirectorySeparatorChar, TestContext.CurrentContext.CancellationToken);
        var project = XDocument.Load(Path.Combine(fixture.OutputPath, "SharpProof.Regression.csproj"));
        var include = project.Descendants("ProjectReference").Single().Attribute("Include")!.Value;
        Assert.That(include, Is.EqualTo("../../src/app with spaces/Consumer.csproj"));
        var readme = await File.ReadAllTextAsync(Path.Combine(fixture.OutputPath, "README.md"), TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(readme, Does.Contain("docker compose run --rm verify test 'tests/Regression''s Case/SharpProof.Regression.csproj'"));
            Assert.That(readme, Does.Contain("tests/Regression'\\''s Case/SharpProof.Regression.csproj"));
            Assert.That(readme, Does.Not.Contain(fixture.Root).And.Not.Contain("NuGetPackageSourceCredentials"));
            Assert.That(Directory.GetFiles(fixture.OutputPath, "compose*"), Is.Empty);
        }

        var relocated = fixture.Root + " relocated";
        Directory.Move(fixture.Root, relocated);
        try
        {
            var movedProject = Path.Combine(relocated, "src", "app with spaces", "Consumer.csproj");
            var movedRegression = Path.Combine(relocated, "tests", "Regression's Case");
            Assert.That(Path.GetFullPath(Path.Combine(movedRegression, include)), Is.EqualTo(movedProject));
            Assert.That(File.Exists(movedProject), Is.True);
            var secondOutput = Path.Combine(relocated, "tests", "After Move");
            await NUnitExporter.ExportAsync(movedProject, fixture.Evidence, fixture.Claim.ClaimId, secondOutput,
                relocated, TestContext.CurrentContext.CancellationToken);
            var secondReadme = await File.ReadAllTextAsync(Path.Combine(secondOutput, "README.md"), TestContext.CurrentContext.CancellationToken);
            Assert.That(secondReadme, Does.Contain("'tests/After Move/SharpProof.Regression.csproj'").And.Not.Contain(relocated));
            Assert.That(Directory.GetDirectories(relocated, ".sharpproof-export-*", SearchOption.AllDirectories), Is.Empty);
        }
        finally
        {
            Directory.Move(relocated, fixture.Root);
        }
    }

    [TestCase("floating-image")]
    [TestCase("foreign-image")]
    [TestCase("platform")]
    [TestCase("project-mount")]
    [TestCase("missing-isolation")]
    [TestCase("credential-value")]
    [TestCase("entrypoint")]
    [TestCase("duplicate-image")]
    [TestCase("missing-compose")]
    [TestCase("missing-properties")]
    [TestCase("changed-properties")]
    [TestCase("malformed-properties")]
    public void WorkspaceRequiresTheRecognizedPinnedConsumerAndOutputIsolation(string unsupported)
    {
        ArgumentException.ThrowIfNullOrEmpty(unsupported);
        using var fixture = new NUnitExportCoverageTests.ExportFixture();
        Configure(fixture);
        var composePath = Path.Combine(fixture.Root, "compose.yaml");
        var propertiesPath = Path.Combine(fixture.Root, "Directory.Build.props");
        var compose = File.ReadAllText(composePath).Replace("\r\n", "\n", StringComparison.Ordinal);
        switch (unsupported)
        {
            case "floating-image":
                compose = compose.Replace(Image, "ghcr.io/alexyorke/sharpproof-preview:latest", StringComparison.Ordinal);
                break;
            case "foreign-image":
                compose = compose.Replace("ghcr.io/alexyorke/", "ghcr.io/other/", StringComparison.Ordinal);
                break;
            case "platform":
                compose = compose.Replace("linux/amd64", "linux/arm64", StringComparison.Ordinal);
                break;
            case "project-mount":
                compose = compose.Replace("- .:/workspace/consumer", "- ./src/app:/workspace/consumer", StringComparison.Ordinal);
                break;
            case "missing-isolation":
                compose = compose.Replace("      - sharpproof-bin:/workspace/consumer/.sharpproof-bin\n", "", StringComparison.Ordinal);
                break;
            case "credential-value":
                compose = compose.Replace("- NuGetPackageSourceCredentials_SharpProofPreview", "- NuGetPackageSourceCredentials_SharpProofPreview=fixture-value", StringComparison.Ordinal);
                break;
            case "entrypoint":
                compose = compose.Replace("    volumes:", "    entrypoint: [sh]\n    volumes:", StringComparison.Ordinal);
                break;
            case "duplicate-image":
                compose = compose.Replace("    image: ", "    image: " + Image + "\n    image: ", StringComparison.Ordinal);
                break;
            case "missing-compose":
                File.Delete(composePath);
                break;
            case "missing-properties":
                File.Delete(propertiesPath);
                break;
            case "changed-properties":
                File.WriteAllText(propertiesPath, Properties.Replace(".sharpproof-bin/", "bin/", StringComparison.Ordinal));
                break;
            case "malformed-properties":
                File.WriteAllText(propertiesPath, "<Project><broken>");
                break;
        }

        if (unsupported != "missing-compose")
        {
            File.WriteAllText(composePath, compose);
        }

        Assert.ThrowsAsync<InvalidDataException>(new Func<Task>(async () =>
            await ExportAsync(fixture, fixture.Root, TestContext.CurrentContext.CancellationToken)));
        Assert.That(Directory.Exists(fixture.OutputPath), Is.False);
        Assert.That(Directory.GetDirectories(fixture.Root, ".sharpproof-export-*", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void WorkspacePathsMustStayInsideAnExistingRoot()
    {
        using var fixture = new NUnitExportCoverageTests.ExportFixture();
        Configure(fixture);
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            await ExportAsync(fixture, " ", TestContext.CurrentContext.CancellationToken)));
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            await ExportAsync(fixture, Path.Combine(fixture.Root, "missing"), TestContext.CurrentContext.CancellationToken)));
        fixture.OutputPath = Path.Combine(fixture.Root + "-outside", "Regression");
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            await ExportAsync(fixture, fixture.Root, TestContext.CurrentContext.CancellationToken)));
        var smallerRoot = Path.Combine(fixture.Root, "smaller");
        Directory.CreateDirectory(smallerRoot);
        fixture.OutputPath = Path.Combine(smallerRoot, "Regression");
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            await ExportAsync(fixture, smallerRoot, TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void WorkspaceRejectsSymbolicLinkTraversalAndCapturedSourceOutsideTheRoot()
    {
        using var fixture = new NUnitExportCoverageTests.ExportFixture();
        Configure(fixture);
        var link = Path.Combine(fixture.Root, "linked-consumer");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(fixture.ProjectPath)!);
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            await NUnitExporter.ExportAsync(Path.Combine(link, "Consumer.csproj"), fixture.Evidence, fixture.Claim.ClaimId,
                fixture.OutputPath, fixture.Root, TestContext.CurrentContext.CancellationToken)));
        var external = Path.Combine(Path.GetTempPath(), "sharpproof-external-" + Guid.NewGuid().ToString("N") + ".cs");
        try
        {
            File.Copy(fixture.SourcePath, external);
            fixture.Snapshot = fixture.Snapshot with { SyntaxTrees = [fixture.Snapshot.SyntaxTrees[0] with { Path = external }] };
            Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
                await ExportAsync(fixture, fixture.Root, TestContext.CurrentContext.CancellationToken)));
        }
        finally
        {
            File.Delete(external);
        }
    }

    [Test]
    public void WorkspaceExportStillRejectsUnconfirmedModels()
    {
        using var fixture = new NUnitExportCoverageTests.ExportFixture();
        Configure(fixture);
        fixture.Result = fixture.Result with { Outcome = WorkerClaimOutcome.Unknown };
        var error = Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () =>
            await ExportAsync(fixture, fixture.Root, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Does.Contain("Only replayed direct scalar"));
        Assert.That(Directory.Exists(fixture.OutputPath), Is.False);
    }

    [Test]
    public async Task DistinctRegressionDirectoriesUseDistinctIsolatedBuildPaths()
    {
        using var fixture = new NUnitExportCoverageTests.ExportFixture(projectDirectoryName: "src/app");
        Configure(fixture);
        var firstOutput = fixture.OutputPath;
        await ExportAsync(fixture, fixture.Root, TestContext.CurrentContext.CancellationToken);
        fixture.OutputPath = Path.Combine(fixture.Root, "tests", "Second Regression");
        await ExportAsync(fixture, fixture.Root, TestContext.CurrentContext.CancellationToken);
        var first = await BuildPathsAsync(fixture.Root, firstOutput, TestContext.CurrentContext.CancellationToken);
        var second = await BuildPathsAsync(fixture.Root, fixture.OutputPath, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ObjectPath, Does.Contain(".sharpproof-obj/tests/Regression/SharpProof.Regression/"));
            Assert.That(second.ObjectPath, Does.Contain(".sharpproof-obj/tests/Second Regression/SharpProof.Regression/"));
            Assert.That(first.BinaryPath, Is.Not.EqualTo(second.BinaryPath));
            Assert.That(first.ObjectPath, Is.Not.EqualTo(second.ObjectPath));
        }
    }

    private static string Image => "ghcr.io/alexyorke/sharpproof-preview@sha256:" + new string('a', 64);

    internal static void Configure(NUnitExportCoverageTests.ExportFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        File.WriteAllText(Path.Combine(fixture.Root, "compose.yaml"), "# workspace configuration\r\n\r\n" +
            Compose.Replace("__IMAGE__", Image, StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(fixture.Root, "Directory.Build.props"), Properties, new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "tests"));
        fixture.OutputPath = Path.Combine(fixture.Root, "tests", "Regression");
    }

    private static Task ExportAsync(NUnitExportCoverageTests.ExportFixture fixture, string workspaceDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrEmpty(workspaceDirectory);
        return NUnitExporter.ExportAsync(fixture.ProjectPath, fixture.Evidence, fixture.Claim.ClaimId, fixture.OutputPath,
            workspaceDirectory, cancellationToken);
    }

    private static async Task<(string ObjectPath, string BinaryPath)> BuildPathsAsync(string root, string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var evaluated = await ToolProcess.RunAsync("dotnet", root, cancellationToken, "msbuild",
            Path.Combine(directory, "SharpProof.Regression.csproj"), "-nologo", "-p:SHARPPROOF_CONTAINER=1",
            "-getProperty:BaseIntermediateOutputPath,BaseOutputPath");
        Assert.That(evaluated.ExitCode, Is.Zero, evaluated.Output);
        using var document = JsonDocument.Parse(evaluated.Output);
        var properties = document.RootElement.GetProperty("Properties");
        return (properties.GetProperty("BaseIntermediateOutputPath").GetString()!, properties.GetProperty("BaseOutputPath").GetString()!);
    }
}
