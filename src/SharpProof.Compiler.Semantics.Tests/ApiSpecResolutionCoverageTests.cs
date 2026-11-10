using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ApiSpecResolutionCoverageTests
{
    [Test]
    public void NuGetRestoredFrameworkReferencePackIsApproved()
    {
        Assert.That(
            EffectContractMappingCatalog.ReferenceFamilyMarkers,
            Does.Contain((
                "/MICROSOFT.NETCORE.APP.REF/",
                ApiSpecReferenceFamily.MicrosoftNetCoreReferencePack)));
    }

    [Test]
    public void FrameworkMarkersRequireACompleteDirectorySegment()
    {
        var markers = EffectContractMappingCatalog.ReferenceFamilyMarkers;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                markers,
                Does.Contain((
                    "/REFERENCEPACKS/NETSTANDARD/",
                    ApiSpecReferenceFamily.NetStandardReferencePack)));
            Assert.That(
                markers,
                Does.Contain((
                    "/PACKAGES/MICROSOFT.NETFRAMEWORK.REFERENCEASSEMBLIES/",
                    ApiSpecReferenceFamily.NetFrameworkReferenceAssemblies)));
            Assert.That(
                markers,
                Does.Contain((
                    "/REFERENCEPACKS/NET47/",
                    ApiSpecReferenceFamily.NetFrameworkReferenceAssemblies)));
        }
    }

    [Test]
    public void ReferenceFamilyWithoutAssemblyMetadataFailsClosed()
    {
        Assert.That(
            ApiSpecResolver.HasExpectedReferenceMetadata(
                null,
                ApiSpecReferenceFamily.MicrosoftNetCoreReferencePack),
            Is.False);
    }
}
