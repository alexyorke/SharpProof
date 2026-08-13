using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class PackageDependencyAuthorityTests
{
    private const string Version = "1.0.0-preview.1";

    [TestCase("canonical", true)]
    [TestCase("fabricated", false)]
    [TestCase("missing", false)]
    [TestCase("extra", false)]
    [TestCase("wrong-version", false)]
    [TestCase("wrong-direction", false)]
    [TestCase("wrong-framework", false)]
    [TestCase("duplicate", false)]
    [TestCase("symbol-mismatch", false)]
    public async Task PackageDependencyGraphIsDerivedFromExactNuspecs(
        string mutation,
        bool expectedSuccess)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-dependencies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = WritePackageGraph(root, mutation);
            var result = await RunAuthorityAsync(paths);
            Assert.That(
                result.ExitCode == 0,
                Is.EqualTo(expectedSuccess),
                result.Output);
            if (expectedSuccess)
            {
                Assert.That(
                    result.Output,
                    Does.Contain("SharpProof.Attributes")
                        .And.Contain(".NETStandard2.0"));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("canonical", true)]
    [TestCase("license-apache", false)]
    [TestCase("license-missing", false)]
    [TestCase("license-file", false)]
    [TestCase("license-case", false)]
    [TestCase("license-spelling", false)]
    [TestCase("license-symbol-mismatch", false)]
    public async Task PackageLicensesMatchTheExactCatalogExpression(
        string mutation,
        bool expectedSuccess)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-licenses-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = WritePackageGraph(root, mutation);
            var result = await RunAuthorityAsync(paths);
            Assert.That(
                result.ExitCode == 0,
                Is.EqualTo(expectedSuccess),
                result.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("canonical", true)]
    [TestCase("license-apache", false)]
    [TestCase("license-missing", false)]
    [TestCase("license-case", false)]
    public async Task SbomLicensesMatchTheExactPackageAuthority(
        string mutation,
        bool expectedSuccess)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-sbom-license-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await RunSbomLicenseAuthorityAsync(root, mutation);
            Assert.That(
                result.ExitCode == 0,
                Is.EqualTo(expectedSuccess),
                result.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("canonical", true)]
    [TestCase("missing", false)]
    [TestCase("extra", false)]
    [TestCase("fabricated", false)]
    [TestCase("wrong-direction", false)]
    public async Task SbomRelationshipsMustMatchDerivedGraph(
        string mutation,
        bool expectedSuccess)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-sbom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await RunSbomAuthorityAsync(root, mutation);
            Assert.That(
                result.ExitCode == 0,
                Is.EqualTo(expectedSuccess),
                result.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string[] WritePackageGraph(string root, string mutation)
    {
        var paths = new List<string>();
        foreach (var extension in new[] { ".nupkg", ".snupkg" })
        {
            foreach (var id in new[] {
                         "SharpProof.Attributes",
                         "SharpProof",
                         "SharpProof.Verifier"
                     })
            {
                var effectiveMutation = mutation;
                if (extension == ".snupkg" &&
                    mutation is not "symbol-mismatch" and
                        not "license-symbol-mismatch")
                {
                    effectiveMutation = "canonical";
                }
                else if (mutation == "symbol-mismatch")
                {
                    effectiveMutation = extension == ".snupkg"
                        ? "fabricated"
                        : "canonical";
                }
                else if (mutation == "license-symbol-mismatch")
                {
                    effectiveMutation = extension == ".snupkg"
                        ? "license-apache"
                        : "canonical";
                }
                var dependencies = DependencyXml(id, effectiveMutation);
                var license = LicenseXml(id, effectiveMutation);
                if (extension == ".snupkg" &&
                    effectiveMutation == "canonical")
                {
                    license = string.Empty;
                }
                var path = Path.Combine(root, id + extension);
                using var archive = ZipFile.Open(
                    path,
                    ZipArchiveMode.Create);
                var entry = archive.CreateEntry(id + ".nuspec");
                using var writer = new StreamWriter(
                    entry.Open(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write($$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                      <metadata>
                        <id>{{id}}</id>
                        <version>{{Version}}</version>
                        {{license}}
                        {{dependencies}}
                      </metadata>
                    </package>
                    """);
                paths.Add(path);
            }
        }

        return paths.ToArray();
    }

    private static string LicenseXml(string id, string mutation)
    {
        if (id != "SharpProof")
        {
            return "<license type=\"expression\">MIT</license>";
        }
        return mutation switch
        {
            "license-apache" =>
                "<license type=\"expression\">Apache-2.0</license>",
            "license-missing" => string.Empty,
            "license-file" => "<license type=\"file\">LICENSE.txt</license>",
            "license-case" => "<license type=\"expression\">mit</license>",
            "license-spelling" => "<license type=\"expression\">MITT</license>",
            _ => "<license type=\"expression\">MIT</license>"
        };
    }

    private static string DependencyXml(string id, string mutation)
    {
        var target = id switch
        {
            "SharpProof" => "SharpProof.Attributes",
            "SharpProof.Verifier" => "SharpProof",
            _ => null
        };
        if (mutation == "wrong-direction" &&
            id == "SharpProof.Attributes")
        {
            target = "SharpProof";
        }
        if (mutation == "missing" && id == "SharpProof")
        {
            target = null;
        }
        if (target is null)
        {
            return """
                <dependencies>
                  <group targetFramework=".NETStandard2.0" />
                </dependencies>
                """;
        }

        if (mutation is "fabricated" or "symbol-mismatch" &&
            id == "SharpProof")
        {
            target = "Fabricated.Dependency";
        }
        var version = mutation == "wrong-version" && id == "SharpProof"
            ? "[9.9.9]"
            : $"[{Version}]";
        var framework = mutation == "wrong-framework" && id == "SharpProof"
            ? "net8.0"
            : ".NETStandard2.0";
        var extra = mutation == "extra" && id == "SharpProof"
            ? $"<dependency id=\"Extra.Dependency\" version=\"[{Version}]\" />"
            : string.Empty;
        var duplicate = mutation == "duplicate" && id == "SharpProof"
            ? $"<dependency id=\"{target}\" version=\"{version}\" />"
            : string.Empty;
        return $$"""
            <dependencies>
              <group targetFramework="{{framework}}">
                <dependency id="{{target}}" version="{{version}}" />
                {{extra}}{{duplicate}}
              </group>
            </dependencies>
            """;
    }

    private static async Task<ProcessResult> RunAuthorityAsync(string[] paths)
    {
        var repositoryRoot = FindRepositoryRoot();
        var runner = Path.Combine(
            Path.GetDirectoryName(paths[0])!,
            "run-authority.ps1");
        await File.WriteAllTextAsync(
            runner,
            "param([string]$Helper, " +
            "[Parameter(ValueFromRemainingArguments=$true)]" +
            "[string[]]$PackagePaths)\n" +
            ". $Helper\n" +
            "$graph = Get-SharpProofPackageDependencyGraph " +
            "-PackagePaths $PackagePaths\n" +
            "$graph | ConvertTo-Json -Compress\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(runner);
        startInfo.ArgumentList.Add(Path.Combine(
            repositoryRoot,
            "scripts",
            "Test-SharpProofPackageDependencies.ps1"));
        foreach (var path in paths)
        {
            startInfo.ArgumentList.Add(path);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            (await output) + Environment.NewLine + (await error));
    }

    private static async Task<ProcessResult> RunSbomAuthorityAsync(
        string root,
        string mutation)
    {
        var repositoryRoot = FindRepositoryRoot();
        var runner = Path.Combine(root, "run-sbom-authority.ps1");
        await File.WriteAllTextAsync(
            runner,
            "param([string]$Helper, [string]$Mutation)\n" +
            ". $Helper\n" +
            "$graph = @(\n" +
            "  [pscustomobject]@{FromId='SharpProof';" +
            "ToId='SharpProof.Attributes'},\n" +
            "  [pscustomobject]@{FromId='SharpProof.Verifier';" +
            "ToId='SharpProof'}\n" +
            ")\n" +
            "$rows = @($graph | ForEach-Object { " +
            "[pscustomobject]@{spdxElementId=" +
            "(Get-SharpProofDependencySpdxId $_.FromId);" +
            "relationshipType='DEPENDS_ON';relatedSpdxElement=" +
            "(Get-SharpProofDependencySpdxId $_.ToId)} })\n" +
            "switch ($Mutation) {\n" +
            "  'missing' {$rows=@($rows[0])}\n" +
            "  'extra' {$rows+= [pscustomobject]@{" +
            "spdxElementId='SPDXRef-Package-Extra';" +
            "relationshipType='DEPENDS_ON';" +
            "relatedSpdxElement='SPDXRef-Package-SharpProof'}}\n" +
            "  'fabricated' {$rows[0].relatedSpdxElement=" +
            "'SPDXRef-Package-Fabricated.Dependency'}\n" +
            "  'wrong-direction' {$value=$rows[0].spdxElementId;" +
            "$rows[0].spdxElementId=$rows[0].relatedSpdxElement;" +
            "$rows[0].relatedSpdxElement=$value}\n" +
            "}\n" +
            "Test-SharpProofSbomDependencyGraph " +
            "-Relationships $rows -DependencyGraph $graph\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return await RunPowerShellAsync(repositoryRoot, runner, mutation);
    }

    private static async Task<ProcessResult> RunSbomLicenseAuthorityAsync(
        string root,
        string mutation)
    {
        var repositoryRoot = FindRepositoryRoot();
        var runner = Path.Combine(root, "run-sbom-license-authority.ps1");
        await File.WriteAllTextAsync(
            runner,
            "param([string]$Helper, [string]$Mutation)\n" +
            ". $Helper\n" +
            "$graph=@('SharpProof.Attributes','SharpProof'," +
            "'SharpProof.Verifier') | ForEach-Object {" +
            "[pscustomobject]@{PackageId=$_;LicenseExpression='MIT'}}\n" +
            "$rows=@($graph | ForEach-Object {" +
            "[pscustomobject]@{name=$_.PackageId;licenseDeclared='MIT';" +
            "licenseConcluded='MIT'}})\n" +
            "switch ($Mutation) {\n" +
            " 'license-apache' {$rows[1].licenseDeclared='Apache-2.0';" +
            "$rows[1].licenseConcluded='Apache-2.0'}\n" +
            " 'license-missing' {$rows[1].PSObject.Properties.Remove(" +
            "'licenseDeclared')}\n" +
            " 'license-case' {$rows[1].licenseDeclared='mit';" +
            "$rows[1].licenseConcluded='mit'}\n" +
            "}\n" +
            "Test-SharpProofSbomLicenseGraph " +
            "-SbomPackages $rows -LicenseGraph $graph\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return await RunPowerShellAsync(repositoryRoot, runner, mutation);
    }

    private static async Task<ProcessResult> RunPowerShellAsync(
        string repositoryRoot,
        string runner,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(runner);
        startInfo.ArgumentList.Add(Path.Combine(
            repositoryRoot,
            "scripts",
            "Test-SharpProofPackageDependencies.ps1"));
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
            (await output) + Environment.NewLine + (await error));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpProof.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
