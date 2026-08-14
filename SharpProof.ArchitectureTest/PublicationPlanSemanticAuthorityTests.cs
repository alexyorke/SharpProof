using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class PublicationPlanSemanticAuthorityTests
{
    private const string SbomParseAuthority =
        "    $sbom = Get-Content -LiteralPath $sbomPath -Raw |\n" +
        "        ConvertFrom-Json";

    [TestCase("canonical", true)]
    [TestCase("malformed-rebound", false)]
    [TestCase("wrong-topology", false)]
    [TestCase("wrong-license", false)]
    [TestCase("wrong-component", false)]
    [TestCase("wrong-package-checksum", false)]
    [TestCase("wrong-artifact-scope", false)]
    [TestCase("inconsistent-checksums", false)]
    [TestCase("validation-removed", false)]
    [TestCase("validation-after-actions", false)]
    public async Task PublicationPlanConsumesStrictReleaseSemanticsBeforeActions(
        string mutation,
        bool expectedValid)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "scripts", "Publish-SharpProofRelease.ps1"));
        script = mutation switch
        {
            "malformed-rebound" => Remove(script, SbomParseAuthority),
            "wrong-topology" => Remove(
                script, "    Test-SharpProofSbomTopology `"),
            "wrong-license" => Remove(
                script, "    Test-SharpProofSbomLicenseGraph `"),
            "wrong-component" => Remove(
                script, "    Test-SharpProofSbomComponentGraph `"),
            "wrong-package-checksum" => Remove(
                script, "        Test-SharpProofSpdxPackageChecksum `"),
            "wrong-artifact-scope" => Remove(
                script, "    Test-SharpProofSbomArtifactScope `"),
            "inconsistent-checksums" => Remove(
                script, "    Test-SharpProofReleaseChecksumFile `"),
            "validation-removed" => Remove(
                script, "$release = Get-ValidatedRelease `"),
            "validation-after-actions" => script.Replace(
                "$release = Get-ValidatedRelease `",
                "$entries = [Collections.Generic.List[object]]::new()\n" +
                "$release = Get-ValidatedRelease `",
                StringComparison.Ordinal),
            _ => script
        };

        Assert.That(
            HasStrictPlanSemanticAuthority(script),
            Is.EqualTo(expectedValid));
    }

    private static bool HasStrictPlanSemanticAuthority(string script)
    {
        var functionStart = script.IndexOf(
            "function Get-ValidatedRelease {", StringComparison.Ordinal);
        var functionEnd = script.IndexOf(
            "\nfunction Invoke-V3Get {", functionStart, StringComparison.Ordinal);
        if (functionStart < 0 || functionEnd < 0)
        {
            return false;
        }

        var body = script[functionStart..functionEnd];
        var requiredBodyAuthorities = new[]
        {
            SbomParseAuthority,
            "Test-SharpProofReleaseBundleTopology",
            "Test-SharpProofReleaseChecksumFile",
            "Test-SharpProofSpdxPackageChecksum",
            "Test-SharpProofSbomTopology",
            "Test-SharpProofSbomArtifactScope",
            "Test-SharpProofSbomDependencyGraph",
            "Test-SharpProofSbomComponentGraph",
            "Test-SharpProofSbomLicenseGraph"
        };
        if (requiredBodyAuthorities.Any(authority =>
                !body.Contains(authority, StringComparison.Ordinal)))
        {
            return false;
        }

        var validation = script.IndexOf(
            "$release = Get-ValidatedRelease `", StringComparison.Ordinal);
        var actions = script.IndexOf(
            "$entries = [Collections.Generic.List[object]]::new()",
            StringComparison.Ordinal);
        return validation >= 0 && actions > validation;
    }

    private static string Remove(string script, string authority)
    {
        Assert.That(
            script.Split(authority, StringSplitOptions.None),
            Has.Length.EqualTo(2),
            authority);
        return script.Replace(authority, string.Empty, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
