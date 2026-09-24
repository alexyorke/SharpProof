using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class PublicationDestinationAuthorityTests
{
    [TestCase("registry-inherited", true)]
    [TestCase("registry-distinct", true)]
    [TestCase("targetless", true)]
    [TestCase("fixture", true)]
    [TestCase("http", false)]
    [TestCase("relative", false)]
    [TestCase("userinfo", false)]
    [TestCase("query", false)]
    [TestCase("fragment", false)]
    [TestCase("symbol-without-main", false)]
    [TestCase("fixture-uri-conflict", false)]
    [TestCase("missing-fixture", false)]
    [TestCase("changed-fixture", false)]
    [TestCase("removed-symbol-projection", false)]
    [TestCase("actions-targetless", true)]
    [TestCase("actions-fixture", true)]
    [TestCase("actions-registry-unchecked", true)]
    [TestCase("actions-registry-absent", true)]
    [TestCase("actions-registry-verified", true)]
    [TestCase("actions-symbol-preflight", false)]
    [TestCase("actions-swapped", false)]
    [TestCase("actions-removed-projection", false)]
    [TestCase("mocked-main-missing", true)]
    [TestCase("mocked-main-exists", true)]
    [TestCase("mocked-main-exists-match", true)]
    [TestCase("mocked-main-exists-mismatch", true)]
    [TestCase("mocked-main-error", true)]
    [TestCase("mocked-main-query-base", true)]
    [TestCase("fixture-empty", true)]
    [TestCase("fixture-foreign", true)]
    [TestCase("fixture-main-case-collision", true)]
    [TestCase("fixture-symbol-case-collision", true)]
    [TestCase("fixture-arbitrary-name", true)]
    [TestCase("fixture-wrong-id", true)]
    [TestCase("fixture-wrong-version", true)]
    [TestCase("fixture-nested-collision", true)]
    [TestCase("fixture-malformed", false)]
    [TestCase("fixture-cross-role", false)]
    [TestCase("fixture-duplicate", false)]
    public async Task PublicationDestinationModesAreExactAndAuthenticated(
        string mutation,
        bool expectedSuccess)
    {
        var result = await ArchitectureRepository.RunScriptAsync(
            TestRepository.FindRoot(), "Test-SharpProofPublicationDestinationFixtures.ps1",
            "-Mutation", mutation);
        Assert.That(
            result.ExitCode == 0,
            Is.EqualTo(expectedSuccess),
            result.CombinedOutput);
    }

    [Test]
    public async Task PublisherProjectsBothDestinationsBeforePlanReturn()
    {
        var text = await File.ReadAllTextAsync(Path.Combine(
            TestRepository.FindRoot(), "scripts", "Publish-SharpProofRelease.ps1"));
        Assert.That(text, Does.Contain("New-SharpProofPublicationDestinationAuthority"));
        Assert.That(text, Does.Contain("publicationDestination ="));
        Assert.That(text, Does.Contain("New-SharpProofPublicationActionAuthority"));
        Assert.That(text, Does.Contain("Test-SharpProofPublicationPlanIdentity"));
        Assert.That(text, Does.Contain("symbolsAction = $action.symbolsAction"));
        Assert.That(text, Does.Contain("-FixtureCatalog $fixtureCatalog"));
        Assert.That(text, Does.Contain("$remote.mainState"));
        Assert.That(text, Does.Contain("$remote.symbolsState"));
        Assert.That(text, Does.Not.Contain("source = if ("));
    }
}
