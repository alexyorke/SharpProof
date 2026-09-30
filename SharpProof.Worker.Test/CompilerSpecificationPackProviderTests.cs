using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerSpecificationPackProviderTests
{
    [Test]
    public void SelectionAuthorityIsExplicitCanonicalAndCatalogBound()
    {
        var unset = CompilerSpecificationPackProvider.ResolveConfiguration(null);
        var selected = CompilerSpecificationPackProvider.ResolveConfiguration(
            [" dotnet.scalar "]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unset.SpecificationPackIds, Is.Empty);
            Assert.That(selected.SpecificationPackIds,
                Is.EqualTo(["dotnet.scalar"]));
            Assert.That(unset.SpecificationPackCatalogVersion,
                Is.EqualTo(CompilerSpecificationPackCatalogVersions.Current));
            Assert.That(unset.SpecificationPackCatalogSha256,
                Is.EqualTo(CompilerSpecificationPackCatalogVersions.Sha256));
        }

        Assert.Throws<InvalidOperationException>((Action)(() =>
            CompilerSpecificationPackProvider.ResolveConfiguration(
                ["dotnet.scalar", "dotnet.scalar"])));
        Assert.Throws<InvalidOperationException>((Action)(() =>
            CompilerSpecificationPackProvider.ResolveConfiguration(
                ["dotnet.scalar", "missing.pack"])));
    }
}
