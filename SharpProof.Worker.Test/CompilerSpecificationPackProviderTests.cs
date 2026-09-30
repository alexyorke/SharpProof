using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerSpecificationPackProviderTests
{
    [Test]
    public void ExplicitPackProviderUsesTheValidatedCatalogSelection()
    {
        Assert.That(new CompilerSpecificationPackProvider(new IrFactory(), ["dotnet.scalar"]), Is.Not.Null);
        Assert.Throws<InvalidOperationException>((Action)(() =>
            new CompilerSpecificationPackProvider(new IrFactory(), ["missing.pack"])));
    }

    [Test]
    public void SerializedPackSelectionsRejectAmbiguousAndForeignIdentities()
    {
        var version = CompilerSpecificationPackCatalogVersions.Current;
        var hash = CompilerSpecificationPackCatalogVersions.Sha256;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CompilerSpecificationPackSelection.IsValid(["dotnet.scalar"], version, hash), Is.True);
            Assert.That(CompilerSpecificationPackSelection.IsValid(["dotnet.scalar", "dotnet.scalar"], version, hash), Is.False);
            Assert.That(CompilerSpecificationPackSelection.IsValid(["z.pack", "dotnet.scalar"], version, hash), Is.False);
            Assert.That(CompilerSpecificationPackSelection.IsValid(["dotnet.scalar"], version + 1, hash), Is.False);
            Assert.That(CompilerSpecificationPackSelection.IsValid(null, version, hash), Is.False);
            Assert.That(CompilerSpecificationPackSelection.Matches(new CompilerManifestArtifact
            {
                SpecificationPackIds = ["dotnet.scalar"],
                SpecificationPackCatalogVersion = version,
                SpecificationPackCatalogSha256 = hash
            }), Is.True);
            Assert.That(CompilerSpecificationPackSelection.Matches(new CompilerManifestArtifact
            {
                Compilation = null!
            }), Is.False);
            Assert.That(CompilerSpecificationPackSelection.GetSummaryPrefix((CompilerSummaryOrigin)int.MaxValue), Is.Null);
        }
    }

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
