using NUnit.Framework;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class InvalidOperationExceptionConstructorSpecTests
{
    [TestCase("bcl.invalid-operation-exception.ctor")]
    [TestCase("bcl.invalid-operation-exception.ctor.string")]
    public void ResourceMessageConstructorsRetainArbitraryCallbackFacets(string witness)
    {
        ArgumentNullException.ThrowIfNull(witness);
        var template = ApiSpecTable.Default.Templates.Single(
            row => row is null ? throw new System.ArgumentNullException(nameof(row)) : row.Target.WitnessIdentifier == witness);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                template.Facets.Effects.Effects,
                Is.EqualTo(SpecEffect.Unknown));
            Assert.That(
                template.Facets.Allocation.Behavior,
                Is.EqualTo(SpecAllocationBehavior.MayAllocate));
            Assert.That(
                template.Facets.Effects.Evidence.Kind,
                Is.EqualTo(SpecEvidenceKind.Observed));
            Assert.That(template.Facets.Throws.Behavior, Is.EqualTo(SpecThrowBehavior.Unknown));
            Assert.That(template.Facets.Termination!.Behavior, Is.EqualTo(SpecTerminationBehavior.Unknown));
            Assert.That(template.Facets.Effects.Evidence.Source, Is.EqualTo("dotnet-resource-message-culture-callback-boundary"));
        }
    }
}
