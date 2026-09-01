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
    public void SignedCarrierEndpointsCanonicalizeToUnboundedBounds()
    {
        var fullRange = _domain.Range(long.MinValue, long.MaxValue);
        var lowerEndpoint = _domain.Range(long.MinValue, 0);
        var upperEndpoint = _domain.Range(0, long.MaxValue);

        Assert.That(fullRange, Is.EqualTo(_domain.Top));
        Assert.That(fullRange.LowerBound, Is.Null);
        Assert.That(fullRange.UpperBound, Is.Null);
        Assert.That(lowerEndpoint.LowerBound, Is.Null);
        Assert.That(upperEndpoint.UpperBound, Is.Null);
        Assert.That(_domain.AreEquivalent(fullRange, _domain.Top), Is.True);
    }

    [Test]
    public void SignedCarrierEndpointsCanonicalizeForCongruentIntervals()
    {
        var explicitLowerEndpoint = _domain.Create(long.MinValue, 0, 3, 0);
        var explicitUpperEndpoint = _domain.Create(0, long.MaxValue, 3, 0);

        Assert.That(
            explicitLowerEndpoint,
            Is.EqualTo(_domain.Create(null, 0, 3, 0)));
        Assert.That(explicitLowerEndpoint.LowerBound, Is.Null);
        Assert.That(
            explicitUpperEndpoint,
            Is.EqualTo(_domain.Create(0, null, 3, 0)));
        Assert.That(explicitUpperEndpoint.UpperBound, Is.Null);
    }

    [Test]
    public void Int64BoundaryRangesHaveOneCanonicalRepresentation()
    {
        var implicitLower = _domain.Range(null, 5);
        var explicitLower = _domain.Range(long.MinValue, 5);
        var implicitUpper = _domain.Range(-5, null);
        var explicitUpper = _domain.Range(-5, long.MaxValue);

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
    public void RefinementTransfersAreMonotone()
    {
        DomainLawAssertions.AssertMonotone(
            _domain,
            Samples,
            value => _domain.AssumeAtLeast(value, 0));
        DomainLawAssertions.AssertMonotone(
            _domain,
            Samples,
            value => _domain.AssumeAtMost(value, 1));
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
    public void ClosedDomainJoinAndOrderAreConsistent()
    {
        Assert.That(
            _domain.LessThanOrEqual(_domain.Bottom, _domain.Top),
            Is.True);
        Assert.That(_domain.AreEquivalent(
            _domain.Join(_domain.Constant(2), _domain.Constant(6)),
            _domain.Join(_domain.Constant(6), _domain.Constant(2))), Is.True);
    }
}
