using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerResourceAccountingCoverageTests
{
    [Test]
    public void CounterBelowItsStartingValueIsRejected()
    {
        long count = 11;
        var accounting = new MethodResourceBudget(() => count, 2, 10);
        count = 10;

        var exception = Assert.Throws<InvalidOperationException>(new Action(() => accounting.TryStartQuery()));

        Assert.That(exception!.Message, Does.Contain("must be monotonic"));
    }

    [Test]
    public void NondecreasingCounterRespectsTheDeclaredQueryAndMethodLimits()
    {
        long count = 11;
        var accounting = new MethodResourceBudget(() => count, 2, 10);
        count = 12;
        Assert.That(accounting.TryStartQuery(), Is.True);
        Assert.That(accounting.IsExceeded, Is.False);

        count = 20;
        Assert.That(accounting.TryStartQuery(), Is.False);
        Assert.That(accounting.IsExceeded, Is.False);
        count = 21;
        Assert.That(accounting.IsExceeded, Is.False);
        count = 22;
        Assert.That(accounting.IsExceeded, Is.True);
    }

    [Test]
    public void NegativeInitialCounterIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(new Action(() =>
        {
            _ = new MethodResourceBudget(() => -1, 2, 10);
        }));

        Assert.That(exception!.Message, Does.Contain("cannot be negative"));
    }

    [TestCase(0u, 1u, "queryRlimit")]
    [TestCase(2u, 1u, "methodRlimit")]
    public void InvalidAccountingLimitsAreRejected(uint queryRlimit, uint methodRlimit, string parameter)
    {
        System.ArgumentNullException.ThrowIfNull(parameter);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
        {
            _ = new MethodResourceBudget(null, queryRlimit, methodRlimit);
        }));

        Assert.That(exception!.ParamName, Is.EqualTo(parameter));
    }
}
