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
    public void ReferenceFamilyWithoutAssemblyMetadataFailsClosed()
    {
        Assert.That(
            ApiSpecResolver.HasExpectedReferenceMetadata(
                null,
                ApiSpecReferenceFamily.MicrosoftNetCoreReferencePack),
            Is.False);
    }
}
