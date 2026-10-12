using System.Globalization;

namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class IntervalValueTests
{
    [Test]
    public void EmptyAndBoundedIntervalsRejectValuesOutsideTheirSets()
    {
        var bottom = IntervalValue.Bottom;
        var bounded = IntervalValue.Range(-2, 3);
        var singleton = IntervalValue.Constant(2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bottom.Contains(0), Is.False);
            Assert.That(bottom.GetHashCode(), Is.Zero);
            Assert.That(bottom.ToString(), Is.EqualTo("bottom"));
            Assert.That(bounded.Contains(-3), Is.False);
            Assert.That(bounded.Contains(4), Is.False);
            Assert.That(bounded.Contains(0), Is.True);
            Assert.That(singleton.Contains(2), Is.True);
            Assert.That(singleton.Contains(3), Is.False);
        }
    }

    [Test]
    public void SingletonValueRequiresAnExactInterval()
    {
        var singleton = IntervalValue.Constant(-2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(singleton.IsSingleton, Is.True);
            Assert.That(singleton.SingletonValue, Is.EqualTo(-2));
            Assert.That(IntervalValue.Range(-2, 2).IsSingleton, Is.False);
            Assert.That(IntervalValue.Bottom.IsSingleton, Is.False);
        }

        Assert.Throws<InvalidOperationException>((Action)(() => _ = IntervalValue.Range(-2, 2).SingletonValue));
        Assert.Throws<InvalidOperationException>((Action)(() => _ = IntervalValue.Bottom.SingletonValue));
    }

    [Test]
    public void EqualityAndFormattingReflectCanonicalValueAcrossCultures()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            var odd = IntervalValue.Congruent(-3, -1, 2, 1);
            var equivalent = IntervalValue.Congruent(-3, -1, 2, -1);
            var other = IntervalValue.Range(-3, -1);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(odd.ToString(), Is.EqualTo("[-3, -1] mod 2 = 1"));
                Assert.That(IntervalValue.Constant(-2).ToString(), Is.EqualTo("[-2, -2]"));
                Assert.That(odd.Equals((object)equivalent), Is.True);
                Assert.That(odd.Equals((object)"not an interval"), Is.False);
                Assert.That(odd == equivalent, Is.True);
                Assert.That(odd != other, Is.True);
                Assert.That(odd.GetHashCode(), Is.EqualTo(equivalent.GetHashCode()));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
    }
}
