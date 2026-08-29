using System.Globalization;

namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class IntervalDomainTests
{
    private readonly IntervalDomain _domain = IntervalDomain.Instance;

    private static IReadOnlyList<IntervalValue> Samples => [
        IntervalValue.Bottom,
        IntervalValue.Top,
        IntervalValue.Constant(-2),
        IntervalValue.Constant(-1),
        IntervalValue.Constant(0),
        IntervalValue.Constant(1),
        IntervalValue.Constant(2),
        IntervalValue.Range(-2, 2),
        IntervalValue.Range(0, null),
        IntervalValue.Congruent(-4, 4, 2, 0),
        IntervalValue.Congruent(-3, 5, 2, 1),
        IntervalValue.Congruent(null, null, 2, 0)
    ];

    [Test]
    public void OrderAndJoinSatisfySampledLatticeLaws()
    {
        DomainLawAssertions.AssertOrderAndJoinLaws(_domain, Samples);
    }

    [Test]
    public void CongruentBoundsAreCanonicalized()
    {
        var even = _domain.Create(-5, 5, 2, 0);

        Assert.That(even.LowerBound, Is.EqualTo(-4));
        Assert.That(even.UpperBound, Is.EqualTo(4));
        Assert.That(even.Contains(-2), Is.True);
        Assert.That(even.Contains(3), Is.False);
        Assert.That(_domain.Create(2, 2, 2, 1), Is.EqualTo(IntervalValue.Bottom));
    }

    [Test]
    public void UnboundedCongruenceUsesEffectiveInt64Endpoints()
    {
        var implicitBounds = _domain.Create(null, null, 10, 0);
        var explicitBounds = _domain.Create(
            -9223372036854775800L,
            9223372036854775800L,
            10,
            0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(implicitBounds, Is.EqualTo(explicitBounds));
            Assert.That(
                _domain.LessThanOrEqual(implicitBounds, explicitBounds),
                Is.True);
            Assert.That(
                _domain.LessThanOrEqual(explicitBounds, implicitBounds),
                Is.True);
            Assert.That(
                _domain.AddConstant(implicitBounds, 1),
                Is.EqualTo(_domain.AddConstant(explicitBounds, 1)));
            Assert.That(
                _domain.AddConstant(implicitBounds, 1),
                Is.EqualTo(_domain.Create(
                    -9223372036854775799L,
                    9223372036854775801L,
                    10,
                    1)));
            Assert.That(
                _domain.AddConstant(implicitBounds, 1),
                Is.Not.EqualTo(_domain.Top));
        }
    }

    [Test]
    public void Int64BoundaryRangesHaveOneCanonicalRepresentation()
    {
        var implicitLower = _domain.Range(null, 5);
        var explicitLower = _domain.Range(long.MinValue, 5);
        var implicitUpper = _domain.Range(-5, null);
        var explicitUpper = _domain.Range(-5, long.MaxValue);
        var addedLower = _domain.Add(
            _domain.Range(long.MinValue, 0),
            _domain.Range(0, 5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(explicitLower, Is.EqualTo(implicitLower));
            Assert.That(
                explicitLower.GetHashCode(),
                Is.EqualTo(implicitLower.GetHashCode()));
            Assert.That(explicitUpper, Is.EqualTo(implicitUpper));
            Assert.That(
                explicitUpper.GetHashCode(),
                Is.EqualTo(implicitUpper.GetHashCode()));
            Assert.That(addedLower, Is.EqualTo(implicitLower));
        }
    }

    [Test]
    public void JoinComputesCongruenceHull()
    {
        var joined = _domain.Join(_domain.Constant(2), _domain.Constant(6));

        Assert.That(joined.LowerBound, Is.EqualTo(2));
        Assert.That(joined.UpperBound, Is.EqualTo(6));
        Assert.That(joined.Modulus, Is.EqualTo(new BigInteger(4)));
        Assert.That(joined.Remainder, Is.EqualTo(new BigInteger(2)));
        Assert.That(joined.Contains(4), Is.False);
    }

    [Test]
    public void EndpointJoinRemainsTheLeastCongruenceUpperBound()
    {
        var joined = _domain.Join(
            _domain.Constant(long.MinValue),
            _domain.Constant(long.MaxValue));
        var divisibleByThree = _domain.Create(null, null, 3, 1);

        Assert.That(
            joined.Modulus,
            Is.EqualTo(BigInteger.Parse(
                "18446744073709551615",
                CultureInfo.InvariantCulture)));
        Assert.That(_domain.LessThanOrEqual(joined, divisibleByThree), Is.True);
    }

    [Test]
    public void ArithmeticAndRefinementTransfersAreMonotone()
    {
        DomainLawAssertions.AssertMonotone(
            _domain,
            Samples,
            value => _domain.AddConstant(value, 3));
        DomainLawAssertions.AssertMonotone(
            _domain,
            Samples,
            value => _domain.AssumeAtLeast(value, 0));
        DomainLawAssertions.AssertMonotone(
            _domain,
            Samples,
            value => _domain.AssumeAtMost(value, 1));
        DomainLawAssertions.AssertBinaryMonotone(_domain, Samples, _domain.Add);
    }

    [Test]
    public void ArithmeticOverflowFailsClosed()
    {
        Assert.That(
            _domain.Add(_domain.Constant(long.MaxValue), _domain.Constant(1)),
            Is.EqualTo(_domain.Top));
    }

    [Test]
    public void PotentialEndpointOverflowFailsClosed()
    {
        Assert.That(
            _domain.Add(
                _domain.Range(-485, 292),
                _domain.Range(null, 386)),
            Is.EqualTo(_domain.Top));
    }

    [Test]
    public void WideningTerminatesForAscendingBounds()
    {
        var previous = _domain.Constant(0);
        for (var upper = 1; upper <= 64; upper++)
        {
            var next = _domain.Range(0, upper);
            var widened = _domain.Widen(previous, next);
            Assert.That(_domain.LessThanOrEqual(previous, widened), Is.True);
            Assert.That(_domain.LessThanOrEqual(next, widened), Is.True);
            previous = widened;
        }

        var stable = _domain.Widen(previous, _domain.Range(0, 1_000));
        Assert.That(previous.UpperBound, Is.Null);
        Assert.That(stable, Is.EqualTo(previous));
    }

    [Test]
    public void HavocIsConservative()
    {
        DomainLawAssertions.AssertConservativeHavoc(_domain, Samples);
    }

    [Test]
    public void ClosedDomainFacadeUsesSharpProofJoinAndOrder()
    {
        Assert.That(
            _domain.Merge(_domain.Constant(2), _domain.Constant(6)),
            Is.EqualTo(_domain.Join(_domain.Constant(2), _domain.Constant(6))));
        Assert.That(
            _domain.Compare(_domain.Bottom, _domain.Top),
            Is.LessThan(0));
    }

    [Test]
    public void FullRangeUsesTheCanonicalTopRepresentation()
    {
        var fullRange = _domain.Range(long.MinValue, long.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fullRange, Is.EqualTo(_domain.Top));
            Assert.That(fullRange.GetHashCode(), Is.EqualTo(_domain.Top.GetHashCode()));
        }
    }
}
