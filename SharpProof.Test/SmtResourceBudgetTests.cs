using NUnit.Framework;
using SearchLib.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SmtResourceBudgetTests
{
    [Test]
    public void ResourceLimits_UseSharedSaturationAndMinimumPolicy()
    {
        Assert.That(SmtResourceBudget.GetRlimit(TimeSpan.Zero), Is.EqualTo(1u));
        Assert.That(SmtResourceBudget.GetRlimit(TimeSpan.MaxValue), Is.EqualTo(uint.MaxValue));
        Assert.That(SmtResourceBudget.GetMethodRlimitBudget(TimeSpan.FromMilliseconds(-1)), Is.Zero);
    }

    [Test]
    public void GetWallClockSafetyNet_LargestSafeBudget_ScalesBySafetyFactor()
    {
        var safeTicks = TimeSpan.MaxValue.Ticks / SmtResourceBudget.WallClockSafetyFactor;
        var budget = TimeSpan.FromTicks(safeTicks);

        var safetyNet = SmtResourceBudget.GetWallClockSafetyNet(budget);

        Assert.That(safetyNet.Ticks, Is.EqualTo(safeTicks * SmtResourceBudget.WallClockSafetyFactor));
    }

    [Test]
    public void GetWallClockSafetyNet_OverflowingBudget_SaturatesAtMaxValue()
    {
        var overflowingTicks = TimeSpan.MaxValue.Ticks / SmtResourceBudget.WallClockSafetyFactor + 1;
        var budget = TimeSpan.FromTicks(overflowingTicks);

        var safetyNet = SmtResourceBudget.GetWallClockSafetyNet(budget);

        Assert.That(safetyNet, Is.EqualTo(TimeSpan.MaxValue));
    }

    [Test]
    public void GetWallClockSafetyNet_NegativeBudget_ClampsToZero()
    {
        var underflowingTicks = TimeSpan.MinValue.Ticks / SmtResourceBudget.WallClockSafetyFactor - 1;
        var budget = TimeSpan.FromTicks(underflowingTicks);

        var safetyNet = SmtResourceBudget.GetWallClockSafetyNet(budget);

        Assert.That(safetyNet, Is.EqualTo(TimeSpan.Zero));
    }
}
