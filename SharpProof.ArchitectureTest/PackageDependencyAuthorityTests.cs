using System.Diagnostics;
using System.Globalization;
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
    [TestCase("metadata-authors-missing", false)]
    [TestCase("metadata-authors-changed", false)]
    [TestCase("metadata-authors-duplicate", false)]
    [TestCase("metadata-authors-case", false)]
    [TestCase("metadata-authors-form", false)]
    [TestCase("metadata-projectUrl-missing", false)]
    [TestCase("metadata-projectUrl-changed", false)]
    [TestCase("metadata-projectUrl-duplicate", false)]
    [TestCase("metadata-projectUrl-case", false)]
    [TestCase("metadata-projectUrl-form", false)]
    [TestCase("metadata-description-missing", false)]
    [TestCase("metadata-description-changed", false)]
    [TestCase("metadata-description-duplicate", false)]
    [TestCase("metadata-description-case", false)]
    [TestCase("metadata-description-form", false)]
    [TestCase("metadata-tags-missing", false)]
    [TestCase("metadata-tags-changed", false)]
    [TestCase("metadata-tags-duplicate", false)]
    [TestCase("metadata-tags-case", false)]
    [TestCase("metadata-tags-form", false)]
    [TestCase("metadata-symbol-mismatch", false)]
    public async Task PublicPackageMetadataMatchesTheExactCatalog(
        string mutation,
        bool expectedSuccess)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-metadata-" + Guid.NewGuid().ToString("N"));
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
    [TestCase("first-noassertion", false)]
    [TestCase("first-wrong", false)]
    [TestCase("first-case", false)]
    [TestCase("first-missing", false)]
    [TestCase("first-extra", false)]
    [TestCase("third-noassertion", false)]
    [TestCase("third-wrong", false)]
    [TestCase("third-case", false)]
    [TestCase("third-missing", false)]
    [TestCase("third-extra", false)]
    [TestCase("unknown-component", false)]
    [TestCase("duplicate-component", false)]
    [TestCase("wrong-download", false)]
    [TestCase("files-analyzed", false)]
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
                        not "license-symbol-mismatch" and
                        not "metadata-symbol-mismatch")
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
                else if (mutation == "metadata-symbol-mismatch")
                {
                    effectiveMutation = extension == ".snupkg"
                        ? "metadata-authors-changed"
                        : "canonical";
                }
                var dependencies = DependencyXml(id, effectiveMutation);
                var license = LicenseXml(id, effectiveMutation);
                var publicMetadata = PublicMetadataXml(id, effectiveMutation);
                if (extension == ".snupkg" &&
                    effectiveMutation == "canonical")
                {
                    license = string.Empty;
                    publicMetadata = string.Empty;
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
                        {{publicMetadata}}
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

    private static string PublicMetadataXml(string id, string mutation)
    {
        var values = id switch
        {
            "SharpProof.Attributes" => new Dictionary<string, string>
            {
                ["authors"] = "Alex Yorke",
                ["projectUrl"] = "https://github.com/alexyorke/SharpProof",
                ["description"] = "Contains SharpProof contracts for compiler-bound Requires, Ensures, Assume, Result, and Old expressions; closed contract attributes; suppressions and trust declarations; and composable method effects.",
                ["tags"] = "SharpProof Attributes Contracts Purity StaticAnalysis MethodEffects Capabilities ZeroAllocations Exceptions Preconditions Postconditions"
            },
            "SharpProof.Verifier" => new Dictionary<string, string>
            {
                ["authors"] = "Alex Yorke",
                ["projectUrl"] = "https://github.com/alexyorke/SharpProof",
                ["description"] = "Container-only Linux amd64 postcondition verifier for SharpProof, including the worker, launcher, MSBuild integration, and the pinned native Z3 payload.",
                ["tags"] = "SharpProof Verifier Contracts SMT Z3 Linux Container x64"
            },
            _ => new Dictionary<string, string>
            {
                ["authors"] = "Alex Yorke",
                ["projectUrl"] = "https://github.com/alexyorke/SharpProof",
                ["description"] = "Portable SharpProof Roslyn analysis and contract generation for bounded effect contracts, compiler-bound preconditions, and accountable postcondition verification. Unsupported selected code remains visibly incomplete.",
                ["tags"] = "SharpProof Roslyn Analyzer Purity StaticAnalysis MethodEffects Capabilities ZeroAllocations ExceptionContracts Preconditions Contracts"
            }
        };
        var parts = mutation.Split('-');
        var mutatedField = parts.Length >= 3 && parts[0] == "metadata"
            ? parts[1]
            : null;
        var operation = parts.Length >= 3 ? parts[2] : null;
        var builder = new StringBuilder();
        foreach (var pair in values)
        {
            if (id == "SharpProof" && pair.Key == mutatedField &&
                operation == "missing")
            {
                continue;
            }
            var value = id == "SharpProof" && pair.Key == mutatedField
                ? operation switch
                {
                    "changed" => "Fabricated metadata",
                    "case" => pair.Value.ToUpperInvariant(),
                    _ => pair.Value
                }
                : pair.Value;
            if (id == "SharpProof" && pair.Key == mutatedField &&
                operation == "form")
            {
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"<{pair.Key} xml:lang=\"en\">{value}</{pair.Key}>");
                continue;
            }
            builder.Append(
                CultureInfo.InvariantCulture,
                $"<{pair.Key}>{value}</{pair.Key}>");
            if (id == "SharpProof" && pair.Key == mutatedField &&
                operation == "duplicate")
            {
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"<{pair.Key}>{value}</{pair.Key}>");
            }
        }
        return builder.ToString();
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
            "$packages=@('SharpProof.Attributes','SharpProof'," +
            "'SharpProof.Verifier') | ForEach-Object {" +
            "[pscustomobject]@{PackageId=$_;LicenseExpression='MIT'}}\n" +
            "$components=@([pscustomobject]@{id='Microsoft.Z3';" +
            "version='4.12.2';license='MIT'})\n" +
            "$graph=@(Get-SharpProofSbomLicenseGraph " +
            "-PackageLicenseGraph $packages -PackageVersion '1.0.0' " +
            "-ThirdPartyComponents $components)\n" +
            "$rows=@($graph | ForEach-Object {" +
            "[pscustomobject]@{name=$_.Name;versionInfo=$_.Version;" +
            "downloadLocation='NOASSERTION';filesAnalyzed=$false;" +
            "licenseDeclared='MIT';licenseConcluded='MIT'}})\n" +
            "$first=@($rows | Where-Object name -eq 'SharpProof')[0]\n" +
            "$third=@($rows | Where-Object name -eq 'Microsoft.Z3')[0]\n" +
            "switch ($Mutation) {\n" +
            " 'first-noassertion' {$first.licenseDeclared='NOASSERTION'}\n" +
            " 'first-wrong' {$first.licenseConcluded='Apache-2.0'}\n" +
            " 'first-case' {$first.licenseDeclared='mit'}\n" +
            " 'first-missing' {$first.PSObject.Properties.Remove(" +
            "'licenseDeclared')}\n" +
            " 'first-extra' {$first | Add-Member NoteProperty " +
            "licenseComments extra}\n" +
            " 'third-noassertion' {$third.licenseConcluded='NOASSERTION'}\n" +
            " 'third-wrong' {$third.licenseDeclared='BSD-3-Clause'}\n" +
            " 'third-case' {$third.licenseConcluded='mit'}\n" +
            " 'third-missing' {$third.PSObject.Properties.Remove(" +
            "'licenseConcluded')}\n" +
            " 'third-extra' {$third | Add-Member NoteProperty " +
            "licenseInfoFromFiles @('MIT')}\n" +
            " 'unknown-component' {$rows += [pscustomobject]@{" +
            "name='Foreign';versionInfo='1';downloadLocation='NOASSERTION';" +
            "filesAnalyzed=$false;licenseDeclared='MIT';" +
            "licenseConcluded='MIT'}}\n" +
            " 'duplicate-component' {$rows += $third}\n" +
            " 'wrong-download' {$rows[0].downloadLocation='https://invalid'}\n" +
            " 'files-analyzed' {$rows[0].filesAnalyzed=$true}\n" +
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
