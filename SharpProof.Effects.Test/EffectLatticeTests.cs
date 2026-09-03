namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectLatticeTests
{
    [Test]
    public void JoinSatisfiesFiniteLatticeLaws()
    {
        var samples = Samples;
        var domain = EffectSummaryDomain.Instance;

        foreach (var value in samples)
        {
            Assert.That(domain.LessThanOrEqual(value, value), Is.True);
            Assert.That(domain.LessThanOrEqual(domain.Bottom, value), Is.True);
            Assert.That(domain.LessThanOrEqual(value, domain.Top), Is.True);
            Assert.That(domain.Join(value, value), Is.EqualTo(value));
        }

        foreach (var left in samples)
        {
            foreach (var right in samples)
            {
                var join = domain.Join(left, right);
                Assert.That(domain.LessThanOrEqual(left, join), Is.True);
                Assert.That(domain.LessThanOrEqual(right, join), Is.True);
                Assert.That(join, Is.EqualTo(domain.Join(right, left)));
            }
        }
    }

    [Test]
    public void ProjectionIsMonotoneUnderPublicUnknownOrder()
    {
        var domain = EffectSummaryDomain.Instance;
        var samples = Samples;
        var closure = samples
            .Concat(
                from left in samples
                from right in samples
                select domain.Join(left, right))
            .Distinct()
            .ToImmutableArray();

        foreach (var left in closure)
        {
            foreach (var right in closure)
            {
                if (domain.LessThanOrEqual(left, right))
                {
                    Assert.That(
                        ProjectionLessThanOrEqual(
                            EffectSummaryProjector.Project(left),
                            EffectSummaryProjector.Project(right)),
                        Is.True,
                        "Projection is not monotone for a sampled ordered pair.");
                }
            }
        }
    }

    [Test]
    public void ThrowSetsCanonicalizeConstructedGenericTypesIndependentlyOfInsertionOrder()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class GenericException<T> : System.Exception {
            }

            public static class Subject {
                public static void Compare(
                    GenericException<int> first,
                    GenericException<string> second) {
                }
            }
            """);
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Subject",
            "Compare");
        var integerException = (INamedTypeSymbol)method.Parameters[0].Type;
        var stringException = (INamedTypeSymbol)method.Parameters[1].Type;
        var forward = EffectThrowSet.Create([
            integerException,
            stringException
        ]);
        var reverse = EffectThrowSet.Create([
            stringException,
            integerException
        ]);
        var forwardUnion = EffectThrowSet.Create([integerException])
            .Union(EffectThrowSet.Create([stringException]));
        var reverseUnion = EffectThrowSet.Create([stringException])
            .Union(EffectThrowSet.Create([integerException]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                EffectSymbolComparer<INamedTypeSymbol>.Instance.Compare(
                    integerException,
                    stringException),
                Is.Not.Zero);
            Assert.That(reverse, Is.EqualTo(forward));
            Assert.That(
                reverse.GetHashCode(),
                Is.EqualTo(forward.GetHashCode()));
            Assert.That(reverseUnion, Is.EqualTo(forwardUnion));
            Assert.That(
                reverseUnion.GetHashCode(),
                Is.EqualTo(forwardUnion.GetHashCode()));
            Assert.That(
                reverse.Types,
                Is.EqualTo(forward.Types)
                    .Using<INamedTypeSymbol>(SymbolEqualityComparer.Default));
        }
    }

    private static ImmutableArray<EffectSummary> CreateSamples()
    {
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

    private static ImmutableArray<EffectSummary> Samples { get; } = CreateSamples();

    private static EffectSummary Summary(
        EffectRegionSet reads = default,
        EffectRegionSet writes = default,
        EffectAllocationKind allocation = EffectAllocationKind.None,
        EffectCapabilitySet capabilities = default,
        EffectThrowSet throws = default,
        EffectTermination termination = EffectTermination.Terminates)
    {
        return new(
            reads,
            writes,
            allocation,
            capabilities,
            throws,
            termination,
            EffectCompleteness.Complete);
    }

    private static bool ProjectionLessThanOrEqual(
        EffectProjection left,
        EffectProjection right)
    {
        if (!right.IsComplete)
        {
            return true;
        }

        if (!left.IsComplete)
        {
            return false;
        }

        return (left.Effects & ~right.Effects) == 0 &&
               (left.Capabilities & ~right.Capabilities) == 0;
    }
}
