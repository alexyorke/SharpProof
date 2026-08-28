using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class GeneratorDuplicatePropertyTests
{
    [TestCase(
        "Generate-BoundContractModel.ps1",
        "SharpProof.Contracts/BoundContractModel.schema.json",
        "SchemaPath",
        "OutputPath",
        "schemaVersion")]
    [TestCase(
        "Generate-OperationSupportCatalog.ps1",
        "SharpProof.Frontend/OperationSupport.catalog.json",
        "CatalogPath",
        "OutputPath",
        "schemaVersion")]
    [TestCase(
        "Generate-EffectContractMappings.ps1",
        "SharpProof.Effects/EffectContractMappings.catalog.json",
        "CatalogPath",
        "OutputPath",
        "enums")]
    [TestCase(
        "Generate-AnalyzerDiagnosticCatalog.ps1",
        "SharpProof.Analyzer.Core/AnalyzerDiagnostic.catalog.json",
        "CatalogPath",
        "OutputPath",
        "schemaVersion")]
    public async Task GeneratorRejectsDuplicateRootProperties(
        string generator,
        string relativeCatalog,
        string catalogArgument,
        string outputArgument,
        string duplicateProperty)
    {
        var repository = RepositoryRoot();
        var catalog = await File.ReadAllTextAsync(
            Path.Combine(repository, relativeCatalog));
        catalog = duplicateProperty == "enums"
            ? catalog.Replace(
                "\"enums\": [",
                "\"enums\": [],\n  \"enums\": [",
                StringComparison.Ordinal)
            : catalog.Replace(
                "\"schemaVersion\": 1,",
                "\"schemaVersion\": 1,\n  \"schemaVersion\": 1,",
                StringComparison.Ordinal);

        var directory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "generator-duplicate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalogPath = Path.Combine(directory, "catalog.json");
            var outputPath = Path.Combine(directory, "generated.cs");
            await File.WriteAllTextAsync(catalogPath, catalog);
            var startInfo = new ProcessStartInfo("pwsh")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            foreach (var argument in new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(repository, "scripts", generator),
                "-" + catalogArgument,
                catalogPath,
                "-" + outputArgument,
                outputPath
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("PowerShell did not start.");
            var output = await process.StandardOutput.ReadToEndAsync();
            output += await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.That(process.ExitCode, Is.Not.Zero, output);
            Assert.That(
                output,
                Does.Contain($"duplicate property '{duplicateProperty}'"));
            Assert.That(File.Exists(outputPath), Is.False);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
