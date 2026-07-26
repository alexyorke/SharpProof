namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectLatticeTests {
    [Test]
    public void JoinSatisfiesFiniteLatticeLaws() {
        var samples = CreateSamples();
        var domain = EffectSummaryDomain.Instance;

        foreach (var value in samples) {
            Assert.That(domain.LessThanOrEqual(value, value), Is.True);
            Assert.That(domain.LessThanOrEqual(domain.Bottom, value), Is.True);
            Assert.That(domain.LessThanOrEqual(value, domain.Top), Is.True);
            Assert.That(domain.Join(value, value), Is.EqualTo(value));
        }

        foreach (var left in samples)
            foreach (var right in samples) {
                var join = domain.Join(left, right);
                Assert.That(domain.LessThanOrEqual(left, join), Is.True);
                Assert.That(domain.LessThanOrEqual(right, join), Is.True);
                Assert.That(join, Is.EqualTo(domain.Join(right, left)));
            }
    }

    [Test]
    public void ProjectionIsMonotoneUnderPublicUnknownOrder() {
        var domain = EffectSummaryDomain.Instance;
        var samples = CreateSamples();
        var closure = samples
            .Concat(
                from left in samples
                from right in samples
                select domain.Join(left, right))
            .Distinct()
            .ToImmutableArray();

        foreach (var left in closure)
            foreach (var right in closure)
                if (domain.LessThanOrEqual(left, right))
                    Assert.That(
                        ProjectionLessThanOrEqual(
                            EffectSummaryProjector.Project(left),
                            EffectSummaryProjector.Project(right)),
                        Is.True,
                        "Projection is not monotone for a sampled ordered pair.");
    }

    private static ImmutableArray<EffectSummary> CreateSamples() {
        var compilation = EffectTestHost.CreateCompilation("public sealed class Sample { }");
        var exception = EffectTestHost.RequireType(
            compilation,
            "System.InvalidOperationException");
        return [
            EffectSummary.Bottom,
            EffectSummary.Empty,
            Summary(reads: EffectRegionSet.Create(EffectRegionId.Receiver)),
            Summary(writes: EffectRegionSet.Create(EffectRegionId.Parameter(0))),
            Summary(allocation: EffectAllocationKind.Managed),
            Summary(
                capabilities: new EffectCapabilitySet(
                    EffectCapabilityKind.Console |
                    EffectCapabilityKind.Synchronization)),
            Summary(throws: EffectThrowSet.Create([exception])),
            Summary(termination: EffectTermination.MayDiverge),
            new EffectSummary(
                EffectRegionSet.Unknown,
                EffectRegionSet.Unknown,
                EffectAllocationKind.Unknown,
                EffectCapabilitySet.Unknown,
                EffectThrowSet.Unknown,
                EffectTermination.Unknown,
                EffectCompleteness.Incomplete,
                EffectUncertainty.UnmodeledCall),
            EffectSummary.Top
        ];
    }

    private static EffectSummary Summary(
        EffectRegionSet reads = default,
        EffectRegionSet writes = default,
        EffectAllocationKind allocation = EffectAllocationKind.None,
        EffectCapabilitySet capabilities = default,
        EffectThrowSet throws = default,
        EffectTermination termination = EffectTermination.Terminates) =>
        new(
            reads,
            writes,
            allocation,
            capabilities,
            throws,
            termination,
            EffectCompleteness.Complete);

    private static bool ProjectionLessThanOrEqual(
        EffectProjection left,
        EffectProjection right) {
        if (!right.IsComplete)
            return true;
        if (!left.IsComplete)
            return false;
        return (left.Effects & ~right.Effects) == 0 &&
               (left.Capabilities & ~right.Capabilities) == 0;
    }
}
