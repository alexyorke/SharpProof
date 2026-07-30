namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class NullnessDomainTests
{
    private static readonly NullnessValue[] Samples = Enum.GetValues<NullnessValue>();
    private readonly NullnessDomain _domain = NullnessDomain.Instance;

    [Test]
    public void OrderAndJoinSatisfyFiniteLatticeLaws()
    {
        DomainLawAssertions.AssertOrderAndJoinLaws(_domain, Samples);
    }

    [Test]
    public void RefinementTransfersAreMonotone()
    {
        DomainLawAssertions.AssertMonotone(_domain, Samples, _domain.AssumeNull);
        DomainLawAssertions.AssertMonotone(_domain, Samples, _domain.AssumeNonNull);
    }

    [Test]
    public void NullAndNonNullJoinToMaybeNull()
    {
        Assert.That(
            _domain.Join(NullnessValue.Null, NullnessValue.NonNull),
            Is.EqualTo(NullnessValue.MaybeNull));
    }

    [Test]
    public void ContradictoryAssumptionsReachBottom()
    {
        Assert.That(
            (
                _domain.AssumeNull(NullnessValue.NonNull),
                _domain.AssumeNonNull(NullnessValue.Null)
            ),
            Is.EqualTo((NullnessValue.Bottom, NullnessValue.Bottom)));
    }

    [Test]
    public void WideningTerminatesImmediately()
    {
        var value = NullnessValue.Bottom;
        value = _domain.Widen(value, NullnessValue.Null);
        value = _domain.Widen(value, NullnessValue.NonNull);
        var stable = _domain.Widen(value, NullnessValue.Null);

        Assert.That(value, Is.EqualTo(NullnessValue.MaybeNull));
        Assert.That(stable, Is.EqualTo(value));
    }

    [Test]
    public void HavocIsConservative()
    {
        DomainLawAssertions.AssertConservativeHavoc(_domain, Samples);
    }

    [Test]
    public void ClosedDomainComparisonsAndInvalidValuesAreExplicit()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _domain.Compare(NullnessValue.Null, NullnessValue.Null),
                Is.Zero);
            Assert.That(
                _domain.Compare(NullnessValue.MaybeNull, NullnessValue.Null),
                Is.Positive);
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() =>
                    _domain.AssumeNull((NullnessValue)int.MaxValue)));
        }
    }
}
