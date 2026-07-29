using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ApiSpecResolutionCoverageTests
{
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
