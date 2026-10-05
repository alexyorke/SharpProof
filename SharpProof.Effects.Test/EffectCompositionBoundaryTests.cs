namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectCompositionBoundaryTests
{
    [Test]
    public void CallRemappingPreservesDistinctReadAndWriteOwnersAndAmbientEffects()
    {
        var reads = EffectRegionSet.Create(EffectRegionId.Receiver, EffectRegionId.Parameter(0), EffectRegionId.Ambient);
        var writes = EffectRegionSet.Create(EffectRegionId.Receiver, EffectRegionId.Parameter(1), EffectRegionId.Static());
        var source = EffectSummaryOperations.Join(EffectSummaryOperations.Read(reads),
            EffectSummaryOperations.Write(writes), EffectSummaryOperations.Capability(EffectCapabilityKind.IO));
        var mapped = EffectSummaryOperations.Remap(source, EffectRegionSet.Create(EffectRegionId.Captured(3)),
            EffectRegionSet.Create(EffectRegionId.Fresh(9)),
            [EffectRegionSet.Create(EffectRegionId.Parameter(4)), EffectRegionSet.Create(EffectRegionId.Static(2))]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapped.Reads.Regions, Is.EquivalentTo(new[] { EffectRegionId.Captured(3),
                EffectRegionId.Parameter(4), EffectRegionId.Ambient }));
            Assert.That(mapped.Writes.Regions, Is.EquivalentTo(new[] { EffectRegionId.Fresh(9),
                EffectRegionId.Static(2), EffectRegionId.Static() }));
            Assert.That(mapped.Capabilities, Is.EqualTo(source.Capabilities));
            Assert.That(mapped.Completeness, Is.EqualTo(source.Completeness));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnresolvedArgumentsCannotLoseTheirEffects(bool suppliedUnknown)
    {
        var source = EffectSummaryOperations.Read(EffectRegionSet.Create(EffectRegionId.Parameter(0)));
        var arguments = suppliedUnknown ? ImmutableArray.Create(EffectRegionSet.Unknown) : [];
        var mapped = EffectSummaryOperations.Remap(source, EffectRegionSet.Empty, arguments);
        Assert.That(mapped.Reads.IsUnknown, Is.True);
        Assert.That(mapped.Writes.Regions, Is.Empty);
    }

    [Test]
    public void ExceptionConstructionKeepsConstructionEffectsAndBothThrowAlternatives()
    {
        var compilation = TestCompilation.Create("ExceptionComposition", "public class C { }");
        var argument = compilation.GetTypeByMetadataName("System.ArgumentException")!;
        var invalid = compilation.GetTypeByMetadataName("System.InvalidOperationException")!;
        var construction = EffectSummaryOperations.Join(EffectSummaryOperations.Write(EffectRegionSet.Create(EffectRegionId.Ambient)),
            EffectSummaryOperations.Capability(EffectCapabilityKind.Reflection),
            EffectSummaryOperations.Throw(EffectThrowSet.Create([argument])));
        var result = EffectSummaryOperations.ExceptionConstructionThrow(construction, EffectThrowSet.Create([invalid]));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Writes, Is.EqualTo(construction.Writes));
            Assert.That(result.Capabilities, Is.EqualTo(construction.Capabilities));
            Assert.That(result.Throws.Contains(argument), Is.True);
            Assert.That(result.Throws.Contains(invalid), Is.True);
            Assert.That(result.Allocation, Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(result.Termination, Is.EqualTo(EffectTermination.Unknown));
        }
    }

    [Test]
    public void UnknownThrowAndInitializationCannotBecomeComplete()
    {
        var unknown = EffectSummaryOperations.Throw(EffectThrowSet.Unknown);
        var initialization = EffectSummaryOperations.TypeInitializationBoundary();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unknown.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(unknown.Throws.IncludesUnknown, Is.True);
            Assert.That(unknown.Allocation, Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(initialization.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(initialization.Throws.IncludesUnknown, Is.True);
            Assert.That(initialization.Capabilities.IsUnknown, Is.True);
        }
    }

    [Test]
    public void ExceptionSetUnionIsCanonicalAndUnknownRemainsTheUpperBound()
    {
        var compilation = TestCompilation.Create("ExceptionSets", "public class C { }");
        var first = compilation.GetTypeByMetadataName("System.ArgumentException")!;
        var second = compilation.GetTypeByMetadataName("System.InvalidOperationException")!;
        var left = EffectThrowSet.Create([first]);
        var right = EffectThrowSet.Create([second]);
        var union = left.Union(right);
        var reverse = right.Union(left);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(union == reverse, Is.True);
            Assert.That(union.GetHashCode(), Is.EqualTo(reverse.GetHashCode()));
            Assert.That(left.IsSubsetOf(union), Is.True);
            Assert.That(union.IsSubsetOf(left), Is.False);
            Assert.That(left != right, Is.True);
            Assert.That(union.Union(EffectThrowSet.Empty), Is.EqualTo(union));
            Assert.That(EffectThrowSet.Empty.Union(union), Is.EqualTo(union));
            Assert.That(union.IsSubsetOf(EffectThrowSet.Unknown), Is.True);
            Assert.That(EffectThrowSet.Unknown.IsSubsetOf(union), Is.False);
        }
    }

    [Test]
    public void SummaryCoverageUsesExceptionSubtypingWithoutErasingUnknownThrows()
    {
        var compilation = TestCompilation.Create("ExceptionCoverage", "public class C { }");
        var root = compilation.GetTypeByMetadataName("System.Exception")!;
        var child = compilation.GetTypeByMetadataName("System.ArgumentException")!;
        var declared = EffectSummaryOperations.Throw(EffectThrowSet.Create([root]));
        var actual = EffectSummaryOperations.Throw(EffectThrowSet.Create([child]));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(EffectContractMappings.Covers(actual, declared), Is.True);
            Assert.That(EffectContractMappings.Covers(declared, actual), Is.False);
            Assert.That(EffectContractMappings.Covers(EffectSummaryOperations.Throw(EffectThrowSet.Unknown), declared), Is.False);
            Assert.That(EffectContractMappings.Covers(EffectSummary.Empty, declared), Is.True);
        }
    }

    [Test]
    public void SequencingSeveralEffectsKeepsTheUnionAndBottomIdentity()
    {
        var region = EffectRegionSet.Create(EffectRegionId.Parameter(0));
        var effects = new[] { EffectSummaryOperations.Read(region), EffectSummaryOperations.Write(region),
            EffectSummaryOperations.Allocate(EffectAllocationKind.Native), EffectSummaryOperations.DirectCall() };
        var result = EffectSummaryOperations.Join(effects);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Reads, Is.EqualTo(region));
            Assert.That(result.Writes, Is.EqualTo(region));
            Assert.That(result.Allocation, Is.EqualTo(EffectAllocationKind.Native));
            Assert.That(result.Uncertainty, Is.EqualTo(EffectUncertainty.DirectCall));
            Assert.That(EffectSummaryOperations.JoinFrom(EffectSummary.Bottom, effects), Is.EqualTo(result));
            Assert.That(EffectSummaryOperations.Remap(EffectSummary.Bottom, region, []), Is.EqualTo(EffectSummary.Bottom));
        }
    }

    [Test]
    public void HandlingThrowsDoesNotEraseMutationsOrReviveAnUnreachablePath()
    {
        var source = EffectSummaryOperations.Join(EffectSummaryOperations.Write(EffectRegionSet.Create(EffectRegionId.Ambient)),
            EffectSummaryOperations.Throw(EffectThrowSet.Unknown));
        var handled = EffectSummaryOperations.WithThrows(source, EffectThrowSet.Empty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handled.Throws.IsEmpty, Is.True);
            Assert.That(handled.Writes, Is.EqualTo(source.Writes));
            Assert.That(handled.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(EffectSummaryOperations.WithThrows(EffectSummary.Bottom, EffectThrowSet.Unknown), Is.EqualTo(EffectSummary.Bottom));
        }
    }
}
