namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectCapabilitySetValidationTests
{
    [TestCaseSource(nameof(PartialUnknownKinds))]
    public void ConstructorRejectsPartialUnknownKinds(EffectCapabilityKind kinds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() => _ = new EffectCapabilitySet(kinds)));
    }

    [Test]
    public void ConstructorAcceptsKnownKindsAndCanonicalUnknown()
    {
        Assert.That(
            new EffectCapabilitySet(
                EffectCapabilityKind.Console |
                EffectCapabilityKind.Synchronization).IsUnknown,
            Is.False);
        Assert.That(
            new EffectCapabilitySet(EffectCapabilityKind.Unknown),
            Is.EqualTo(EffectCapabilitySet.Unknown));
    }

    private static IEnumerable<EffectCapabilityKind> PartialUnknownKinds()
    {
        var unknownMarker =
            EffectCapabilityKind.Unknown & ~EffectCapabilityKind.AllKnown;

        yield return unknownMarker;
        yield return unknownMarker | EffectCapabilityKind.Console;
    }
}
