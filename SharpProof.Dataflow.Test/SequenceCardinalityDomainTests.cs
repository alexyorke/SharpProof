namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class SequenceCardinalityDomainTests
{
    private readonly SequenceCardinalityDomain _domain = SequenceCardinalityDomain.Instance;

    private static IReadOnlyList<SequenceCardinalityValue> Samples => [
        SequenceCardinalityValue.Bottom,
        SequenceCardinalityValue.Empty,
        SequenceCardinalityValue.KnownLength(1),
        SequenceCardinalityValue.KnownLength(2),
        SequenceCardinalityValue.NonEmpty,
        SequenceCardinalityValue.Top,
        SequenceCardinalityDomain.Instance.Create(
            SequenceCardinalityKind.Top,
            IntervalValue.Range(0, 3)),
        SequenceCardinalityDomain.Instance.Create(
            SequenceCardinalityKind.NonEmpty,
            IntervalValue.Congruent(1, 7, 2, 1))
    ];

    [Test]
    public void OrderAndJoinSatisfySampledProductLaws()
    {
        DomainLawAssertions.AssertOrderAndJoinLaws(_domain, Samples);
    }

    [Test]
    public void LengthCanonicalizesCardinalityKind()
    {
        var zero = _domain.Create(
            SequenceCardinalityKind.Top,
            IntervalValue.Constant(0));
        var positive = _domain.Create(
            SequenceCardinalityKind.Top,
            IntervalValue.Range(2, 5));

        Assert.That(zero.Kind, Is.EqualTo(SequenceCardinalityKind.Empty));
        Assert.That(positive.Kind, Is.EqualTo(SequenceCardinalityKind.NonEmpty));
        Assert.That(
            _domain.Create(
                SequenceCardinalityKind.NonEmpty,
                IntervalValue.Constant(0)),
            Is.EqualTo(SequenceCardinalityValue.Bottom));
    }

    [Test]
    public void EmptyAndNonEmptyJoinToTopWithLengthHull()
    {
        var joined = _domain.Join(
            SequenceCardinalityValue.Empty,
            SequenceCardinalityValue.KnownLength(2));

        Assert.That(joined.Kind, Is.EqualTo(SequenceCardinalityKind.Top));
        Assert.That(joined.Length.LowerBound, Is.EqualTo(0));
        Assert.That(joined.Length.UpperBound, Is.EqualTo(2));
    }

    [Test]
    public void CardinalityTransfersAreMonotone()
    {
        DomainLawAssertions.AssertMonotone(
            _domain,
            Samples,
            value => _domain.Append(value, 1));
        DomainLawAssertions.AssertMonotone(_domain, Samples, _domain.AssumeEmpty);
        DomainLawAssertions.AssertMonotone(_domain, Samples, _domain.AssumeNonEmpty);
        DomainLawAssertions.AssertBinaryMonotone(_domain, Samples, _domain.Concat);
    }

    [Test]
    public void AppendAndConcatTrackLengths()
    {
        var appended = _domain.Append(SequenceCardinalityValue.Empty);
        var concatenated = _domain.Concat(
            SequenceCardinalityValue.KnownLength(2),
            SequenceCardinalityValue.KnownLength(3));

        Assert.That(appended, Is.EqualTo(SequenceCardinalityValue.KnownLength(1)));
        Assert.That(concatenated, Is.EqualTo(SequenceCardinalityValue.KnownLength(5)));
    }

    [Test]
    public void WideningTerminatesForGrowingLengths()
    {
        var previous = SequenceCardinalityValue.Empty;
        for (var length = 1; length <= 64; length++)
        {
            var next = _domain.Create(
                SequenceCardinalityKind.Top,
                IntervalValue.Range(0, length));
            var widened = _domain.Widen(previous, next);
            Assert.That(_domain.LessThanOrEqual(previous, widened), Is.True);
            Assert.That(_domain.LessThanOrEqual(next, widened), Is.True);
            previous = widened;
        }

        var stable = _domain.Widen(
            previous,
            _domain.Create(
                SequenceCardinalityKind.Top,
                IntervalValue.Range(0, 1_000)));
        Assert.That(previous, Is.EqualTo(SequenceCardinalityValue.Top));
        Assert.That(stable, Is.EqualTo(previous));
    }

    [Test]
    public void HavocIsConservative()
    {
        DomainLawAssertions.AssertConservativeHavoc(_domain, Samples);
    }
}
