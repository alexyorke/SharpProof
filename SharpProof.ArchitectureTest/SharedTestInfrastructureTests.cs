using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class SharedTestInfrastructureTests
{
    [Test]
    public void SharedMetadataReferenceProjectsDoNotReadTrustedAssembliesDirectly()
    {
        var root = TestRepository.FindRoot();
        var projects = new[]
        {
            "SharpProof.Contracts.Test",
            "SharpProof.Worker.Test"
        };
        var directReads = projects
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(root, project),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(path => File.ReadAllText(path).Contains(
                "TRUSTED_PLATFORM_ASSEMBLIES",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            directReads,
            Is.Empty,
            "Projects linked to TestMetadataReferences.cs must use its " +
            "centralized platform-reference policy instead of reading " +
            "TRUSTED_PLATFORM_ASSEMBLIES directly.");
    }
}
