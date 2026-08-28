using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ScalarSemanticsGeneratorTests
{
    [Test]
    public async Task GeneratorRejectsDuplicateCatalogProperties()
    {
        var repository = RepositoryRoot();
        var catalog = await File.ReadAllTextAsync(Path.Combine(
            repository,
            "SharpProof.Frontend",
            "CSharpScalarSemantics.json"));
        catalog = catalog.Replace(
            "\"schemaVersion\": 2,",
            "\"schemaVersion\": 2,\n  \"schemaVersion\": 2,",
            StringComparison.Ordinal);

        var directory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "scalar-semantics-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalogPath = Path.Combine(directory, "catalog.json");
            var outputPath = Path.Combine(directory, "frontend.generated.cs");
            var irOutputPath = Path.Combine(directory, "ir.generated.cs");
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
                Path.Combine(repository, "scripts", "Generate-CSharpScalarSemantics.ps1"),
                "-CatalogPath",
                catalogPath,
                "-OutputPath",
                outputPath,
                "-IrOutputPath",
                irOutputPath
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
            Assert.That(output, Does.Contain("duplicate property 'schemaVersion'"));
            Assert.That(File.Exists(outputPath), Is.False);
            Assert.That(File.Exists(irOutputPath), Is.False);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("SharpProof.sln was not found.");
    }
}
