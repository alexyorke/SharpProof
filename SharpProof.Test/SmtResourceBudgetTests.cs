using NUnit.Framework;
using SearchLib.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SmtResourceBudgetTests
{
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
    public void GetWallClockSafetyNet_UnderflowingBudget_SaturatesAtMinValue()
    {
        var underflowingTicks = TimeSpan.MinValue.Ticks / SmtResourceBudget.WallClockSafetyFactor - 1;
        var budget = TimeSpan.FromTicks(underflowingTicks);

        var safetyNet = SmtResourceBudget.GetWallClockSafetyNet(budget);

        Assert.That(safetyNet, Is.EqualTo(TimeSpan.MinValue));
    }
}