using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class PublicationPlanIdentityTests
{
    [TestCase("canonical", true)]
    [TestCase("two-bundle", true)]
    [TestCase("changed-symbol", false)]
    [TestCase("stale-manifest", false)]
    [TestCase("same-length-byte-mutation", false)]
    [TestCase("missing-identity", false)]
    [TestCase("duplicate-identity", false)]
    [TestCase("version-syntax", true)]
    [TestCase("commit-syntax", true)]
    [TestCase("string-schema", false)]
    [TestCase("decimal-bytes", false)]
    [TestCase("array-version", false)]
    [TestCase("array-commit", false)]
    [TestCase("array-artifact-text", false)]
    [TestCase("destination-tamper", false)]
    [TestCase("package-action-tamper", false)]
    [TestCase("fixture-canonical", true)]
    [TestCase("fixture-authority-tamper", false)]
    [TestCase("fixture-nonexistent-archive", true)]
    [TestCase("registry-canonical", true)]
    [TestCase("registry-url-tamper", false)]
    [TestCase("registry-verified-canonical", true)]
    [TestCase("registry-verified-digest-tamper", false)]
    [TestCase("registry-verified-action-tamper", false)]
    [TestCase("targetless-publish-tamper", false)]
    [TestCase("json-roundtrip", true)]
    public async Task ReplayRehashesEveryImmutablePlanInput(
        string mutation,
        bool expectedValid)
    {
        var result = await ArchitectureRepository.RunScriptAsync(
            TestRepository.FindRoot(), "Test-SharpProofPublicationPlanIdentityFixtures.ps1",
            "-Mutation", mutation);
        Assert.That(result.ExitCode == 0, Is.EqualTo(expectedValid), result.CombinedOutput);
    }

    [Test]
    public async Task PublisherValidatesCurrentIdentitiesBeforeAndAfterWritingPlan()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(
            TestRepository.FindRoot(), "scripts", "Publish-SharpProofRelease.ps1"));
        var create = script.IndexOf(
            "New-SharpProofPublicationPlanIdentities", StringComparison.Ordinal);
        var validationCall = System.Text.RegularExpressions.Regex.Match(
            script,
            @"^\s*Test-SharpProofPublicationPlanIdentity -Plan \$plan\r?$",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.That(validationCall.Success, Is.True,
            "The publisher must execute plan identity validation before writing the plan.");
        var validate = validationCall.Index;
        var write = script.IndexOf(
            "Write-PublicationPlan `", validate, StringComparison.Ordinal);
        var replay = script.IndexOf(
            "Assert-PublicationPlanRoundTrip `", write, StringComparison.Ordinal);
        Assert.That(create, Is.GreaterThanOrEqualTo(0));
        Assert.That(validate, Is.GreaterThan(create));
        Assert.That(write, Is.GreaterThan(validate));
        Assert.That(replay, Is.GreaterThan(write));
    }
}
