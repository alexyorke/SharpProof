using System.Collections.Immutable;
namespace SharpProof.Effects.Test;

[TestFixture]
internal sealed class EffectValueValidationTests
{
    [Test]
    public void ContractMetadataIntegerConversionRejectsInvalidValues()
    {
        ImmutableArray<object> invalidValues = [
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

    [Test]
    public void CompleteEffectSummaryRejectsImpossibleEffectClaims()
    {
        static EffectSummary Summary(
            EffectAllocationKind allocation = EffectAllocationKind.None,
            EffectTermination termination = EffectTermination.Terminates,
            EffectUncertainty uncertainty = EffectUncertainty.None,
            EffectAnalysisIncompleteReason incompleteReason = EffectAnalysisIncompleteReason.None)
        {
            return new EffectSummary(
                EffectRegionSet.Empty, EffectRegionSet.Empty, allocation,
                EffectCapabilitySet.Empty, EffectThrowSet.Empty, termination,
                EffectCompleteness.Complete, uncertainty, incompleteReason);
        }

        const EffectAllocationKind allocationMarker = EffectAllocationKind.Unknown & ~EffectAllocationKind.ManagedAndNative;
        const EffectUncertainty uncertaintyMarker = EffectUncertainty.Unknown & ~EffectUncertainty.All;
        var invalidClaims = ImmutableArray.Create<(string Name, Action Construct, string Parameter)>(
            ("unknown allocation bit", () => _ = Summary(allocation: (EffectAllocationKind)(-1)), "allocation"),
            ("partial unknown allocation", () => _ = Summary(allocation: allocationMarker), "allocation"),
            ("unreachable termination", () => _ = Summary(termination: EffectTermination.Bottom), "termination"),
            ("unknown uncertainty bit", () => _ = Summary(uncertainty: (EffectUncertainty)(-1)), "uncertainty"),
            ("partial unknown uncertainty", () => _ = Summary(uncertainty: uncertaintyMarker), "uncertainty"),
            ("unknown incomplete reason", () => _ = Summary(incompleteReason: (EffectAnalysisIncompleteReason)(-1)),
                "analysisIncompleteReason"),
            ("complete with incomplete evidence", () => _ = Summary(
                incompleteReason: EffectAnalysisIncompleteReason.BlockBudgetExceeded), "analysisIncompleteReason")
        );

        foreach (var (name, construct, parameter) in invalidClaims)
        {
            var error = Assert.Catch<ArgumentException>(construct, name);
            Assert.That(error!.ParamName, Is.EqualTo(parameter), name);
        }
    }
}
