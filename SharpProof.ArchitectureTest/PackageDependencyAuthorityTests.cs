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
    [TestCase("id-duplicate", false)]
    [TestCase("version-duplicate", false)]
    [TestCase("id-missing", false)]
    [TestCase("version-missing", false)]
    [TestCase("id-attributed", false)]
    [TestCase("version-nested", false)]
    [TestCase("id-whitespace", false)]
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

    [TestCase("canonical", true)]
    [TestCase("fabricated", false)]
    [TestCase("missing", false)]
    [TestCase("duplicate", false)]
    [TestCase("swapped-owner", false)]
    [TestCase("foreign-entry", false)]
    [TestCase("self-consistent-rewrite", false)]
    [TestCase("missing-containment", false)]
    [TestCase("extra-containment", false)]
    public async Task ThirdPartyInventoryMatchesCatalogPayloadAndSbomOwnership(
        string mutation,
        bool expectedSuccess)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-component-authority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await RunComponentAuthorityAsync(root, mutation);
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
    [TestCase("extra-contains", false)]
    [TestCase("extra-depends", false)]
    [TestCase("other-type", false)]
    [TestCase("missing", false)]
    [TestCase("reversed", false)]
    [TestCase("duplicate", false)]
    [TestCase("wrong-spdx", false)]
    [TestCase("self-consistent-spdx", false)]
    [TestCase("id-collision", false)]
    [TestCase("extra-package", false)]
    [TestCase("extra-describes", false)]
    public async Task SbomTopologyIsTheExactAuthenticatedProjection(
        string mutation,
        bool expectedSuccess)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-sbom-topology-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await RunSbomTopologyAuthorityAsync(root, mutation);
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

    [Test]
    public async Task EveryReleaseAuthorityUsesTheExactSbomTopology()
    {
        var root = FindRepositoryRoot();
        foreach (var scriptName in new[]
                 {
                     "New-SharpProofReleaseEvidence.ps1",
                     "Test-SharpProofReleaseArtifacts.ps1",
                     "Publish-SharpProofRelease.ps1"
                 })
        {
            var script = await File.ReadAllTextAsync(
                Path.Combine(root, "scripts", scriptName));
            Assert.That(
                script,
                Does.Contain("Test-SharpProofSbomTopology"),
                scriptName);
            Assert.That(
                script,
                Does.Contain("Test-SharpProofSpdxPackageChecksum"),
                scriptName);
        }
    }

    [TestCase("first-canonical", true)]
    [TestCase("third-canonical", true)]
    [TestCase("duplicate-same", false)]
    [TestCase("duplicate-different", false)]
    [TestCase("extra-algorithm", false)]
    [TestCase("missing", false)]
    [TestCase("wrong", false)]
    [TestCase("stale", false)]
    [TestCase("wrong-case", false)]
    [TestCase("extra-property", false)]
    [TestCase("missing-property", false)]
    [TestCase("scalar", false)]
    [TestCase("null", false)]
    [TestCase("object", false)]
    public async Task SpdxChecksumRowsAreExact(
        string mutation,
        bool expectedSuccess)
    {
        var root = FindRepositoryRoot();
        var result = await RunSpdxChecksumAuthorityAsync(root, mutation);
        Assert.That(
            result.ExitCode == 0,
            Is.EqualTo(expectedSuccess),
            result.Output);
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
                        not "metadata-symbol-mismatch" and
                        not "id-duplicate" and
                        not "version-duplicate" and
                        not "id-missing" and
                        not "version-missing" and
                        not "id-attributed" and
                        not "version-nested" and
                        not "id-whitespace")
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
                var identity = IdentityXml(id, effectiveMutation);
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
                        {{identity}}
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

    private static string IdentityXml(string id, string mutation)
    {
        var idNode = mutation switch
        {
            "id-missing" => string.Empty,
            "id-attributed" => $"<id xml:lang=\"en\">{id}</id>",
            "id-whitespace" => $"<id> {id} </id>",
            _ => $"<id>{id}</id>"
        };
        if (mutation == "id-duplicate")
        {
            idNode += $"<id>{id}</id>";
        }

        var versionNode = mutation switch
        {
            "version-missing" => string.Empty,
            "version-nested" => $"<version><value>{Version}</value></version>",
            _ => $"<version>{Version}</version>"
        };
        if (mutation == "version-duplicate")
        {
            versionNode += $"<version>{Version}</version>";
        }

        return idNode + Environment.NewLine + "                        " + versionNode;
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

    private static async Task<ProcessResult> RunComponentAuthorityAsync(
        string root,
        string mutation)
    {
        var repositoryRoot = FindRepositoryRoot();
        var runner = Path.Combine(root, "run-component-authority.ps1");
        await File.WriteAllTextAsync(
            runner,
            "param([string]$Helper, [string]$Mutation)\n" +
            ". $Helper\n" +
            "$expected=@(" +
            "[pscustomobject]@{packageId='SharpProof';id='Component.A';" +
            "version='1.0';license='MIT';entries=@('tools/a.dll')}," +
            "[pscustomobject]@{packageId='SharpProof.Verifier';id='Component.B';" +
            "version='2.0';license='MIT';entries=@('tools/b.so')})\n" +
            "$actual=@($expected | ConvertTo-Json -Depth 4 | ConvertFrom-Json)\n" +
            "$packages=@($expected | ForEach-Object {[pscustomobject]@{" +
            "name=$_.id;versionInfo=$_.version}})\n" +
            "$relationships=@($expected | ForEach-Object {[pscustomobject]@{" +
            "spdxElementId=(Get-SharpProofDependencySpdxId $_.packageId);" +
            "relationshipType='CONTAINS';relatedSpdxElement=" +
            "(Get-SharpProofDependencySpdxId ($_.id+'-'+$_.version))}})\n" +
            "switch ($Mutation) {\n" +
            " 'fabricated' {$actual[0].id='Fabricated'}\n" +
            " 'missing' {$actual=@($actual[0])}\n" +
            " 'duplicate' {$actual+= $actual[0]}\n" +
            " 'swapped-owner' {$actual[0].packageId='SharpProof.Verifier'}\n" +
            " 'foreign-entry' {$actual[0].entries=@('tools/foreign.dll')}\n" +
            " 'self-consistent-rewrite' {$actual[0].id='Fabricated';" +
            "$packages[0].name='Fabricated';$relationships[0].relatedSpdxElement=" +
            "(Get-SharpProofDependencySpdxId 'Fabricated-1.0')}\n" +
            " 'missing-containment' {$relationships=@($relationships[0])}\n" +
            " 'extra-containment' {$relationships+= $relationships[0]}\n" +
            "}\n" +
            "Test-SharpProofThirdPartyComponentProjection " +
            "-ActualComponents $actual -ExpectedComponents $expected\n" +
            "Test-SharpProofSbomComponentGraph -SbomPackages $packages " +
            "-Relationships $relationships -Components $expected\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return await RunPowerShellAsync(repositoryRoot, runner, mutation);
    }

    private static async Task<ProcessResult> RunSbomTopologyAuthorityAsync(
        string root,
        string mutation)
    {
        var repositoryRoot = FindRepositoryRoot();
        var runner = Path.Combine(root, "run-sbom-topology-authority.ps1");
        await File.WriteAllTextAsync(
            runner,
            "param([string]$Helper,[string]$Mutation)\n" +
            ". $Helper\n" +
            "$ids=@('SharpProof.Attributes','SharpProof','SharpProof.Verifier')\n" +
            "$version='1.0.0-preview.1'\n" +
            "$components=@(" +
            "[pscustomobject]@{packageId='SharpProof';id='Component.A';version='1'}," +
            "[pscustomobject]@{packageId='SharpProof.Verifier';id='Component.B';version='2'})\n" +
            "$graph=@(" +
            "[pscustomobject]@{FromId='SharpProof';ToId='SharpProof.Attributes'}," +
            "[pscustomobject]@{FromId='SharpProof.Verifier';ToId='SharpProof'})\n" +
            "$packages=@($ids|ForEach-Object{[pscustomobject]@{" +
            "name=$_;versionInfo=$version;SPDXID=(Get-SharpProofDependencySpdxId $_);" +
            "externalRefs=@([pscustomobject][ordered]@{referenceCategory='PACKAGE-MANAGER';" +
            "referenceType='purl';referenceLocator=(Get-SharpProofNuGetPurl $_ $version)})}})\n" +
            "$packages+=@($components|ForEach-Object{[pscustomobject]@{" +
            "name=$_.id;versionInfo=$_.version;SPDXID=" +
            "(Get-SharpProofDependencySpdxId ($_.id+'-'+$_.version));" +
            "externalRefs=@([pscustomobject][ordered]@{referenceCategory='PACKAGE-MANAGER';" +
            "referenceType='purl';referenceLocator=(Get-SharpProofNuGetPurl $_.id $_.version)})}})\n" +
            "$describes=@($ids|ForEach-Object{Get-SharpProofDependencySpdxId $_})\n" +
            "$rows=@($ids|ForEach-Object{[pscustomobject]@{" +
            "spdxElementId='SPDXRef-DOCUMENT';relationshipType='DESCRIBES';" +
            "relatedSpdxElement=(Get-SharpProofDependencySpdxId $_)}})\n" +
            "$rows+=@($components|ForEach-Object{[pscustomobject]@{" +
            "spdxElementId=(Get-SharpProofDependencySpdxId $_.packageId);" +
            "relationshipType='CONTAINS';relatedSpdxElement=" +
            "(Get-SharpProofDependencySpdxId ($_.id+'-'+$_.version))}})\n" +
            "$rows+=@($graph|ForEach-Object{[pscustomobject]@{" +
            "spdxElementId=(Get-SharpProofDependencySpdxId $_.FromId);" +
            "relationshipType='DEPENDS_ON';relatedSpdxElement=" +
            "(Get-SharpProofDependencySpdxId $_.ToId)}})\n" +
            "switch($Mutation){\n" +
            " 'extra-contains' {$rows+=[pscustomobject]@{spdxElementId=$rows[3].spdxElementId;relationshipType='CONTAINS';relatedSpdxElement=$rows[4].relatedSpdxElement}}\n" +
            " 'extra-depends' {$rows+=[pscustomobject]@{spdxElementId=$rows[5].spdxElementId;relationshipType='DEPENDS_ON';relatedSpdxElement=$rows[6].relatedSpdxElement}}\n" +
            " 'other-type' {$rows[0].relationshipType='GENERATED_FROM'}\n" +
            " 'missing' {$rows=@($rows[1..($rows.Count-1)])}\n" +
            " 'reversed' {$v=$rows[5].spdxElementId;$rows[5].spdxElementId=$rows[5].relatedSpdxElement;$rows[5].relatedSpdxElement=$v}\n" +
            " 'duplicate' {$rows+=$rows[0]}\n" +
            " 'wrong-spdx' {$packages[0].SPDXID='SPDXRef-Package-Wrong'}\n" +
            " 'self-consistent-spdx' {$packages[3].SPDXID='SPDXRef-Package-Fabricated';$rows[3].relatedSpdxElement='SPDXRef-Package-Fabricated'}\n" +
            " 'id-collision' {$packages[3].SPDXID=$packages[0].SPDXID}\n" +
            " 'extra-package' {$packages+=[pscustomobject]@{name='Extra';versionInfo='1';SPDXID='SPDXRef-Package-Extra'}}\n" +
            " 'extra-describes' {$describes+='SPDXRef-Package-Extra'}\n" +
            "}\n" +
            "Test-SharpProofSbomTopology -SbomPackages $packages " +
            "-DocumentDescribes $describes -Relationships $rows " +
            "-FirstPartyPackageIds $ids -PackageVersion $version " +
            "-Components $components -DependencyGraph $graph\n",
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

    private static async Task<ProcessResult> RunSpdxChecksumAuthorityAsync(
        string repositoryRoot,
        string mutation)
    {
        var runner = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-spdx-checksum-" + Guid.NewGuid().ToString("N") +
            ".ps1");
        try
        {
            await File.WriteAllTextAsync(
                runner,
                "param([string]$Authority,[string]$Mutation)\n" +
                "$ErrorActionPreference='Stop'\n" +
                ". $Authority\n" +
                "$hash='" + new string('a', 64) + "'\n" +
                "$row=[pscustomobject][ordered]@{algorithm='SHA256';checksumValue=$hash}\n" +
                "$rows=@($row)\n" +
                "switch($Mutation){\n" +
                " 'duplicate-same' {$rows=@($row,$row)}\n" +
                " 'duplicate-different' {$rows=@($row,[pscustomobject][ordered]@{algorithm='SHA256';checksumValue=('0'*64)})}\n" +
                " 'extra-algorithm' {$rows=@($row,[pscustomobject][ordered]@{algorithm='SHA1';checksumValue=('0'*40)})}\n" +
                " 'missing' {$rows=@()}\n" +
                " 'wrong' {$rows=@([pscustomobject][ordered]@{algorithm='SHA256';checksumValue=('0'*64)})}\n" +
                " 'stale' {$rows=@([pscustomobject][ordered]@{algorithm='SHA256';checksumValue=('b'*64)})}\n" +
                " 'wrong-case' {$rows=@([pscustomobject][ordered]@{algorithm='sha256';checksumValue=$hash})}\n" +
                " 'extra-property' {$rows=@([pscustomobject][ordered]@{algorithm='SHA256';checksumValue=$hash;comment='decoy'})}\n" +
                " 'missing-property' {$rows=@([pscustomobject][ordered]@{algorithm='SHA256'})}\n" +
                " 'scalar' {$rows='SHA256:'+ $hash}\n" +
                " 'null' {$rows=$null}\n" +
                " 'object' {$rows=$row}\n" +
                "}\n" +
                "$package=[pscustomobject]@{name=$Mutation;checksums=$rows}\n" +
                "Test-SharpProofSpdxPackageChecksum -Package $package -ExpectedSha256 $hash -Identity $Mutation\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return await RunPowerShellAsync(
                repositoryRoot,
                runner,
                mutation);
        }
        finally
        {
            File.Delete(runner);
        }
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
