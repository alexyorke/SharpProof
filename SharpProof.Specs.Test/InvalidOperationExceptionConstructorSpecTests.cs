using NUnit.Framework;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class InvalidOperationExceptionConstructorSpecTests
{
    [Test]
    public void ParameterlessConstructorIncludesResourceLookupEffects()
    {
        var template = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier ==
                "bcl.invalid-operation-exception.ctor");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                template.Facets.Effects.Effects,
                Is.EqualTo(
                    SpecEffect.WritesReceiverState |
                    SpecEffect.ReadsAmbientState |
                    SpecEffect.Synchronization));
            Assert.That(
                template.Facets.Allocation.Behavior,
                Is.EqualTo(SpecAllocationBehavior.MayAllocate));
            Assert.That(
                template.Facets.Effects.Evidence.Kind,
                Is.EqualTo(SpecEvidenceKind.Observed));
        }
    }
}
