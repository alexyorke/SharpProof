namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectValueValidationTests
{
    [Test]
    public void ContractMetadataIntegerConversionRejectsInvalidValues()
    {
        object[] invalidValues = [
            new object(),
            "not-an-integer",
            decimal.MaxValue
        ];

        foreach (var value in invalidValues)
        {
            Assert.That(
                EffectContractMetadata.TryConvertInt64(value, out var result),
                Is.False);
            Assert.That(result, Is.Zero);
        }
    }

    [TestCase((EffectRegionKind)(-1), 0)]
    [TestCase(EffectRegionKind.Parameter, -1)]
    [TestCase(EffectRegionKind.Receiver, 1)]
    public void RegionIdentifiersRejectInvalidKindAndOrdinalPairs(
        EffectRegionKind kind,
        int ordinal)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() =>
                _ = new EffectRegionId(kind, ordinal)));
    }
}
