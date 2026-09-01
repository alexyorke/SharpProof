using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ContractApiCatalogTests
{
    [Test]
    public void GeneratedCatalogPreservesTheContractSurface()
    {
        Assert.That(
            ContractApiCatalog.Methods.Select(static method => method.Name),
            Is.EqualTo([
                "Requires",
                "Ensures",
                "Assume",
                "Old",
                "Result"]));
        Assert.That(
            ContractApiCatalog.Attributes.Select(
                static attribute => attribute.MetadataName),
            Is.EqualTo([
                "SharpProof.Attributes.ContractForAttribute",
                "SharpProof.Attributes.EnforcePureAttribute",
                "SharpProof.Attributes.ZeroAllocationsAttribute",
                "SharpProof.Attributes.AllowedCapabilitiesAttribute",
                "SharpProof.Attributes.DoesNotThrowAttribute",
                "SharpProof.Attributes.AllowedExceptionsAttribute",
                "SharpProof.Attributes.EffectContractAttribute",
                "SharpProof.Attributes.NotNullAttribute",
                "SharpProof.Attributes.PositiveAttribute",
                "SharpProof.Attributes.InRangeAttribute",
                "SharpProof.Attributes.SharpProofSuppressAttribute",
                "SharpProof.Attributes.SharpProofTrustedAttribute"]));
        Assert.That(
            ContractApiMetadata.AttributeMetadataNames,
            Is.EqualTo(ContractApiCatalog.Attributes.Select(
                static attribute => attribute.MetadataName)));
    }

    [Test]
    public void GeneratedCatalogContainsNoLookupAlgorithms()
    {
        var source = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "SharpProof.Frontend",
            "ContractApiMetadata.generated.cs"));

        Assert.That(source, Does.Not.Contain("foreach"));
        Assert.That(source, Does.Not.Contain("TryGetMethod"));
        Assert.That(source, Does.Not.Contain("TryGetAttribute"));
        Assert.That(source, Does.Not.Contain("IsClosedAttributeTypeName"));
    }

}
