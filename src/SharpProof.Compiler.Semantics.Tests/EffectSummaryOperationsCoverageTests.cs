namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectSummaryOperationsCoverageTests
{
    [Test]
    public void RemapRejectsDefaultArgumentArrayForNonBottomSummary()
    {
        var summary = EffectSummaryOperations.Read(EffectRegionSet.Empty);
        Assert.That(
            (Action)(() => EffectSummaryOperations.Remap(summary, EffectRegionSet.Empty, default)),
            Throws.ArgumentNullException);
    }
}
